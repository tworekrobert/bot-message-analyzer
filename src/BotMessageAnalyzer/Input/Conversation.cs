namespace BotMessageAnalyzer.Input;

public record Message(string Role, string Text);

public record Conversation(string Id, string? Channel, IReadOnlyList<Message> Messages);

public record Turn(string ConversationId, int Index, string CustomerText, string? BotReply);
