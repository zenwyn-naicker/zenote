namespace Zenote.Core.Domain;

public sealed record Conversation(Guid Id, Guid NotebookId, string Title, DateTimeOffset UpdatedAt);

public sealed record Message(
    Guid Id,
    Guid ConversationId,
    MessageRole Role,
    string Content,
    bool NotFound,
    IReadOnlyList<Citation> Citations,
    DateTimeOffset CreatedAt);
