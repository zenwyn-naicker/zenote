namespace Zenote.Core.Abstractions;

public interface IVectorStore
{
    Task AddAsync(Guid notebookId, IReadOnlyList<(long ChunkId, ReadOnlyMemory<float> Vector)> items, CancellationToken ct);
    Task<IReadOnlyList<(long ChunkId, double Similarity)>> SearchAsync(
        Guid notebookId, ReadOnlyMemory<float> query, int k, CancellationToken ct);
}
