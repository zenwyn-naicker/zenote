namespace Zenote.Core.Domain;

public enum MessageRole { User, Assistant }

public sealed record ChatTurn(MessageRole Role, string Text);

/// <param name="Sources">What the answer was based on, or the closest matches when nothing was found.</param>
public sealed record Answer(string Text, bool NotFound, IReadOnlyList<ScoredChunk> Sources);

public abstract record AnswerEvent;

public sealed record TokenReceived(string Text) : AnswerEvent;

public sealed record AnswerCompleted(Answer Answer) : AnswerEvent;
