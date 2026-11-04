using Zenote.Core.Domain;

namespace Zenote.Core.Abstractions;

public interface IDocumentRepository
{
    Task<Document?> FindByHashAsync(Guid notebookId, string contentHash, CancellationToken ct);
    Task<IReadOnlyList<Document>> ListAsync(Guid notebookId, CancellationToken ct);
    Task SaveAsync(Document document, CancellationToken ct);
    Task DeleteAsync(Guid id, CancellationToken ct);
}
