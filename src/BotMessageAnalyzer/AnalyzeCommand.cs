using System.Text;
using BotMessageAnalyzer.Classification;
using BotMessageAnalyzer.Input;
using BotMessageAnalyzer.Reporting;

namespace BotMessageAnalyzer;

public static class AnalyzeCommand
{
    public const int Ok = 0;
    public const int Unexpected = 1;
    public const int InvalidInput = 2;
    public const int TooManyUnclassified = 3;

    private const string ClassifyPromptFile = "classify.v1.md";
    private const string MergePromptFile = "merge-topics.v1.md";
    private const string DefaultCachePath = ".cache/responses.jsonl";

    private sealed record Options(string Input, string OutDirectory, string? ConfigPath, string CachePath);

    public static async Task<int> RunAsync(
        string[] args,
        HttpMessageHandler? handler = null,
        Func<string, string?>? environment = null)
    {
        var options = ParseOptions(args);
        if (options is null)
        {
            Console.Error.WriteLine("Usage: bma analyze <input.json> [--out ./out] [--config config.json] [--cache path]");
            return InvalidInput;
        }

        if (options.ConfigPath is null)
        {
            return Fail("No config given. Pass --config (see samples/config.json).");
        }
        var configResult = ConfigLoader.LoadFile(options.ConfigPath);
        if (configResult.Config is not { } config)
        {
            return Fail(configResult.Errors);
        }

        var load = ConversationLoader.LoadFile(options.Input);
        if (!load.IsValid)
        {
            return Fail(load.Errors);
        }

        var classifyPrompt = PromptFiles.Read(ClassifyPromptFile);
        var mergePrompt = PromptFiles.Read(MergePromptFile);

        var turns = load.Conversations.Select(Redactor.Redact).SelectMany(TurnBuilder.Build).ToList();
        var cache = ResultCache.Open(options.CachePath);

        var apiKey = (environment ?? Environment.GetEnvironmentVariable)("GEMINI_API_KEY")?.Trim();
        using var http = new HttpClient(handler ?? new HttpClientHandler()) { Timeout = TimeSpan.FromSeconds(90) };
        var client = string.IsNullOrEmpty(apiKey)
            ? null
            : new GeminiClient(http, apiKey, config.Model, config.MaxRetries, TimeSpan.FromMilliseconds(config.RequestDelayMs), log: Console.WriteLine);
        var classifier = new GeminiClassifier(client, cache, config.Model, config.BatchSize, classifyPrompt, mergePrompt, Console.WriteLine);

        var missing = classifier.CountUncached(turns);
        if (missing > 0 && client is null)
        {
            return Fail($"{missing} of {turns.Count} turns are not in the cache and GEMINI_API_KEY is not set.");
        }
        Console.WriteLine($"{load.Conversations.Count} conversations, {turns.Count} turns, {turns.Count - missing} cached.");

        var outcome = await new TurnAnalyzer(classifier, config.FallbackPhrases).RunAsync(turns, CancellationToken.None);
        var gaps = GapAggregator.Build(outcome.Results);

        var context = new ReportContext(
            Path.GetFileName(options.Input),
            load.Conversations.Count,
            config.Model,
            $"{Path.GetFileNameWithoutExtension(ClassifyPromptFile)}, {Path.GetFileNameWithoutExtension(MergePromptFile)}",
            DateOnly.FromDateTime(DateTime.Now),
            outcome.TopicsConsolidated);

        Directory.CreateDirectory(options.OutDirectory);
        Write(options.OutDirectory, "report.md", MarkdownReport.Build(context, outcome.Results, gaps));
        Write(options.OutDirectory, "gaps.csv", CsvWriter.Gaps(gaps));
        Write(options.OutDirectory, "training-candidates.csv", CsvWriter.TrainingCandidates(outcome.Results));

        var failed = outcome.Results.Count(r => r.Verdict is Verdict.NotUnderstood or Verdict.WrongAnswer);
        var unclassified = outcome.Results.Count(r => r.Verdict == Verdict.Unclassified);
        Console.WriteLine($"{failed} failed turns in {gaps.Count} topics. Files written to {options.OutDirectory}.");

        if (unclassified > 0)
        {
            Console.Error.WriteLine($"warning: {unclassified} of {turns.Count} turns could not be classified.");
        }
        if (!outcome.TopicsConsolidated)
        {
            Console.Error.WriteLine("warning: topic consolidation did not complete; topics were only lowercased and trimmed.");
        }
        return unclassified * 10 > turns.Count ? TooManyUnclassified : Ok;
    }

    private static Options? ParseOptions(string[] args)
    {
        string? input = null, config = null;
        var output = "./out";
        var cache = DefaultCachePath;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--out" when i + 1 < args.Length:
                    output = args[++i];
                    break;
                case "--config" when i + 1 < args.Length:
                    config = args[++i];
                    break;
                case "--cache" when i + 1 < args.Length:
                    cache = args[++i];
                    break;
                case var flag when flag.StartsWith("--", StringComparison.Ordinal):
                    return null;
                default:
                    if (input is not null)
                    {
                        return null;
                    }
                    input = args[i];
                    break;
            }
        }
        return input is null ? null : new Options(input, output, config, cache);
    }

    private static void Write(string directory, string name, string content) =>
        File.WriteAllText(Path.Combine(directory, name), content, new UTF8Encoding(false));

    private static int Fail(string error) => Fail([error]);

    private static int Fail(IReadOnlyList<string> errors)
    {
        foreach (var error in errors)
        {
            Console.Error.WriteLine($"error: {error}");
        }
        return InvalidInput;
    }
}
