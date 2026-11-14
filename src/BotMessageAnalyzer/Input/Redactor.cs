using System.Text.RegularExpressions;

namespace BotMessageAnalyzer.Input;

public static class Redactor
{
    private static readonly Regex Email = new(
        @"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Runs of digits that may contain spaces, dashes, dots, brackets or a leading plus.
    private static readonly Regex DigitRun = new(
        @"(?<!\w)\+?[\d(][\d ()\-.]{5,}\d(?!\w)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex Date = new(
        @"^(\d{4}-\d{2}-\d{2}|\d{1,2}[./]\d{1,2}[./]\d{2,4})$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string Redact(string text)
    {
        var withoutEmails = Email.Replace(text, "[email]");
        return DigitRun.Replace(withoutEmails, ReplaceDigits);
    }

    public static Conversation Redact(Conversation conversation) =>
        conversation with
        {
            Messages = conversation.Messages
                .Select(m => m with { Text = Redact(m.Text) })
                .ToList()
        };

    private static string ReplaceDigits(Match match)
    {
        var value = match.Value;
        if (value.Count(char.IsDigit) < 7 || Date.IsMatch(value))
        {
            return value;
        }
        return value.All(char.IsDigit) ? "[number]" : "[phone]";
    }
}
