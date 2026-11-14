using BotMessageAnalyzer.Classification;
using BotMessageAnalyzer.Input;

namespace BotMessageAnalyzer.Tests;

internal static class Make
{
    public static Conversation Conversation(params (string Role, string Text)[] messages) =>
        new("c-001", null, messages.Select(m => new Message(m.Role, m.Text)).ToList());

    public static Turn Turn(string conversationId, int index, string customer, string? reply) =>
        new(conversationId, index, customer, reply);

    public static TurnResult Result(
        string conversationId, int index, string customer, string? reply,
        Verdict verdict, string? topic, VerdictSource source = VerdictSource.Model, string? reason = null) =>
        new(Turn(conversationId, index, customer, reply), verdict, topic, reason, source);
}

internal sealed class FakeClassifier(
    Func<Turn, TurnResult> judge,
    IReadOnlyDictionary<string, string>? mergeMap) : IClassifier
{
    public List<IReadOnlyList<string>> MergeRequests { get; } = [];

    public Task<IReadOnlyList<TurnResult>> ClassifyAsync(IReadOnlyList<Turn> turns, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<TurnResult>>(turns.Select(judge).ToList());

    public Task<IReadOnlyDictionary<string, string>?> MergeTopicsAsync(
        IReadOnlyList<string> labels, CancellationToken cancellationToken)
    {
        MergeRequests.Add(labels);
        return Task.FromResult(mergeMap);
    }
}

internal sealed class StubHandler(Func<string, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<string> Bodies { get; } = [];
    public List<string?> ApiKeys { get; } = [];
    public List<Uri?> Urls { get; } = [];

    public int Calls => Bodies.Count;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        Bodies.Add(body);
        Urls.Add(request.RequestUri);
        ApiKeys.Add(request.Headers.TryGetValues("x-goog-api-key", out var values) ? values.Single() : null);
        return respond(body);
    }
}

internal static class Reply
{
    public static HttpResponseMessage Text(string modelText) =>
        Ok(new System.Text.Json.Nodes.JsonObject
        {
            ["candidates"] = new System.Text.Json.Nodes.JsonArray(new System.Text.Json.Nodes.JsonObject
            {
                ["content"] = new System.Text.Json.Nodes.JsonObject
                {
                    ["parts"] = new System.Text.Json.Nodes.JsonArray(new System.Text.Json.Nodes.JsonObject { ["text"] = modelText })
                }
            })
        }.ToJsonString());

    public static HttpResponseMessage Ok(string json) =>
        new(System.Net.HttpStatusCode.OK) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };

    public static HttpResponseMessage Status(int code, int? retryAfterSeconds = null)
    {
        var response = new HttpResponseMessage((System.Net.HttpStatusCode)code)
        {
            Content = new StringContent("{\"error\":{\"message\":\"status " + code + "\"}}")
        };
        if (retryAfterSeconds is { } seconds)
        {
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(seconds));
        }
        return response;
    }
}

/// <summary>Plays the model: reads the turns or labels out of the prompt and answers them.</summary>
internal static class FakeGemini
{
    public static string PromptOf(string requestBody)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(requestBody);
        return doc.RootElement.GetProperty("contents")[0].GetProperty("parts")[0].GetProperty("text").GetString()!;
    }

    public static bool IsMerge(string requestBody) => PromptOf(requestBody).Contains("\n\nLabels:\n");

    public static List<(string Id, string Customer)> TurnsIn(string requestBody)
    {
        var json = PromptOf(requestBody).Split("\n\nTurns:\n")[1];
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        return doc.RootElement.EnumerateArray()
            .Select(e => (e.GetProperty("id").GetString()!, e.GetProperty("customer").GetString()!))
            .ToList();
    }

    public static HttpResponseMessage Answer(string requestBody, Func<string, (string Verdict, string Topic)> judge)
    {
        if (IsMerge(requestBody))
        {
            var json = PromptOf(requestBody).Split("\n\nLabels:\n")[1];
            using var labels = System.Text.Json.JsonDocument.Parse(json);
            var pairs = labels.RootElement.EnumerateArray()
                .Select(l => new { label = l.GetString(), canonical = l.GetString() });
            return Reply.Text(System.Text.Json.JsonSerializer.Serialize(pairs));
        }

        var items = TurnsIn(requestBody).Select(t =>
        {
            var (verdict, topic) = judge(t.Customer);
            return new { id = t.Id, verdict, topic, reason = "because" };
        });
        return Reply.Text(System.Text.Json.JsonSerializer.Serialize(items));
    }
}
