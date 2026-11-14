using System.Globalization;
using System.Text;
using BotMessageAnalyzer.Classification;

namespace BotMessageAnalyzer.Reporting;

public record ReportContext(
    string InputFile,
    int Conversations,
    string Model,
    string PromptVersions,
    DateOnly Date,
    bool TopicsConsolidated);

public static class MarkdownReport
{
    private const int MaxGapsShown = 15;
    private const int MaxIdsShown = 10;

    public static string Build(ReportContext context, IReadOnlyList<TurnResult> results, IReadOnlyList<Gap> gaps)
    {
        var sb = new StringBuilder();
        var total = results.Count;

        Line(sb, "# Bot message analysis");
        Line(sb);
        Line(sb, "## Run information");
        Line(sb);
        Line(sb, "| | |");
        Line(sb, "|---|---|");
        Line(sb, $"| Input file | {Cell(context.InputFile)} |");
        Line(sb, $"| Conversations | {context.Conversations} |");
        Line(sb, $"| Turns | {total} |");
        Line(sb, $"| Model | {Cell(context.Model)} |");
        Line(sb, $"| Prompt versions | {Cell(context.PromptVersions)} |");
        Line(sb, $"| Date | {context.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} |");
        Line(sb);

        Line(sb, "## Summary");
        Line(sb);
        Line(sb, "| Verdict | Turns | Share of all turns |");
        Line(sb, "|---|---|---|");
        foreach (var verdict in Enum.GetValues<Verdict>())
        {
            var count = results.Count(r => r.Verdict == verdict);
            Line(sb, $"| {verdict} | {count} | {Percent(count, total)} |");
        }
        Line(sb);

        Line(sb, "## Top gaps");
        Line(sb);
        if (gaps.Count == 0)
        {
            Line(sb, "No failed turns were found.");
            Line(sb);
        }
        else
        {
            var shown = gaps.Take(MaxGapsShown).ToList();
            Line(sb, "| Rank | Topic | Turns | Share of failed turns | Not understood | Wrong answer | Suggested action |");
            Line(sb, "|---|---|---|---|---|---|---|");
            foreach (var gap in shown)
            {
                Line(sb, $"| {gap.Rank} | {Cell(gap.Topic)} | {gap.Turns} | {Percent(gap.ShareOfFailed)} | {gap.NotUnderstood} | {gap.WrongAnswer} | {Cell(gap.SuggestedAction)} |");
            }
            Line(sb);
            if (gaps.Count > shown.Count)
            {
                Line(sb, $"Showing the top {shown.Count} of {gaps.Count} gaps. The full list is in gaps.csv.");
                Line(sb);
            }

            Line(sb, "## Gap details");
            Line(sb);
            foreach (var gap in shown)
            {
                WriteDetail(sb, gap);
            }
        }

        Line(sb, "## Limits");
        Line(sb);
        Line(sb, "- Every verdict and topic comes from a language model plus a fixed list of fallback phrases. Spot-check the results against the original logs before acting on them.");
        var unclassified = results.Count(r => r.Verdict == Verdict.Unclassified);
        Line(sb, unclassified == 0
            ? "- Unclassified turns: 0."
            : $"- Unclassified turns: {unclassified}. The model call failed for these turns, so they are not counted in any gap.");
        Line(sb, context.TopicsConsolidated
            ? "- Topic consolidation completed."
            : "- Topic consolidation did not complete. Topics were only lowercased and trimmed, so similar topics may appear as separate gaps.");
        return sb.ToString();
    }

    private static void WriteDetail(StringBuilder sb, Gap gap)
    {
        Line(sb, $"### {gap.Rank}. {OneLine(gap.Topic)} ({gap.Turns} {(gap.Turns == 1 ? "turn" : "turns")})");
        Line(sb);
        foreach (var example in gap.Examples)
        {
            Line(sb, $"- Customer: {OneLine(example.Turn.CustomerText)}");
            Line(sb, $"  - Bot: {(example.Turn.BotReply is null ? "(no reply)" : OneLine(example.Turn.BotReply))}");
        }
        Line(sb);
        var ids = string.Join(", ", gap.ConversationIds.Take(MaxIdsShown));
        var more = gap.ConversationIds.Count - MaxIdsShown;
        Line(sb, more > 0 ? $"Conversations: {ids} and {more} more." : $"Conversations: {ids}.");
        Line(sb);
    }

    private static string Percent(int part, int whole) => Percent(whole == 0 ? 0 : (double)part / whole);

    private static string Percent(double fraction) =>
        (fraction * 100).ToString("0.0", CultureInfo.InvariantCulture) + "%";

    private static string Cell(string text) => OneLine(text).Replace("|", "\\|");

    private static string OneLine(string text) =>
        text.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ');

    private static void Line(StringBuilder sb, string text = "") => sb.Append(text).Append('\n');
}
