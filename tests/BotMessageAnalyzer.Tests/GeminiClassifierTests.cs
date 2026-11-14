using System.Text.Json;
using BotMessageAnalyzer.Classification;
using BotMessageAnalyzer.Input;

namespace BotMessageAnalyzer.Tests;

public class GeminiClassifierTests
{
    private static GeminiClassifier Create(
        StubHandler? handler, ResultCache cache, string model = "m", int batchSize = 15, string prompt = "P")
    {
        var client = handler is null
            ? null
            : new GeminiClient(new HttpClient(handler), "k", model, 0, TimeSpan.Zero, (_, _) => Task.CompletedTask);
        return new GeminiClassifier(client, cache, model, batchSize, prompt, "MERGE");
    }

    private static StubHandler Judging(Func<string, (string, string)>? judge = null) =>
        new(body => FakeGemini.Answer(body, judge ?? (_ => ("Answered", "topic"))));

    private static Turn[] Turns(int count) =>
        Enumerable.Range(1, count).Select(i => Make.Turn("c-1", i, $"question {i}", "reply")).ToArray();

    [Fact]
    public async Task Valid_response_becomes_model_results()
    {
        var handler = Judging(_ => ("WrongAnswer", "holiday hours"));
        var classifier = Create(handler, ResultCache.InMemory());

        var results = await classifier.ClassifyAsync(Turns(2), CancellationToken.None);

        Assert.Equal(2, results.Count);
        Assert.Equal(Verdict.WrongAnswer, results[0].Verdict);
        Assert.Equal("holiday hours", results[0].Topic);
        Assert.Equal(VerdictSource.Model, results[0].Source);
        Assert.Equal("question 2", results[1].Turn.CustomerText);
    }

    [Fact]
    public async Task Malformed_json_marks_the_whole_batch_unclassified()
    {
        var handler = new StubHandler(_ => Reply.Text("this is not json"));
        var classifier = Create(handler, ResultCache.InMemory());

        var results = await classifier.ClassifyAsync(Turns(3), CancellationToken.None);

        Assert.True(results.All(r => r.Verdict == Verdict.Unclassified && r.Source == VerdictSource.Failed));
    }

    [Fact]
    public async Task Unknown_ids_are_ignored_and_missing_ids_are_unclassified()
    {
        var handler = new StubHandler(_ => Reply.Text("""
            [ { "id": "c-1:1", "verdict": "Answered", "topic": "a", "reason": "r" },
              { "id": "zzz:9", "verdict": "Answered", "topic": "b", "reason": "r" } ]
            """));
        var classifier = Create(handler, ResultCache.InMemory());

        var results = await classifier.ClassifyAsync(Turns(2), CancellationToken.None);

        Assert.Equal(Verdict.Answered, results[0].Verdict);
        Assert.Equal(Verdict.Unclassified, results[1].Verdict);
    }

    [Fact]
    public async Task Unknown_verdict_word_is_treated_as_missing()
    {
        var handler = new StubHandler(_ => Reply.Text(
            """[ { "id": "c-1:1", "verdict": "Maybe", "topic": "a", "reason": "r" } ]"""));
        var classifier = Create(handler, ResultCache.InMemory());

        var results = await classifier.ClassifyAsync(Turns(1), CancellationToken.None);

        Assert.Equal(Verdict.Unclassified, results[0].Verdict);
    }

    [Fact]
    public async Task Turns_are_sent_in_batches_of_the_configured_size()
    {
        var handler = Judging();
        var classifier = Create(handler, ResultCache.InMemory(), batchSize: 15);

        var results = await classifier.ClassifyAsync(Turns(20), CancellationToken.None);

        Assert.Equal(2, handler.Calls);
        Assert.Equal(15, FakeGemini.TurnsIn(handler.Bodies[0]).Count);
        Assert.Equal(5, FakeGemini.TurnsIn(handler.Bodies[1]).Count);
        Assert.True(results.All(r => r.Verdict == Verdict.Answered));
    }

    [Fact]
    public async Task Cached_turns_avoid_a_second_call()
    {
        var cache = ResultCache.InMemory();
        var handler = Judging();
        var classifier = Create(handler, cache);
        await classifier.ClassifyAsync(Turns(3), CancellationToken.None);
        Assert.Equal(1, handler.Calls);

        var again = await classifier.ClassifyAsync(Turns(3), CancellationToken.None);

        Assert.Equal(1, handler.Calls);
        Assert.Equal(Verdict.Answered, again[0].Verdict);
        Assert.Equal(0, classifier.CountUncached(Turns(3)));
    }

    [Fact]
    public async Task Changed_prompt_or_model_misses_the_cache()
    {
        var cache = ResultCache.InMemory();
        await Create(Judging(), cache).ClassifyAsync(Turns(2), CancellationToken.None);

        Assert.Equal(0, Create(null, cache).CountUncached(Turns(2)));
        Assert.Equal(2, Create(null, cache, prompt: "another prompt").CountUncached(Turns(2)));
        Assert.Equal(2, Create(null, cache, model: "another-model").CountUncached(Turns(2)));
    }

    [Fact]
    public async Task Cache_key_covers_the_bot_reply()
    {
        var cache = ResultCache.InMemory();
        await Create(Judging(), cache).ClassifyAsync([Make.Turn("c-1", 1, "q", "one reply")], CancellationToken.None);

        Assert.Equal(1, Create(null, cache).CountUncached([Make.Turn("c-1", 1, "q", "different reply")]));
    }

    [Fact]
    public async Task Failed_batches_are_not_cached()
    {
        var cache = ResultCache.InMemory();
        await Create(new StubHandler(_ => Reply.Status(400)), cache).ClassifyAsync(Turns(2), CancellationToken.None);

        var handler = Judging();
        var results = await Create(handler, cache).ClassifyAsync(Turns(2), CancellationToken.None);

        Assert.Equal(1, handler.Calls);
        Assert.Equal(Verdict.Answered, results[0].Verdict);
    }

    [Fact]
    public async Task A_rejected_request_stops_further_batches()
    {
        var handler = new StubHandler(_ => Reply.Status(400));
        var classifier = Create(handler, ResultCache.InMemory(), batchSize: 2);

        var results = await classifier.ClassifyAsync(Turns(6), CancellationToken.None);

        Assert.Equal(1, handler.Calls);
        Assert.True(results.All(r => r.Verdict == Verdict.Unclassified));
    }

    [Fact]
    public async Task Without_a_client_uncached_turns_are_unclassified()
    {
        var results = await Create(null, ResultCache.InMemory()).ClassifyAsync(Turns(1), CancellationToken.None);

        Assert.Equal(Verdict.Unclassified, results[0].Verdict);
    }

    [Fact]
    public async Task Topic_merge_returns_the_map_and_is_cached()
    {
        var cache = ResultCache.InMemory();
        var handler = new StubHandler(_ => Reply.Text("""
            [ { "label": "where to park", "canonical": "parking information" },
              { "label": "parking info", "canonical": "parking information" },
              { "label": "not asked", "canonical": "x" } ]
            """));
        var classifier = Create(handler, cache);
        string[] labels = ["parking info", "where to park"];

        var map = await classifier.MergeTopicsAsync(labels, CancellationToken.None);
        var again = await classifier.MergeTopicsAsync(labels, CancellationToken.None);

        Assert.Equal("parking information", map!["where to park"]);
        Assert.Equal(2, map.Count);
        Assert.Equal(1, handler.Calls);
        Assert.Equal("parking information", again!["parking info"]);
    }

    [Fact]
    public async Task Topic_merge_returns_null_when_the_request_fails()
    {
        var classifier = Create(new StubHandler(_ => Reply.Status(400)), ResultCache.InMemory());

        Assert.Null(await classifier.MergeTopicsAsync(["a", "b"], CancellationToken.None));
    }

    [Fact]
    public async Task Cache_survives_a_reload_from_disk()
    {
        var path = Path.Combine(Path.GetTempPath(), $"bma-cache-{Guid.NewGuid():N}.jsonl");
        try
        {
            await Create(Judging(), ResultCache.Open(path)).ClassifyAsync(Turns(2), CancellationToken.None);

            var reloaded = Create(null, ResultCache.Open(path));

            Assert.Equal(0, reloaded.CountUncached(Turns(2)));
            Assert.Equal(2, File.ReadAllLines(path).Length);
            using var first = JsonDocument.Parse(File.ReadAllLines(path)[0]);
            Assert.True(first.RootElement.TryGetProperty("key", out _));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Damaged_cache_lines_are_skipped()
    {
        var path = Path.Combine(Path.GetTempPath(), $"bma-cache-{Guid.NewGuid():N}.jsonl");
        try
        {
            File.WriteAllText(path, "not json\n\n{\"key\":\"k\",\"value\":{\"a\":1}}\n{\"nokey\":1}\n");

            var cache = ResultCache.Open(path);

            Assert.True(cache.TryGet("k", out _));
            Assert.False(cache.TryGet("nokey", out _));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
