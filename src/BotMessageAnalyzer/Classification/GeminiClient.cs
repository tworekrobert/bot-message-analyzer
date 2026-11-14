using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BotMessageAnalyzer.Classification;

/// <summary>Sends one prompt to the Gemini generateContent endpoint and returns the JSON text the model produced.</summary>
public sealed class GeminiClient(
    HttpClient http,
    string apiKey,
    string model,
    int maxRetries,
    TimeSpan requestDelay,
    Func<TimeSpan, CancellationToken, Task>? wait = null,
    Action<string>? log = null)
{
    private static readonly TimeSpan MaxRetryAfter = TimeSpan.FromSeconds(120);

    private readonly Func<TimeSpan, CancellationToken, Task> pause = wait ?? ((delay, token) => Task.Delay(delay, token));
    private bool anyRequestSent;

    public string? LastError { get; private set; }

    /// <summary>True after the API rejected a request for a reason retrying cannot fix (bad key, bad request, unknown model).</summary>
    public bool Fatal { get; private set; }

    public async Task<string?> GenerateJsonAsync(string prompt, JsonNode responseSchema, CancellationToken cancellationToken)
    {
        if (Fatal)
        {
            return null;
        }
        if (anyRequestSent)
        {
            await pause(requestDelay, cancellationToken);
        }
        anyRequestSent = true;

        var body = BuildBody(prompt, responseSchema);
        for (var attempt = 0; ; attempt++)
        {
            TimeSpan? retryAfter = null;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post,
                    $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent")
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json")
                };
                request.Headers.Add("x-goog-api-key", apiKey);

                using var response = await http.SendAsync(request, cancellationToken);
                var content = await response.Content.ReadAsStringAsync(cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    return ExtractText(content);
                }

                LastError = $"HTTP {(int)response.StatusCode}: {ErrorMessage(content)}";
                if ((int)response.StatusCode is not (429 or 503))
                {
                    Fatal = (int)response.StatusCode is 400 or 401 or 403 or 404;
                    log?.Invoke(LastError);
                    return null;
                }
                retryAfter = ReadRetryAfter(response);
            }
            catch (HttpRequestException ex)
            {
                LastError = $"Network error: {ex.Message}";
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                LastError = "The request timed out.";
            }

            if (attempt >= maxRetries)
            {
                log?.Invoke($"{LastError} Giving up on this request.");
                return null;
            }
            var pauseFor = retryAfter ?? TimeSpan.FromSeconds(Math.Pow(2, attempt));
            log?.Invoke($"{LastError} Retrying in {pauseFor.TotalSeconds:0} s (retry {attempt + 1} of {maxRetries}).");
            await pause(pauseFor, cancellationToken);
        }
    }

    private static string BuildBody(string prompt, JsonNode responseSchema) =>
        new JsonObject
        {
            ["contents"] = new JsonArray(new JsonObject
            {
                ["role"] = "user",
                ["parts"] = new JsonArray(new JsonObject { ["text"] = prompt })
            }),
            ["generationConfig"] = new JsonObject
            {
                ["temperature"] = 0,
                ["responseMimeType"] = "application/json",
                ["responseJsonSchema"] = responseSchema.DeepClone()
            }
        }.ToJsonString();

    private string? ExtractText(string content)
    {
        try
        {
            using var doc = JsonDocument.Parse(content);
            if (doc.RootElement.TryGetProperty("candidates", out var candidates)
                && candidates.ValueKind == JsonValueKind.Array
                && candidates.GetArrayLength() > 0
                && candidates[0].TryGetProperty("content", out var message)
                && message.TryGetProperty("parts", out var parts)
                && parts.ValueKind == JsonValueKind.Array)
            {
                var text = new StringBuilder();
                foreach (var part in parts.EnumerateArray())
                {
                    var isThought = part.TryGetProperty("thought", out var thought) && thought.ValueKind == JsonValueKind.True;
                    if (!isThought && part.TryGetProperty("text", out var value) && value.ValueKind == JsonValueKind.String)
                    {
                        text.Append(value.GetString());
                    }
                }
                if (text.Length > 0)
                {
                    return text.ToString();
                }
            }
        }
        catch (JsonException)
        {
            LastError = "The response was not valid JSON.";
            return null;
        }

        LastError = "The response contained no text.";
        return null;
    }

    private static string ErrorMessage(string content)
    {
        try
        {
            using var doc = JsonDocument.Parse(content);
            if (doc.RootElement.TryGetProperty("error", out var error)
                && error.TryGetProperty("message", out var message)
                && message.ValueKind == JsonValueKind.String)
            {
                return Shorten(message.GetString()!);
            }
        }
        catch (JsonException)
        {
        }
        return Shorten(content);
    }

    private static string Shorten(string text)
    {
        var oneLine = text.ReplaceLineEndings(" ").Trim();
        return oneLine.Length > 300 ? oneLine[..300] + "..." : oneLine;
    }

    private static TimeSpan? ReadRetryAfter(HttpResponseMessage response)
    {
        var header = response.Headers.RetryAfter;
        var delay = header?.Delta ?? (header?.Date is { } date ? date - DateTimeOffset.UtcNow : null);
        if (delay is null || delay < TimeSpan.Zero)
        {
            return null;
        }
        return delay > MaxRetryAfter ? MaxRetryAfter : delay;
    }
}
