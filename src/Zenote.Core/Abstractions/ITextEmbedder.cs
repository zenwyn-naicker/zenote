namespace Zenote.Core.Abstractions;

/// Makes the query/document split explicit, because some models expect a different prefix for each.
public interface ITextEmbedder
{
    Task<IReadOnlyList<ReadOnlyMemory<float>>> EmbedDocumentsAsync(IReadOnlyList<string> texts, CancellationToken ct);
    Task<ReadOnlyMemory<float>> EmbedQueryAsync(string query, CancellationToken ct);
}
