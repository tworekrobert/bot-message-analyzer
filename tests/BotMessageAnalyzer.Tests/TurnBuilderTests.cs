using BotMessageAnalyzer.Input;

namespace BotMessageAnalyzer.Tests;

public class TurnBuilderTests
{
    [Fact]
    public void Consecutive_customer_messages_merge_into_one_turn()
    {
        var turns = TurnBuilder.Build(Make.Conversation(
            ("customer", "hi"),
            ("customer", "I need parking"),
            ("bot", "Sorry, I did not understand that.")));

        var turn = Assert.Single(turns);
        Assert.Equal("hi I need parking", turn.CustomerText);
        Assert.Equal("Sorry, I did not understand that.", turn.BotReply);
    }

    [Fact]
    public void Several_bot_messages_form_one_reply()
    {
        var turns = TurnBuilder.Build(Make.Conversation(
            ("customer", "hours?"),
            ("bot", "We open at 8."),
            ("bot", "We close at 18.")));

        Assert.Equal("We open at 8. We close at 18.", Assert.Single(turns).BotReply);
    }

    [Fact]
    public void Greeting_before_the_first_customer_message_is_ignored()
    {
        var turns = TurnBuilder.Build(Make.Conversation(
            ("bot", "Hello!"),
            ("bot", "How can I help?"),
            ("customer", "hours?"),
            ("bot", "8 to 18.")));

        var turn = Assert.Single(turns);
        Assert.Equal("8 to 18.", turn.BotReply);
    }

    [Fact]
    public void Trailing_customer_message_becomes_a_turn_without_reply()
    {
        var turns = TurnBuilder.Build(Make.Conversation(
            ("customer", "hours?"),
            ("bot", "8 to 18."),
            ("customer", "thanks")));

        Assert.Equal(2, turns.Count);
        Assert.Null(turns[1].BotReply);
        Assert.Equal("thanks", turns[1].CustomerText);
    }

    [Fact]
    public void Turns_are_numbered_from_one_in_conversation_order()
    {
        var turns = TurnBuilder.Build(Make.Conversation(
            ("customer", "a"), ("bot", "1"), ("customer", "b"), ("bot", "2")));

        Assert.Equal(new[] { 1, 2 }, turns.Select(t => t.Index).ToArray());
        Assert.Equal("c-001", turns[0].ConversationId);
    }

    [Fact]
    public void Customer_text_and_bot_reply_are_cut_to_1000_characters()
    {
        var turns = TurnBuilder.Build(Make.Conversation(
            ("customer", new string('a', 1500)),
            ("bot", new string('b', 1500))));

        var turn = Assert.Single(turns);
        Assert.Equal(1000, turn.CustomerText.Length);
        Assert.Equal(1000, turn.BotReply!.Length);
    }
}
