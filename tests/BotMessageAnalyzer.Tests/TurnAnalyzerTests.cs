using BotMessageAnalyzer.Classification;
using BotMessageAnalyzer.Input;

namespace BotMessageAnalyzer.Tests;

public class TurnAnalyzerTests
{
    private static readonly string[] Phrases = ["did not understand"];

    private static Turn[] SampleTurns() =>
    [
        Make.Turn("c-1", 1, "where can I park", "Sorry, I did not understand that."),
        Make.Turn("c-2", 1, "is there a car park", "Sorry, I did not understand that."),
        Make.Turn("c-3", 1, "book tuesday", "Booked.")
    ];

    private static TurnResult Judge(Turn turn) => turn.CustomerText switch
    {
        "where can I park" => new(turn, Verdict.NotUnderstood, "  Where To Park ", "r", VerdictSource.Model),
        "is there a car park" => new(turn, Verdict.NotUnderstood, "parking info", "r", VerdictSource.Model),
        _ => new(turn, Verdict.Answered, "booking", "r", VerdictSource.Model)
    };

    [Fact]
    public async Task Merge_map_is_applied_and_only_failed_topics_are_sent()
    {
        var map = new Dictionary<string, string>
        {
            ["where to park"] = "parking information",
            ["parking info"] = "parking information"
        };
        var fake = new FakeClassifier(Judge, map);

        var outcome = await new TurnAnalyzer(fake, Phrases).RunAsync(SampleTurns(), CancellationToken.None);

        Assert.True(outcome.TopicsConsolidated);
        Assert.Equal(
            new[] { "parking info", "where to park" },
            Assert.Single(fake.MergeRequests).ToArray());
        Assert.Equal("parking information", outcome.Results[0].Topic);
        Assert.Equal("parking information", outcome.Results[1].Topic);
        Assert.Equal("booking", outcome.Results[2].Topic);
    }

    [Fact]
    public async Task Failed_merge_falls_back_to_lowercased_trimmed_labels()
    {
        var fake = new FakeClassifier(Judge, null);

        var outcome = await new TurnAnalyzer(fake, Phrases).RunAsync(SampleTurns(), CancellationToken.None);

        Assert.False(outcome.TopicsConsolidated);
        Assert.Equal("where to park", outcome.Results[0].Topic);
        Assert.Equal("parking info", outcome.Results[1].Topic);
    }

    [Fact]
    public async Task Fallback_phrase_overrides_a_model_verdict_of_answered()
    {
        var fake = new FakeClassifier(
            t => new TurnResult(t, Verdict.Answered, "parking", null, VerdictSource.Model),
            new Dictionary<string, string>());

        var outcome = await new TurnAnalyzer(fake, Phrases).RunAsync(SampleTurns().Take(1).ToArray(), CancellationToken.None);

        Assert.Equal(Verdict.NotUnderstood, outcome.Results[0].Verdict);
        Assert.Equal(VerdictSource.Rule, outcome.Results[0].Source);
    }

    [Fact]
    public async Task Unclassified_turns_stay_unclassified_and_are_not_merged()
    {
        var fake = new FakeClassifier(
            t => new TurnResult(t, Verdict.Unclassified, null, null, VerdictSource.Failed),
            new Dictionary<string, string>());

        var turns = new[] { Make.Turn("c-3", 1, "book tuesday", "Booked.") };
        var outcome = await new TurnAnalyzer(fake, Phrases).RunAsync(turns, CancellationToken.None);

        Assert.Equal(Verdict.Unclassified, outcome.Results[0].Verdict);
        Assert.Empty(fake.MergeRequests);
    }
}
