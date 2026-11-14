using System.Text.Json;
using System.Text.Json.Nodes;

namespace BotMessageAnalyzer.Classification;

/// <summary>A JSON Lines file of earlier model answers, one {"key", "value"} object per line.</summary>
public sealed class ResultCache
{
    private readonly Dictionary<string, JsonElement> entries = new(StringComparer.Ordinal);
    private readonly string? path;

    private ResultCache(string? path) => this.path = path;

    public static ResultCache InMemory() => new(null);

    public static ResultCache Open(string path)
    {
        var cache = new ResultCache(path);
        if (!File.Exists(path))
        {
            return cache;
        }

        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                if (root.TryGetProperty("key", out var key) && key.ValueKind == JsonValueKind.String
                    && root.TryGetProperty("value", out var value))
                {
                    cache.entries[key.GetString()!] = value.Clone();
                }
            }
            catch (JsonException)
            {
                // A damaged line only costs one cached answer.
            }
        }
        return cache;
    }

    public bool TryGet(string key, out JsonElement value) => entries.TryGetValue(key, out value);

    public void Put(string key, JsonObject value)
    {
        var line = new JsonObject { ["key"] = key, ["value"] = value.DeepClone() }.ToJsonString();
        entries[key] = JsonDocument.Parse(value.ToJsonString()).RootElement.Clone();

        if (path is null)
        {
            return;
        }
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
        File.AppendAllText(path, line + "\n");
    }
}
