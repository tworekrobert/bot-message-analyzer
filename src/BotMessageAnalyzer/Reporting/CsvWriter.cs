using System.Globalization;
using System.Text;
using BotMessageAnalyzer.Classification;

namespace BotMessageAnalyzer.Reporting;

public static class CsvWriter
{
    private const string NewLine = "\r\n";

    public static string Gaps(IReadOnlyList<Gap> gaps)
    {
        var sb = new StringBuilder();
        Row(sb, "rank", "topic", "turns", "share_of_failed", "not_understood", "wrong_answer", "suggested_action", "examples");
        foreach (var gap in gaps)
        {
            Row(sb,
                gap.Rank.ToString(CultureInfo.InvariantCulture),
                gap.Topic,
                gap.Turns.ToString(CultureInfo.InvariantCulture),
                (gap.ShareOfFailed * 100).ToString("0.0", CultureInfo.InvariantCulture) + "%",
                gap.NotUnderstood.ToString(CultureInfo.InvariantCulture),
                gap.WrongAnswer.ToString(CultureInfo.InvariantCulture),
                gap.SuggestedAction,
                string.Join("|", gap.Examples.Select(e => e.Turn.CustomerText)));
        }
        return sb.ToString();
    }

    public static string TrainingCandidates(IReadOnlyList<TurnResult> results)
    {
        var rows = results
            .Where(r => r.Verdict == Verdict.NotUnderstood)
            .Select((r, order) => (Result: r, Order: order, Topic: string.IsNullOrWhiteSpace(r.Topic) ? GapAggregator.Unlabelled : r.Topic!))
            .DistinctBy(x => (x.Topic, x.Result.Turn.CustomerText.ToLowerInvariant()))
            .OrderBy(x => x.Topic, StringComparer.Ordinal)
            .ThenBy(x => x.Order);

        var sb = new StringBuilder();
        Row(sb, "topic", "customer_message", "conversation_id");
        foreach (var row in rows)
        {
            Row(sb, row.Topic, row.Result.Turn.CustomerText, row.Result.Turn.ConversationId);
        }
        return sb.ToString();
    }

    private static void Row(StringBuilder sb, params string[] fields)
    {
        sb.Append(string.Join(',', fields.Select(Field))).Append(NewLine);
    }

    private static string Field(string value) =>
        value.IndexOfAny([',', '"', '\r', '\n']) >= 0
            ? "\"" + value.Replace("\"", "\"\"") + "\""
            : value;
}
