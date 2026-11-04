using Zenote.Core.Abstractions;

namespace Zenote.Tests.Support;

/// A word-hashing embedder: texts sharing words get similar vectors, with no model needed.
public sealed class FakeEmbedder : ITextEmbedder
{
    private const int Dimensions = 768;

    public Exception? FailWith { get; set; }

    /// When set, embedding waits for this before returning, so tests can cancel mid-document.
    public TaskCompletionSource? Gate { get; set; }

    public TaskCompletionSource Entered { get; } = new();

    public async Task<IReadOnlyList<ReadOnlyMemory<float>>> EmbedDocumentsAsync(IReadOnlyList<string> texts, CancellationToken ct)
    {
        Entered.TrySetResult();

        if (Gate is { } gate)
        {
            await gate.Task.WaitAsync(ct);
        }

        if (FailWith is { } failure)
        {
            throw failure;
        }

        return texts.Select(Embed).ToList();
    }

    public Task<ReadOnlyMemory<float>> EmbedQueryAsync(string query, CancellationToken ct) => Task.FromResult(Embed(query));

    private static ReadOnlyMemory<float> Embed(string text)
    {
        var vector = new float[Dimensions];

        foreach (var word in text.ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var hash = word.Aggregate(17, (h, c) => unchecked((h * 31) + c));
            vector[Math.Abs(hash % Dimensions)] += 1;
        }

        vector[0] += 0.01f;
        return vector;
    }
}
