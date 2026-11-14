using BotMessageAnalyzer.Input;

namespace BotMessageAnalyzer.Classification;

public sealed record AnalysisOutcome(IReadOnlyList<TurnResult> Results, bool TopicsConsolidated);

public sealed class TurnAnalyzer(IClassifier classifier, IReadOnlyList<string> fallbackPhrases)
{
    public async Task<AnalysisOutcome> RunAsync(IReadOnlyList<Turn> turns, CancellationToken cancellationToken)
    {
        var judged = await classifier.ClassifyAsync(turns, cancellationToken);
        var results = judged
            .Select(r => FallbackRules.Apply(r, fallbackPhrases) with { Topic = Normalise(r.Topic) })
            .ToList();

        var labels = results
            .Where(r => r.Verdict is Verdict.NotUnderstood or Verdict.WrongAnswer)
            .Select(r => r.Topic)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        if (labels.Count == 0)
        {
            return new AnalysisOutcome(results, true);
        }

        var map = await classifier.MergeTopicsAsync(labels, cancellationToken);
        if (map is null)
        {
            return new AnalysisOutcome(results, false);
        }

        var merged = results
            .Select(r => r.Topic is not null && map.TryGetValue(r.Topic, out var canonical)
                ? r with { Topic = Normalise(canonical) }
                : r)
            .ToList();
        return new AnalysisOutcome(merged, true);
    }

    private static string? Normalise(string? label)
    {
        var trimmed = label?.Trim().ToLowerInvariant();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
