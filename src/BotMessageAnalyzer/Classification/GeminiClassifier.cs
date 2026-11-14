using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using BotMessageAnalyzer.Input;

namespace BotMessageAnalyzer.Classification;

public sealed class GeminiClassifier(
    GeminiClient? client,
    ResultCache cache,
    string model,
    int batchSize,
    string classifyPrompt,
    string mergePrompt,
    Action<string>? log = null) : IClassifier
{
    private static readonly JsonSerializerOptions Compact = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static readonly JsonNode ClassifySchema = JsonNode.Parse("""
        {
          "type": "array",
          "items": {
            "type": "object",
            "properties": {
              "id": { "type": "string" },
              "verdict": { "type": "string", "enum": ["Answered", "NotUnderstood", "WrongAnswer"] },
              "topic": { "type": "string" },
              "reason": { "type": "string" }
            },
            "required": ["id", "verdict", "topic", "reason"]
          }
        }
        """)!;

    private static readonly JsonNode MergeSchema = JsonNode.Parse("""
        {
          "type": "array",
          "items": {
            "type": "object",
            "properties": {
              "label": { "type": "string" },
              "canonical": { "type": "string" }
            },
            "required": ["label", "canonical"]
          }
        }
        """)!;

    private sealed record Judgement(Verdict Verdict, string Topic, string Reason);

    public string TurnKey(Turn turn) => Hash(classifyPrompt, model, turn.CustomerText, turn.BotReply ?? "");

    public int CountUncached(IEnumerable<Turn> turns) => turns.Count(t => !cache.TryGet(TurnKey(t), out _));

    public async Task<IReadOnlyList<TurnResult>> ClassifyAsync(IReadOnlyList<Turn> turns, CancellationToken cancellationToken)
    {
        var results = new TurnResult?[turns.Count];
        var missing = new List<int>();

        for (var i = 0; i < turns.Count; i++)
        {
            if (cache.TryGet(TurnKey(turns[i]), out var cached) && ReadCached(turns[i], cached) is { } hit)
            {
                results[i] = hit;
            }
            else
            {
                missing.Add(i);
            }
        }

        var batchNumber = 0;
        var batchCount = (missing.Count + batchSize - 1) / batchSize;
        foreach (var chunk in missing.Chunk(batchSize))
        {
            log?.Invoke($"Classifying batch {++batchNumber} of {batchCount}.");
            var batch = chunk.Select(i => turns[i]).ToList();
            var judged = await JudgeAsync(batch, cancellationToken);

            foreach (var i in chunk)
            {
                var turn = turns[i];
                if (judged is not null && judged.TryGetValue(Id(turn), out var judgement))
                {
                    results[i] = new TurnResult(turn, judgement.Verdict, judgement.Topic, judgement.Reason, VerdictSource.Model);
                    cache.Put(TurnKey(turn), new JsonObject
                    {
                        ["verdict"] = judgement.Verdict.ToString(),
                        ["topic"] = judgement.Topic,
                        ["reason"] = judgement.Reason
                    });
                }
                else
                {
                    results[i] = new TurnResult(turn, Verdict.Unclassified, null, FailureReason(judged is null), VerdictSource.Failed);
                }
            }
        }
        return results!;
    }

    public async Task<IReadOnlyDictionary<string, string>?> MergeTopicsAsync(
        IReadOnlyList<string> labels, CancellationToken cancellationToken)
    {
        var key = Hash(mergePrompt, model, string.Join('\n', labels));
        if (cache.TryGet(key, out var cached) && cached.TryGetProperty("map", out var stored)
            && stored.ValueKind == JsonValueKind.Object)
        {
            return stored.EnumerateObject()
                .Where(p => p.Value.ValueKind == JsonValueKind.String)
                .ToDictionary(p => p.Name, p => p.Value.GetString()!, StringComparer.Ordinal);
        }
        if (client is null)
        {
            return null;
        }

        log?.Invoke($"Merging {labels.Count} topic labels.");
        var text = await client.GenerateJsonAsync(
            mergePrompt + "\n\nLabels:\n" + JsonSerializer.Serialize(labels, Compact), MergeSchema, cancellationToken);
        var map = text is null ? null : ParseMerge(text, labels);
        if (map is null)
        {
            return null;
        }

        var stash = new JsonObject();
        foreach (var (label, canonical) in map)
        {
            stash[label] = canonical;
        }
        cache.Put(key, new JsonObject { ["map"] = stash });
        return map;
    }

    private async Task<Dictionary<string, Judgement>?> JudgeAsync(IReadOnlyList<Turn> batch, CancellationToken cancellationToken)
    {
        if (client is null || client.Fatal)
        {
            return null;
        }

        var items = batch.Select(t => new { id = Id(t), customer = t.CustomerText, bot = t.BotReply });
        var text = await client.GenerateJsonAsync(
            classifyPrompt + "\n\nTurns:\n" + JsonSerializer.Serialize(items, Compact), ClassifySchema, cancellationToken);
        return text is null ? null : ParseJudgements(text, batch.Select(Id).ToHashSet(StringComparer.Ordinal));
    }

    private static Dictionary<string, Judgement>? ParseJudgements(string text, HashSet<string> requested)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var judgements = new Dictionary<string, Judgement>(StringComparer.Ordinal);
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }
                var id = Read(item, "id");
                if (id is null || !requested.Contains(id) || judgements.ContainsKey(id))
                {
                    continue;
                }
                if (!Enum.TryParse<Verdict>(Read(item, "verdict"), ignoreCase: true, out var verdict)
                    || verdict == Verdict.Unclassified)
                {
                    continue;
                }
                judgements[id] = new Judgement(verdict, Read(item, "topic") ?? "", Read(item, "reason") ?? "");
            }
            return judgements;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static Dictionary<string, string>? ParseMerge(string text, IReadOnlyList<string> labels)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var requested = labels.ToHashSet(StringComparer.Ordinal);
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }
                var label = Read(item, "label");
                var canonical = Read(item, "canonical")?.Trim();
                if (label is not null && requested.Contains(label) && !string.IsNullOrEmpty(canonical))
                {
                    map.TryAdd(label, canonical);
                }
            }
            return map;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static TurnResult? ReadCached(Turn turn, JsonElement cached)
    {
        if (Enum.TryParse<Verdict>(Read(cached, "verdict"), ignoreCase: true, out var verdict)
            && verdict != Verdict.Unclassified)
        {
            return new TurnResult(turn, verdict, Read(cached, "topic"), Read(cached, "reason"), VerdictSource.Model);
        }
        return null;
    }

    private string FailureReason(bool requestFailed) =>
        client is null ? "not cached and no API key"
        : requestFailed ? $"model request failed: {client.LastError}"
        : "model gave no answer for this turn";

    private static string? Read(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string Id(Turn turn) => $"{turn.ConversationId}:{turn.Index}";

    private static string Hash(params string[] parts) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\u001f', parts)))).ToLowerInvariant();
}
