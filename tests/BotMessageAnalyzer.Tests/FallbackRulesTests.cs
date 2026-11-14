using BotMessageAnalyzer.Classification;

namespace BotMessageAnalyzer.Tests;

public class FallbackRulesTests
{
    private static readonly string[] Phrases = ["did not understand", "can you rephrase"];

    [Fact]
    public void Match_is_case_insensitive_and_overrides_the_model_verdict()
    {
        var modelResult = Make.Result("c-1", 1, "Is there parking?", "SORRY, I DID NOT UNDERSTAND THAT.",
            Verdict.Answered, "parking information");

        var result = FallbackRules.Apply(modelResult, Phrases);

        Assert.Equal(Verdict.NotUnderstood, result.Verdict);
        Assert.Equal(VerdictSource.Rule, result.Source);
        Assert.Equal("parking information", result.Topic);
        Assert.Contains("did not understand", result.Reason!);
    }

    [Fact]
    public void Reply_without_a_fallback_phrase_is_left_alone()
    {
        var modelResult = Make.Result("c-1", 1, "Hours?", "We open at 8.",
            Verdict.Answered, "opening hours");

        Assert.Equal(modelResult, FallbackRules.Apply(modelResult, Phrases));
    }

    [Fact]
    public void Missing_reply_is_not_understood_with_reason_no_reply()
    {
        var modelResult = Make.Result("c-1", 2, "thanks", null, Verdict.Answered, null);

        var result = FallbackRules.Apply(modelResult, Phrases);

        Assert.Equal(Verdict.NotUnderstood, result.Verdict);
        Assert.Equal("no reply", result.Reason);
        Assert.Equal(VerdictSource.Rule, result.Source);
    }

    [Fact]
    public void Blank_phrases_never_match()
    {
        var modelResult = Make.Result("c-1", 1, "Hours?", "We open at 8.", Verdict.Answered, null);

        Assert.Equal(modelResult, FallbackRules.Apply(modelResult, ["", "  "]));
    }
}
