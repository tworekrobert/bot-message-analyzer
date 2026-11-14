using BotMessageAnalyzer.Classification;

namespace BotMessageAnalyzer.Tests;

public class ConfigLoaderTests
{
    [Fact]
    public void Full_config_is_read()
    {
        var result = ConfigLoader.Parse("""
            { "model": "m1", "batchSize": 10, "requestDelayMs": 100, "maxRetries": 2, "fallbackPhrases": ["x"] }
            """);

        var config = result.Config!;
        Assert.Equal("m1", config.Model);
        Assert.Equal(10, config.BatchSize);
        Assert.Equal(100, config.RequestDelayMs);
        Assert.Equal(2, config.MaxRetries);
        Assert.Equal(new[] { "x" }, config.FallbackPhrases.ToArray());
    }

    [Fact]
    public void Only_the_model_is_required()
    {
        var config = ConfigLoader.Parse("""{ "model": "m1" }""").Config!;

        Assert.Equal(15, config.BatchSize);
        Assert.Equal(4, config.MaxRetries);
        Assert.Contains("can you rephrase", config.FallbackPhrases);
    }

    [Theory]
    [InlineData("""{ "batchSize": 5 }""")]
    [InlineData("""{ "model": "m", "batchSize": 0 }""")]
    [InlineData("""{ "model": "m", "requestDelayMs": -1 }""")]
    [InlineData("""{ "model": "m", "maxRetries": 99 }""")]
    [InlineData("{ nope")]
    public void Invalid_config_is_rejected_with_errors(string json)
    {
        var result = ConfigLoader.Parse(json);

        Assert.Null(result.Config);
        Assert.True(result.Errors.Count > 0);
    }

    [Fact]
    public void Missing_file_is_an_error()
    {
        var result = ConfigLoader.LoadFile(Path.Combine(Path.GetTempPath(), "no-such-config.json"));

        Assert.Null(result.Config);
    }
}
