using BotMessageAnalyzer.Input;

namespace BotMessageAnalyzer.Classification;

public interface IClassifier
{
    /// <summary>Returns exactly one result per turn, in the same order. A turn the model could not judge is Unclassified.</summary>
    Task<IReadOnlyList<TurnResult>> ClassifyAsync(IReadOnlyList<Turn> turns, CancellationToken cancellationToken);

    /// <summary>Maps each label to a canonical label, or returns null when the request failed.</summary>
    Task<IReadOnlyDictionary<string, string>?> MergeTopicsAsync(IReadOnlyList<string> labels, CancellationToken cancellationToken);
}
