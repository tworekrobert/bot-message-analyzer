using BotMessageAnalyzer.Classification;

namespace BotMessageAnalyzer.Reporting;

public record Gap(
    int Rank,
    string Topic,
    int Turns,
    double ShareOfFailed,
    int NotUnderstood,
    int WrongAnswer,
    string SuggestedAction,
    IReadOnlyList<TurnResult> Examples,
    IReadOnlyList<string> ConversationIds);

public static class GapAggregator
{
    public const string Unlabelled = "unlabelled";
    public const string AddIntent = "Add an intent and training phrases";
    public const string ReviewAnswer = "Review the bot's answer";
    public const string Both = AddIntent + "; review the bot's answer";

    private const int ExampleCount = 3;

    public static IReadOnlyList<Gap> Build(IReadOnlyList<TurnResult> results)
    {
        var failed = results
            .Where(r => r.Verdict is Verdict.NotUnderstood or Verdict.WrongAnswer)
            .ToList();

        var groups = failed
            .GroupBy(r => string.IsNullOrWhiteSpace(r.Topic) ? Unlabelled : r.Topic!, StringComparer.Ordinal)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .ToList();

        var gaps = new List<Gap>();
        foreach (var group in groups)
        {
            var items = group.ToList();
            var notUnderstood = items.Count(r => r.Verdict == Verdict.NotUnderstood);
            var wrongAnswer = items.Count - notUnderstood;

            var examples = items
                .DistinctBy(r => r.Turn.CustomerText, StringComparer.OrdinalIgnoreCase)
                .Take(ExampleCount)
                .ToList();
            var conversationIds = items.Select(r => r.Turn.ConversationId).Distinct(StringComparer.Ordinal).ToList();

            gaps.Add(new Gap(
                gaps.Count + 1,
                group.Key,
                items.Count,
                (double)items.Count / failed.Count,
                notUnderstood,
                wrongAnswer,
                SuggestedAction(notUnderstood, wrongAnswer),
                examples,
                conversationIds));
        }
        return gaps;
    }

    public static string SuggestedAction(int notUnderstood, int wrongAnswer) =>
        notUnderstood > wrongAnswer ? AddIntent
        : wrongAnswer > notUnderstood ? ReviewAnswer
        : Both;
}
