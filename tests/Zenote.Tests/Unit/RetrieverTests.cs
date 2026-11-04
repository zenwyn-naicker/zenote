using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Zenote.Core.Abstractions;
using Zenote.Core.Domain;
using Zenote.Core.Options;
using Zenote.Core.Retrieval;

namespace Zenote.Tests.Unit;

public class RetrieverTests
{
    private static readonly Guid Notebook = Guid.NewGuid();

    private readonly ITextEmbedder _embedder = Substitute.For<ITextEmbedder>();
    private readonly IVectorStore _vectors = Substitute.For<IVectorStore>();
    private readonly IDocumentRepository _documents = Substitute.For<IDocumentRepository>();
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    private Retriever CreateRetriever() =>
        new(_embedder, _vectors, _documents, Options.Create(new RetrievalOptions { TopK = 2, MinSimilarity = 0.6 }));

    private void GivenMatches(params (long Id, double Similarity)[] matches)
    {
        _vectors.SearchAsync(Notebook, Arg.Any<ReadOnlyMemory<float>>(), 2, Arg.Any<CancellationToken>()).Returns(matches);
        _documents.GetChunksAsync(Arg.Any<IReadOnlyList<long>>(), Arg.Any<CancellationToken>()).Returns(
            matches.Select(m => new Chunk(m.Id, Guid.NewGuid(), "a.pdf", 1, $"chunk {m.Id}")).ToList());
    }

    [Fact]
    public async Task Keeps_matches_in_similarity_order_with_their_scores()
    {
        GivenMatches((7, 0.9), (3, 0.7));

        var result = await CreateRetriever().RetrieveAsync(Notebook, "question", _ct);

        result.Relevant.ShouldBeTrue();
        result.Chunks.Select(c => (c.Chunk.Id, c.Similarity)).ShouldBe([(7L, 0.9), (3L, 0.7)]);
    }

    [Fact]
    public async Task Weak_matches_are_returned_but_marked_not_relevant()
    {
        GivenMatches((7, 0.5));

        var result = await CreateRetriever().RetrieveAsync(Notebook, "question", _ct);

        result.Relevant.ShouldBeFalse();
        result.Chunks.Count.ShouldBe(1);
    }

    [Fact]
    public async Task An_empty_notebook_is_not_relevant()
    {
        GivenMatches();

        var result = await CreateRetriever().RetrieveAsync(Notebook, "question", _ct);

        result.Relevant.ShouldBeFalse();
    }
}
