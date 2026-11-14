using BotMessageAnalyzer;
using BotMessageAnalyzer.Input;

const string usage = """
    Usage:
      bma analyze <input.json> [--out ./out] [--config config.json] [--cache path]
      bma validate <input.json>
    """;

try
{
    if (args.Length >= 1 && args[0] == "analyze")
    {
        return await AnalyzeCommand.RunAsync(args[1..]);
    }
    if (args.Length == 2 && args[0] == "validate")
    {
        return Validate(args[1]);
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Unexpected error: {ex.Message}");
    return AnalyzeCommand.Unexpected;
}

Console.Error.WriteLine(usage);
return AnalyzeCommand.InvalidInput;

static int Validate(string path)
{
    var result = ConversationLoader.LoadFile(path);
    if (!result.IsValid)
    {
        Console.Error.WriteLine($"{result.Errors.Count} problem(s) in {path}:");
        foreach (var error in result.Errors)
        {
            Console.Error.WriteLine($"  {error}");
        }
        return AnalyzeCommand.InvalidInput;
    }

    var customerMessages = result.Conversations.Sum(c => c.Messages.Count(m => m.Role == "customer"));
    Console.WriteLine($"OK: {result.Conversations.Count} conversations, {customerMessages} customer messages.");
    return AnalyzeCommand.Ok;
}
