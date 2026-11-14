namespace BotMessageAnalyzer.Classification;

public static class FallbackRules
{
    public static TurnResult Apply(TurnResult result, IReadOnlyList<string> fallbackPhrases)
    {
        var reply = result.Turn.BotReply;
        if (reply is null)
        {
            return result with { Verdict = Verdict.NotUnderstood, Reason = "no reply", Source = VerdictSource.Rule };
        }

        foreach (var phrase in fallbackPhrases)
        {
            var trimmed = phrase.Trim();
            if (trimmed.Length > 0 && reply.Contains(trimmed, StringComparison.OrdinalIgnoreCase))
            {
                return result with
                {
                    Verdict = Verdict.NotUnderstood,
                    Reason = $"bot replied with the fallback phrase \"{trimmed}\"",
                    Source = VerdictSource.Rule
                };
            }
        }
        return result;
    }
}
