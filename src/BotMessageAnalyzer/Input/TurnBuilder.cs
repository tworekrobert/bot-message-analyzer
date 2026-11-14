namespace BotMessageAnalyzer.Input;

public static class TurnBuilder
{
    public const int MaxLength = 1000;

    public static IReadOnlyList<Turn> Build(Conversation conversation)
    {
        var turns = new List<Turn>();
        var customer = new List<string>();
        var bot = new List<string>();

        foreach (var message in conversation.Messages)
        {
            if (message.Role == "customer")
            {
                if (bot.Count > 0)
                {
                    Flush();
                }
                customer.Add(message.Text);
            }
            else if (customer.Count > 0)
            {
                bot.Add(message.Text);
            }
            // Bot messages before the first customer message are greetings and are dropped.
        }
        Flush();
        return turns;

        void Flush()
        {
            if (customer.Count == 0)
            {
                return;
            }
            var reply = bot.Count == 0 ? null : Cut(string.Join(' ', bot));
            turns.Add(new Turn(conversation.Id, turns.Count + 1, Cut(string.Join(' ', customer)), reply));
            customer.Clear();
            bot.Clear();
        }
    }

    private static string Cut(string text) => text.Length > MaxLength ? text[..MaxLength] : text;
}
