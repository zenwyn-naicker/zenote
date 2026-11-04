using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Zenote.Core.Abstractions;
using Zenote.Core.Options;

namespace Zenote.Infrastructure.Ai;

public sealed class OllamaTextEmbedder(
    IEmbeddingGenerator<string, Embedding<float>> generator,
    IOptions<ModelOptions> options) : ITextEmbedder
{
    private readonly ModelOptions _options = options.Value;

    public async Task<IReadOnlyList<ReadOnlyMemory<float>>> EmbedDocumentsAsync(IReadOnlyList<string> texts, CancellationToken ct)
    {
        var embeddings = await generator.GenerateAsync(
            texts.Select(text => _options.DocumentPrefix + text), cancellationToken: ct);

        return embeddings.Select(embedding => embedding.Vector).ToList();
    }

    public async Task<ReadOnlyMemory<float>> EmbedQueryAsync(string query, CancellationToken ct)
    {
        var embeddings = await generator.GenerateAsync([_options.QueryPrefix + query], cancellationToken: ct);
        return embeddings[0].Vector;
    }
}
