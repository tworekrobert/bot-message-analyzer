using BotMessageAnalyzer.Classification;
using BotMessageAnalyzer.Reporting;

namespace BotMessageAnalyzer.Tests;

public class OutputTests
{
    private const string Fallback = "Sorry, I did not understand that.";

    private static readonly TurnResult[] FixedResults =
    [
        Make.Result("c-001", 1, "Is there parking at the clinic?", Fallback, Verdict.NotUnderstood, "parking information", VerdictSource.Rule),
        Make.Result("c-002", 1, "where can I park", Fallback, Verdict.NotUnderstood, "parking information", VerdictSource.Rule),
        Make.Result("c-003", 2, "do you have a car park", Fallback, Verdict.NotUnderstood, "parking information", VerdictSource.Rule),
        Make.Result("c-004", 1, "Are you open on Christmas Eve?", "We are open Monday to Friday 8:00 to 18:00.", Verdict.WrongAnswer, "holiday opening hours"),
        Make.Result("c-005", 1, "I want to cancel", "Sure. Which day would you like?", Verdict.WrongAnswer, "cancelling an appointment"),
        Make.Result("c-005", 2, "no cancel", "Can you rephrase that, please?", Verdict.NotUnderstood, "cancelling an appointment", VerdictSource.Rule),
        Make.Result("c-006", 1, "book tuesday", "Booked.", Verdict.Answered, "booking"),
        Make.Result("c-007", 1, "opening hours?", "We open at 8:00.", Verdict.Answered, "opening hours"),
        Make.Result("c-008", 1, "any results?", "Sorry, something went wrong.", Verdict.Unclassified, null, VerdictSource.Failed),
        Make.Result("c-009", 3, "invoice for October", null, Verdict.NotUnderstood, "billing | invoices", VerdictSource.Rule)
    ];

    private static readonly ReportContext Context = new(
        "sample.json", 9, "test-model", "classify.v1, merge-topics.v1", new DateOnly(2026, 1, 15), false);

    [Fact]
    public void Report_matches_the_golden_file()
    {
        var report = MarkdownReport.Build(Context, FixedResults, GapAggregator.Build(FixedResults));

        var expected = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Golden", "report.expected.md"));
        Assert.Equal(Normalise(expected), Normalise(report));
    }

    [Fact]
    public void Report_says_when_topic_consolidation_completed()
    {
        var report = MarkdownReport.Build(Context with { TopicsConsolidated = true }, FixedResults, GapAggregator.Build(FixedResults));

        Assert.Contains("Topic consolidation completed.", report);
    }

    [Fact]
    public void Report_without_failed_turns_says_so()
    {
        var results = new[] { Make.Result("c-1", 1, "hi", "hello", Verdict.Answered, "greeting") };

        var report = MarkdownReport.Build(Context, results, GapAggregator.Build(results));

        Assert.Contains("No failed turns were found.", report);
    }

    [Fact]
    public void Gaps_csv_has_the_documented_columns_and_examples_joined_with_a_bar()
    {
        var csv = CsvWriter.Gaps(GapAggregator.Build(FixedResults));
        var lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal("rank,topic,turns,share_of_failed,not_understood,wrong_answer,suggested_action,examples", lines[0]);
        Assert.Equal(
            "1,parking information,3,42.9%,3,0,Add an intent and training phrases,Is there parking at the clinic?|where can I park|do you have a car park",
            lines[1]);
    }

    [Fact]
    public void Csv_fields_with_commas_quotes_and_newlines_are_quoted()
    {
        var results = new[]
        {
            Make.Result("c-1", 1, "a, b", "r", Verdict.NotUnderstood, "t"),
            Make.Result("c-2", 1, "say \"hi\"", "r", Verdict.NotUnderstood, "t"),
            Make.Result("c-3", 1, "line one\nline two", "r", Verdict.NotUnderstood, "t")
        };

        var csv = CsvWriter.TrainingCandidates(results);

        Assert.Contains("t,\"a, b\",c-1\r\n", csv);
        Assert.Contains("t,\"say \"\"hi\"\"\",c-2\r\n", csv);
        Assert.Contains("t,\"line one\nline two\",c-3\r\n", csv);
    }

    [Fact]
    public void Training_candidates_hold_only_not_understood_turns_without_duplicates_per_topic()
    {
        var results = new[]
        {
            Make.Result("c-1", 1, "where can I park", "r", Verdict.NotUnderstood, "parking"),
            Make.Result("c-2", 1, "Where can I park", "r", Verdict.NotUnderstood, "parking"),
            Make.Result("c-3", 1, "where can I park", "r", Verdict.NotUnderstood, "access"),
            Make.Result("c-4", 1, "holiday hours?", "r", Verdict.WrongAnswer, "holiday"),
            Make.Result("c-5", 1, "book", "r", Verdict.Answered, "booking")
        };

        var lines = CsvWriter.TrainingCandidates(results).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(
            new[]
            {
                "topic,customer_message,conversation_id",
                "access,where can I park,c-3",
                "parking,where can I park,c-1"
            },
            lines);
    }

    private static string Normalise(string text) => text.Replace("\r\n", "\n");
}
