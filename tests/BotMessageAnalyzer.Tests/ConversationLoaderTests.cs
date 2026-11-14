using BotMessageAnalyzer.Input;

namespace BotMessageAnalyzer.Tests;

public class ConversationLoaderTests
{
    private const string Valid = """
        {
          "synthetic": true,
          "conversations": [
            {
              "id": "c-001",
              "channel": "chat",
              "messages": [
                { "role": "bot", "text": "Hello, how can I help?" },
                { "role": "customer", "text": "  Is there parking?  " },
                { "role": "bot", "text": "Sorry, I did not understand that." }
              ]
            }
          ]
        }
        """;

    [Fact]
    public void Valid_file_loads_and_trims_text()
    {
        var result = ConversationLoader.Parse(Valid);

        Assert.True(result.IsValid);
        var conversation = Assert.Single(result.Conversations);
        Assert.Equal("c-001", conversation.Id);
        Assert.Equal("chat", conversation.Channel);
        Assert.Equal(3, conversation.Messages.Count);
        Assert.Equal("Is there parking?", conversation.Messages[1].Text);
    }

    [Fact]
    public void Channel_is_optional()
    {
        var json = Single("""{ "id": "a", "messages": [ { "role": "customer", "text": "hi" } ] }""");

        var result = ConversationLoader.Parse(json);

        Assert.True(result.IsValid);
        Assert.Null(result.Conversations[0].Channel);
    }

    [Fact]
    public void Missing_id_is_reported_by_position()
    {
        var json = Single("""{ "messages": [ { "role": "customer", "text": "hi" } ] }""");

        var result = ConversationLoader.Parse(json);

        Assert.Contains(result.Errors, e => e.Contains("#1") && e.Contains("id"));
    }

    [Fact]
    public void Duplicate_id_is_reported()
    {
        const string conversation = """{ "id": "a", "messages": [ { "role": "customer", "text": "hi" } ] }""";
        var json = $$"""{ "conversations": [ {{conversation}}, {{conversation}} ] }""";

        var result = ConversationLoader.Parse(json);

        Assert.Contains(result.Errors, e => e.Contains("a") && e.Contains("duplicate"));
    }

    [Fact]
    public void Bad_role_is_reported_with_conversation_id()
    {
        var json = Single("""{ "id": "c-7", "messages": [ { "role": "user", "text": "hi" } ] }""");

        var result = ConversationLoader.Parse(json);

        Assert.Contains(result.Errors, e => e.Contains("c-7") && e.Contains("role"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_text_after_trimming_is_reported(string text)
    {
        var json = Single($$"""{ "id": "c-8", "messages": [ { "role": "customer", "text": "{{text}}" } ] }""");

        var result = ConversationLoader.Parse(json);

        Assert.Contains(result.Errors, e => e.Contains("c-8") && e.Contains("text"));
    }

    [Fact]
    public void Conversation_without_customer_message_is_reported()
    {
        var json = Single("""{ "id": "c-9", "messages": [ { "role": "bot", "text": "Hello" } ] }""");

        var result = ConversationLoader.Parse(json);

        Assert.Contains(result.Errors, e => e.Contains("c-9") && e.Contains("customer"));
    }

    [Fact]
    public void Empty_conversations_array_is_rejected()
    {
        var result = ConversationLoader.Parse("""{ "conversations": [] }""");

        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("[]")]
    [InlineData("""{ "conversations": "x" }""")]
    public void Malformed_or_wrongly_shaped_input_is_rejected(string json)
    {
        var result = ConversationLoader.Parse(json);

        Assert.False(result.IsValid);
        Assert.Empty(result.Conversations);
    }

    [Fact]
    public void All_errors_are_collected_together()
    {
        const string json = """
            { "conversations": [
              { "id": "x", "messages": [ { "role": "robot", "text": "hi" } ] },
              { "id": "y", "messages": [ { "role": "customer", "text": " " } ] }
            ] }
            """;

        var result = ConversationLoader.Parse(json);

        Assert.True(result.Errors.Count >= 2);
        Assert.Contains(result.Errors, e => e.Contains('x'));
        Assert.Contains(result.Errors, e => e.Contains('y'));
    }

    [Fact]
    public void Missing_file_is_an_error_not_an_exception()
    {
        var result = ConversationLoader.LoadFile(Path.Combine(Path.GetTempPath(), "does-not-exist.json"));

        Assert.False(result.IsValid);
    }

    private static string Single(string conversation) => $$"""{ "conversations": [ {{conversation}} ] }""";
}
