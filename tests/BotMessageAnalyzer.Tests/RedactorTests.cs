using BotMessageAnalyzer.Input;

namespace BotMessageAnalyzer.Tests;

public class RedactorTests
{
    [Theory]
    [InlineData("write to anna.kowalska@example.com please", "write to [email] please")]
    [InlineData("it is J_Smith+clinic@mail.example.org.", "it is [email].")]
    public void Emails_are_replaced(string input, string expected)
    {
        Assert.Equal(expected, Redactor.Redact(input));
    }

    [Theory]
    [InlineData("call me on +48 600 100 200 tomorrow")]
    [InlineData("call me on 600-100-200 tomorrow")]
    [InlineData("call me on 07700 900123 tomorrow")]
    [InlineData("call me on (022) 555 01 42 tomorrow")]
    [InlineData("call me on 600.100.200 tomorrow")]
    public void Phone_numbers_are_replaced(string input)
    {
        Assert.Equal("call me on [phone] tomorrow", Redactor.Redact(input));
    }

    [Theory]
    [InlineData("my policy number is 44812290", "my policy number is [number]")]
    [InlineData("card 1234567890123456 ok", "card [number] ok")]
    public void Long_digit_runs_are_replaced(string input, string expected)
    {
        Assert.Equal(expected, Redactor.Redact(input));
    }

    [Theory]
    [InlineData("Tuesday 18 November at 11:00")]
    [InlineData("the visit on 2026-11-09 was fine")]
    [InlineData("the visit on 14.11.2026 was fine")]
    [InlineData("I am 34 years old and live at number 12")]
    [InlineData("take bus 22 at 9:30")]
    [InlineData("my code is 123456")]
    public void Ordinary_text_is_left_alone(string input)
    {
        Assert.Equal(input, Redactor.Redact(input));
    }

    [Fact]
    public void Redaction_applies_to_every_message_of_a_conversation()
    {
        var conversation = Make.Conversation(
            ("bot", "Confirmation sent to j.kowal@example.com."),
            ("customer", "my number is 07700 900123"));

        var redacted = Redactor.Redact(conversation);

        Assert.Equal("Confirmation sent to [email].", redacted.Messages[0].Text);
        Assert.Equal("my number is [phone]", redacted.Messages[1].Text);
        Assert.Equal("bot", redacted.Messages[0].Role);
    }
}
