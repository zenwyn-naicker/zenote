using Zenote.Core.Domain;

namespace Zenote.Core.Abstractions;

public interface IConversationRepository
{
    Task<Conversation> CreateAsync(Guid notebookId, string title, CancellationToken ct);
    Task<IReadOnlyList<Conversation>> ListAsync(Guid notebookId, CancellationToken ct);
    Task<IReadOnlyList<Message>> GetMessagesAsync(Guid conversationId, CancellationToken ct);
    Task AppendMessageAsync(Message message, CancellationToken ct);
    Task RenameAsync(Guid id, string title, CancellationToken ct);
    Task DeleteAsync(Guid id, CancellationToken ct);
}
