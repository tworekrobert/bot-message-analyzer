using System.Text.Json;

namespace BotMessageAnalyzer.Classification;

public sealed record AnalyzerConfig(
    string Model,
    int BatchSize,
    int RequestDelayMs,
    int MaxRetries,
    IReadOnlyList<string> FallbackPhrases);

public sealed record ConfigResult(AnalyzerConfig? Config, IReadOnlyList<string> Errors);

public static class ConfigLoader
{
    private static readonly string[] DefaultPhrases = ["did not understand", "can you rephrase", "i can only help with"];

    private sealed class Raw
    {
        public string? Model { get; set; }
        public int? BatchSize { get; set; }
        public int? RequestDelayMs { get; set; }
        public int? MaxRetries { get; set; }
        public string[]? FallbackPhrases { get; set; }
    }

    public static ConfigResult LoadFile(string path)
    {
        try
        {
            return Parse(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new ConfigResult(null, [$"Cannot read config '{path}': {ex.Message}"]);
        }
    }

    public static ConfigResult Parse(string json)
    {
        Raw? raw;
        try
        {
            raw = JsonSerializer.Deserialize<Raw>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException ex)
        {
            return new ConfigResult(null, [$"Config is not valid JSON: {ex.Message}"]);
        }
        raw ??= new Raw();

        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(raw.Model))
        {
            errors.Add("Config must set \"model\" (see samples/config.json).");
        }
        var batchSize = raw.BatchSize ?? 15;
        if (batchSize is < 1 or > 50)
        {
            errors.Add("\"batchSize\" must be between 1 and 50.");
        }
        var delay = raw.RequestDelayMs ?? 4500;
        if (delay < 0)
        {
            errors.Add("\"requestDelayMs\" must not be negative.");
        }
        var retries = raw.MaxRetries ?? 4;
        if (retries is < 0 or > 10)
        {
            errors.Add("\"maxRetries\" must be between 0 and 10.");
        }

        return errors.Count > 0
            ? new ConfigResult(null, errors)
            : new ConfigResult(new AnalyzerConfig(raw.Model!.Trim(), batchSize, delay, retries, raw.FallbackPhrases ?? DefaultPhrases), errors);
    }
}
