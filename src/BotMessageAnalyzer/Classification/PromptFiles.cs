using System.Reflection;

namespace BotMessageAnalyzer.Classification;

/// <summary>The prompt files in the repository's prompts folder, compiled into the tool.</summary>
public static class PromptFiles
{
    public static string Read(string fileName)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"prompts.{fileName}")
            ?? throw new InvalidOperationException($"Prompt file {fileName} is missing from the build.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
