using BotMessageAnalyzer.Classification;
using BotMessageAnalyzer.Reporting;

namespace BotMessageAnalyzer.Tests;

public class GapAggregatorTests
{
    private static TurnResult Failed(string id, string text, Verdict verdict, string? topic) =>
        Make.Result(id, 1, text, "reply", verdict, topic);

    [Fact]
    public void Gaps_are_ranked_by_failed_turns_with_ties_broken_by_topic_name()
    {
        var results = new[]
        {
            Failed("c-1", "q1", Verdict.NotUnderstood, "b topic"),
            Failed("c-2", "q2", Verdict.NotUnderstood, "b topic"),
            Failed("c-3", "q3", Verdict.NotUnderstood, "a topic"),
            Failed("c-4", "q4", Verdict.NotUnderstood, "a topic"),
            Failed("c-5", "q5", Verdict.WrongAnswer, "d topic"),
            Failed("c-6", "q6", Verdict.WrongAnswer, "d topic"),
            Failed("c-7", "q7", Verdict.WrongAnswer, "d topic"),
            Failed("c-8", "q8", Verdict.WrongAnswer, "c topic")
        };

        var gaps = GapAggregator.Build(results);

        Assert.Equal(new[] { "d topic", "a topic", "b topic", "c topic" }, gaps.Select(g => g.Topic).ToArray());
        Assert.Equal(new[] { 1, 2, 3, 4 }, gaps.Select(g => g.Rank).ToArray());
        Assert.Equal(new[] { 3, 2, 2, 1 }, gaps.Select(g => g.Turns).ToArray());
    }

    [Fact]
    public void Counts_and_share_of_failed_turns_are_computed_per_gap()
    {
        var results = new[]
        {
            Failed("c-1", "q1", Verdict.NotUnderstood, "x"),
            Failed("c-2", "q2", Verdict.NotUnderstood, "x"),
            Failed("c-3", "q3", Verdict.WrongAnswer, "x"),
            Failed("c-4", "q4", Verdict.NotUnderstood, "y")
        };

        var gap = GapAggregator.Build(results)[0];

        Assert.Equal("x", gap.Topic);
        Assert.Equal(2, gap.NotUnderstood);
        Assert.Equal(1, gap.WrongAnswer);
        Assert.Equal(0.75, gap.ShareOfFailed);
    }

    [Fact]
    public void Answered_and_unclassified_turns_are_not_gaps()
    {
        var results = new[]
        {
            Failed("c-1", "q1", Verdict.Answered, "x"),
            Failed("c-2", "q2", Verdict.Unclassified, null),
            Failed("c-3", "q3", Verdict.NotUnderstood, "y")
        };

        var gaps = GapAggregator.Build(results);

        Assert.Equal("y", Assert.Single(gaps).Topic);
        Assert.Equal(1.0, gaps[0].ShareOfFailed);
    }

    [Fact]
    public void Failed_turn_without_a_topic_is_listed_as_unlabelled()
    {
        var gaps = GapAggregator.Build([Failed("c-1", "q1", Verdict.NotUnderstood, null)]);

        Assert.Equal("unlabelled", Assert.Single(gaps).Topic);
    }

    [Fact]
    public void Examples_are_distinct_customer_messages_capped_at_three()
    {
        var results = new[]
        {
            Failed("c-1", "same", Verdict.NotUnderstood, "x"),
            Failed("c-2", "SAME", Verdict.NotUnderstood, "x"),
            Failed("c-3", "two", Verdict.NotUnderstood, "x"),
            Failed("c-4", "three", Verdict.NotUnderstood, "x"),
            Failed("c-5", "four", Verdict.NotUnderstood, "x")
        };

        var gap = GapAggregator.Build(results)[0];

        Assert.Equal(new[] { "same", "two", "three" }, gap.Examples.Select(e => e.Turn.CustomerText).ToArray());
        Assert.Equal(5, gap.ConversationIds.Count);
    }

    [Theory]
    [InlineData(3, 1, "Add an intent and training phrases")]
    [InlineData(1, 3, "Review the bot's answer")]
    [InlineData(2, 2, "Add an intent and training phrases; review the bot's answer")]
    public void Suggested_action_follows_the_larger_count_and_both_on_a_tie(int notUnderstood, int wrongAnswer, string expected)
    {
        Assert.Equal(expected, GapAggregator.SuggestedAction(notUnderstood, wrongAnswer));
    }
}
