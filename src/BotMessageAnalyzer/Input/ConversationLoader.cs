using System.Text.Json;

namespace BotMessageAnalyzer.Input;

public sealed record LoadResult(IReadOnlyList<Conversation> Conversations, IReadOnlyList<string> Errors)
{
    public bool IsValid => Errors.Count == 0;
}

public static class ConversationLoader
{
    public static LoadResult LoadFile(string path)
    {
        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Fail($"Cannot read '{path}': {ex.Message}");
        }
        return Parse(json);
    }

    public static LoadResult Parse(string json)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            return Fail($"Not valid JSON: {ex.Message}");
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("conversations", out var list)
                || list.ValueKind != JsonValueKind.Array)
            {
                return Fail("Top level must be an object with a \"conversations\" array.");
            }
            if (list.GetArrayLength() == 0)
            {
                return Fail("\"conversations\" is empty.");
            }

            var errors = new List<string>();
            var conversations = new List<Conversation>();
            var seenIds = new HashSet<string>(StringComparer.Ordinal);
            var position = 0;

            foreach (var item in list.EnumerateArray())
            {
                position++;
                var conversation = ReadConversation(item, position, seenIds, errors);
                if (conversation is not null)
                {
                    conversations.Add(conversation);
                }
            }

            return new LoadResult(conversations, errors);
        }
    }

    private static Conversation? ReadConversation(
        JsonElement item, int position, HashSet<string> seenIds, List<string> errors)
    {
        if (item.ValueKind != JsonValueKind.Object)
        {
            errors.Add($"Conversation #{position}: must be an object.");
            return null;
        }

        var id = ReadString(item, "id");
        var label = string.IsNullOrWhiteSpace(id) ? $"#{position}" : id;
        var before = errors.Count;

        if (string.IsNullOrWhiteSpace(id))
        {
            errors.Add($"Conversation #{position}: missing or empty id.");
        }
        else if (!seenIds.Add(id))
        {
            errors.Add($"Conversation {id}: duplicate id.");
        }

        var channel = ReadString(item, "channel");
        var messages = new List<Message>();

        if (!item.TryGetProperty("messages", out var rawMessages) || rawMessages.ValueKind != JsonValueKind.Array)
        {
            errors.Add($"Conversation {label}: \"messages\" must be an array.");
        }
        else
        {
            var number = 0;
            foreach (var raw in rawMessages.EnumerateArray())
            {
                number++;
                var message = ReadMessage(raw, label, number, errors);
                if (message is not null)
                {
                    messages.Add(message);
                }
            }

            if (!messages.Any(m => m.Role == "customer") && errors.Count == before)
            {
                errors.Add($"Conversation {label}: needs at least one customer message.");
            }
        }

        return errors.Count == before ? new Conversation(id!, channel, messages) : null;
    }

    private static Message? ReadMessage(JsonElement raw, string label, int number, List<string> errors)
    {
        if (raw.ValueKind != JsonValueKind.Object)
        {
            errors.Add($"Conversation {label}, message {number}: must be an object.");
            return null;
        }

        var ok = true;
        var role = ReadString(raw, "role");
        if (role != "customer" && role != "bot")
        {
            errors.Add($"Conversation {label}, message {number}: role must be \"customer\" or \"bot\".");
            ok = false;
        }

        var text = ReadString(raw, "text")?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            errors.Add($"Conversation {label}, message {number}: text is empty.");
            ok = false;
        }

        return ok ? new Message(role!, text!) : null;
    }

    private static string? ReadString(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static LoadResult Fail(string error) => new([], [error]);
}
