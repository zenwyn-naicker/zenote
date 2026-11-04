namespace Zenote.Core.Options;

public sealed class RetrievalOptions
{
    public int TopK { get; set; } = 6;

    /// Below this cosine similarity, nothing retrieved is treated as relevant.
    public double MinSimilarity { get; set; } = 0.55;
}
