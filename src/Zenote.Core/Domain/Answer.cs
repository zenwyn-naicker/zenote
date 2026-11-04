namespace Zenote.Core.Domain;

public enum MessageRole { User, Assistant }

public sealed record ChatTurn(MessageRole Role, string Text);

/// <param name="Marker">The number used in the answer text, or 0 for a page the answer doesn't cite directly.</param>
public sealed record Citation(int Marker, Guid DocumentId, string FileName, int Page, string Snippet);

/// <param name="Citations">What the answer cites. Otherwise the pages it was given, or the closest matches when nothing was found.</param>
public sealed record Answer(string Text, bool NotFound, IReadOnlyList<Citation> Citations);

public abstract record AnswerEvent;

public sealed record TokenReceived(string Text) : AnswerEvent;

public sealed record AnswerCompleted(Answer Answer) : AnswerEvent;
