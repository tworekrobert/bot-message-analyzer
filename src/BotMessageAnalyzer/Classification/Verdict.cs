using BotMessageAnalyzer.Input;

namespace BotMessageAnalyzer.Classification;

public enum Verdict { Answered, NotUnderstood, WrongAnswer, Unclassified }

public enum VerdictSource { Rule, Model, Failed }

public record TurnResult(Turn Turn, Verdict Verdict, string? Topic, string? Reason, VerdictSource Source);
