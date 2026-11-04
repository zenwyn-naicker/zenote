using Zenote.Core.Domain;

namespace Zenote.Core.Abstractions;

public interface IDocumentRepository
{
    Task<Document?> FindByHashAsync(Guid notebookId, string contentHash, CancellationToken ct);
    Task<IReadOnlyList<Document>> ListAsync(Guid notebookId, CancellationToken ct);
    Task<IReadOnlyList<Document>> ListUnfinishedAsync(CancellationToken ct);
    Task SaveAsync(Document document, CancellationToken ct);
    Task UpdateStatusAsync(Guid id, DocumentStatus status, string? error, CancellationToken ct);
    Task<IReadOnlyList<long>> InsertChunksAsync(Guid documentId, IReadOnlyList<ChunkDraft> chunks, CancellationToken ct);
    Task<IReadOnlyList<Chunk>> GetChunksAsync(IReadOnlyList<long> ids, CancellationToken ct);
    Task DeleteChunksAsync(Guid documentId, CancellationToken ct);
    Task DeleteAsync(Guid id, CancellationToken ct);
}
