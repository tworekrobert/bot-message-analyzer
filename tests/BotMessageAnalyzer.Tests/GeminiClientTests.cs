using System.Text.Json;
using System.Text.Json.Nodes;
using BotMessageAnalyzer.Classification;

namespace BotMessageAnalyzer.Tests;

public class GeminiClientTests
{
    private static readonly JsonNode Schema = JsonNode.Parse("""{ "type": "array" }""")!;

    private static (GeminiClient Client, List<TimeSpan> Waits) Create(
        StubHandler handler, int maxRetries = 4, int delayMs = 500)
    {
        var waits = new List<TimeSpan>();
        var client = new GeminiClient(
            new HttpClient(handler), "test-key", "test-model", maxRetries, TimeSpan.FromMilliseconds(delayMs),
            (delay, _) => { waits.Add(delay); return Task.CompletedTask; });
        return (client, waits);
    }

    [Fact]
    public async Task Valid_response_returns_the_model_text_and_sends_the_expected_request()
    {
        var handler = new StubHandler(_ => Reply.Text("[1,2]"));
        var (client, _) = Create(handler);

        var text = await client.GenerateJsonAsync("hello prompt", Schema, CancellationToken.None);

        Assert.Equal("[1,2]", text);
        Assert.Equal("test-key", handler.ApiKeys[0]);
        Assert.Contains("models/test-model:generateContent", handler.Urls[0]!.ToString());

        using var request = JsonDocument.Parse(handler.Bodies[0]);
        var config = request.RootElement.GetProperty("generationConfig");
        Assert.Equal(0, config.GetProperty("temperature").GetInt32());
        Assert.Equal("application/json", config.GetProperty("responseMimeType").GetString());
        Assert.Equal("hello prompt",
            request.RootElement.GetProperty("contents")[0].GetProperty("parts")[0].GetProperty("text").GetString());
    }

    [Fact]
    public async Task Thought_parts_are_not_part_of_the_answer()
    {
        var handler = new StubHandler(_ => Reply.Ok(
            """{ "candidates": [ { "content": { "parts": [ { "text": "thinking", "thought": true }, { "text": "[]" } ] } } ] }"""));
        var (client, _) = Create(handler);

        Assert.Equal("[]", await client.GenerateJsonAsync("p", Schema, CancellationToken.None));
    }

    [Fact]
    public async Task Status_429_is_retried_with_backoff_and_then_succeeds()
    {
        var calls = 0;
        var handler = new StubHandler(_ => ++calls < 3 ? Reply.Status(429) : Reply.Text("[]"));
        var (client, waits) = Create(handler);

        var text = await client.GenerateJsonAsync("p", Schema, CancellationToken.None);

        Assert.Equal("[]", text);
        Assert.Equal(3, handler.Calls);
        Assert.Equal(new[] { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2) }, waits.ToArray());
    }

    [Fact]
    public async Task Retry_after_header_is_honoured()
    {
        var calls = 0;
        var handler = new StubHandler(_ => ++calls < 2 ? Reply.Status(503, retryAfterSeconds: 7) : Reply.Text("[]"));
        var (client, waits) = Create(handler);

        await client.GenerateJsonAsync("p", Schema, CancellationToken.None);

        Assert.Equal(new[] { TimeSpan.FromSeconds(7) }, waits.ToArray());
    }

    [Fact]
    public async Task Gives_up_after_the_maximum_number_of_retries()
    {
        var handler = new StubHandler(_ => Reply.Status(429));
        var (client, waits) = Create(handler, maxRetries: 2);

        var text = await client.GenerateJsonAsync("p", Schema, CancellationToken.None);

        Assert.Null(text);
        Assert.Equal(3, handler.Calls);
        Assert.Equal(new[] { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2) }, waits.ToArray());
        Assert.Contains("429", client.LastError!);
    }

    [Fact]
    public async Task Client_errors_other_than_429_are_not_retried()
    {
        var handler = new StubHandler(_ => Reply.Status(400));
        var (client, waits) = Create(handler);

        Assert.Null(await client.GenerateJsonAsync("p", Schema, CancellationToken.None));
        Assert.Equal(1, handler.Calls);
        Assert.Empty(waits);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("""{ "candidates": [] }""")]
    public async Task Response_without_usable_text_returns_null(string body)
    {
        var handler = new StubHandler(_ => Reply.Ok(body));
        var (client, _) = Create(handler);

        Assert.Null(await client.GenerateJsonAsync("p", Schema, CancellationToken.None));
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Requests_are_spaced_by_the_configured_delay()
    {
        var handler = new StubHandler(_ => Reply.Text("[]"));
        var (client, waits) = Create(handler, delayMs: 500);

        await client.GenerateJsonAsync("one", Schema, CancellationToken.None);
        Assert.Empty(waits);

        await client.GenerateJsonAsync("two", Schema, CancellationToken.None);
        Assert.Equal(new[] { TimeSpan.FromMilliseconds(500) }, waits.ToArray());
    }
}
