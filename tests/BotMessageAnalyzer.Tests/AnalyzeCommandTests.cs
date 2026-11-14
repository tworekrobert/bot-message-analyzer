using BotMessageAnalyzer.Classification;

namespace BotMessageAnalyzer.Tests;

public class AnalyzeCommandTests : IDisposable
{
    private const string Input = """
        {
          "synthetic": true,
          "conversations": [
            { "id": "c-1", "messages": [
              { "role": "customer", "text": "Is there parking?" },
              { "role": "bot", "text": "Sorry, I did not understand that." } ] },
            { "id": "c-2", "messages": [
              { "role": "customer", "text": "Book me for Tuesday" },
              { "role": "bot", "text": "Booked." } ] },
            { "id": "c-3", "messages": [
              { "role": "customer", "text": "my email is a.b@example.com and my number is 07700 900123" },
              { "role": "bot", "text": "Thanks." } ] }
          ]
        }
        """;

    private readonly string folder = Path.Combine(Path.GetTempPath(), $"bma-run-{Guid.NewGuid():N}");

    public AnalyzeCommandTests()
    {
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "input.json"), Input);
        File.WriteAllText(Path.Combine(folder, "config.json"),
            """{ "model": "test-model", "requestDelayMs": 0, "maxRetries": 0 }""");
    }

    public void Dispose() => Directory.Delete(folder, recursive: true);

    private string At(string name) => Path.Combine(folder, name);

    private string[] Args(params string[] extra) =>
        [At("input.json"), "--config", At("config.json"), "--cache", At("cache.jsonl"), "--out", At("out"), .. extra];

    private static string? Key(string name) => name == "GEMINI_API_KEY" ? "test-key" : null;

    private static (string, string) Judge(string customer) =>
        customer.Contains("parking") ? ("NotUnderstood", "parking information") : ("Answered", "booking");

    private static StubHandler Model() => new(body => FakeGemini.Answer(body, Judge));

    [Fact]
    public async Task Run_writes_the_three_output_files_and_exits_zero()
    {
        var code = await AnalyzeCommand.RunAsync(Args(), Model(), Key);

        Assert.Equal(0, code);
        var report = File.ReadAllText(At("out/report.md"));
        Assert.Contains("parking information", report);
        Assert.Contains("| Conversations | 3 |", report);
        Assert.True(File.Exists(At("out/gaps.csv")));
        Assert.Contains("parking information,Is there parking?,c-1", File.ReadAllText(At("out/training-candidates.csv")));
    }

    [Fact]
    public async Task Rerun_on_unchanged_input_makes_no_calls_and_needs_no_key()
    {
        await AnalyzeCommand.RunAsync(Args(), Model(), Key);
        File.Delete(At("out/report.md"));

        var second = new StubHandler(_ => throw new InvalidOperationException("no call expected"));
        var code = await AnalyzeCommand.RunAsync(Args(), second, _ => null);

        Assert.Equal(0, code);
        Assert.Equal(0, second.Calls);
        Assert.True(File.Exists(At("out/report.md")));
    }

    [Fact]
    public async Task No_request_contains_an_email_or_a_phone_number()
    {
        var handler = Model();

        await AnalyzeCommand.RunAsync(Args(), handler, Key);

        var sent = string.Join("\n", handler.Bodies);
        Assert.True(handler.Calls > 0);
        Assert.True(!sent.Contains("a.b@example.com"));
        Assert.True(!sent.Contains("900123"));
        Assert.Contains("[email]", sent);
        Assert.Contains("[phone]", sent);
    }

    [Fact]
    public async Task Missing_key_with_uncached_turns_exits_two_without_calls()
    {
        var handler = Model();

        var code = await AnalyzeCommand.RunAsync(Args(), handler, _ => null);

        Assert.Equal(2, code);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Failed_requests_leave_turns_unclassified_and_exit_three()
    {
        var code = await AnalyzeCommand.RunAsync(Args(), new StubHandler(_ => Reply.Status(400)), Key);

        Assert.Equal(3, code);
        Assert.Contains("Unclassified | 2 |", File.ReadAllText(At("out/report.md")));
    }

    [Fact]
    public async Task Failed_topic_merge_still_succeeds_and_says_so_in_the_report()
    {
        var handler = new StubHandler(body => FakeGemini.IsMerge(body) ? Reply.Status(400) : FakeGemini.Answer(body, Judge));

        var code = await AnalyzeCommand.RunAsync(Args(), handler, Key);

        Assert.Equal(0, code);
        Assert.Contains("Topic consolidation did not complete", File.ReadAllText(At("out/report.md")));
    }

    [Fact]
    public async Task Invalid_input_exits_two()
    {
        File.WriteAllText(At("input.json"), "{ broken");

        Assert.Equal(2, await AnalyzeCommand.RunAsync(Args(), Model(), Key));
    }

    [Fact]
    public async Task Missing_config_or_missing_model_exits_two()
    {
        Assert.Equal(2, await AnalyzeCommand.RunAsync([At("input.json"), "--cache", At("cache.jsonl")], Model(), Key));

        File.WriteAllText(At("config.json"), "{}");
        Assert.Equal(2, await AnalyzeCommand.RunAsync(Args(), Model(), Key));
    }

    [Fact]
    public async Task Unknown_option_exits_two()
    {
        Assert.Equal(2, await AnalyzeCommand.RunAsync(Args("--verbose"), Model(), Key));
    }
}
