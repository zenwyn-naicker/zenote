using Microsoft.Extensions.Options;
using Zenote.Core.Abstractions;
using Zenote.Core.Domain;
using Zenote.Core.Options;

namespace Zenote.Core.Retrieval;

public sealed class Retriever(
    ITextEmbedder embedder,
    IVectorStore vectors,
    IDocumentRepository documents,
    IOptions<RetrievalOptions> options) : IRetriever
{
    private readonly RetrievalOptions _options = options.Value;

    public async Task<RetrievalResult> RetrieveAsync(Guid notebookId, string query, CancellationToken ct)
    {
        var embedding = await embedder.EmbedQueryAsync(query, ct);
        var matches = await vectors.SearchAsync(notebookId, embedding, _options.TopK, ct);
        var chunks = await documents.GetChunksAsync(matches.Select(match => match.ChunkId).ToList(), ct);

        var similarities = matches.ToDictionary(match => match.ChunkId, match => match.Similarity);
        var scored = chunks.Select(chunk => new ScoredChunk(chunk, similarities[chunk.Id])).ToList();

        return new RetrievalResult(scored, scored.Count > 0 && scored[0].Similarity >= _options.MinSimilarity);
    }
}
