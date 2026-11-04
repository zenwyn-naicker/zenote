using Shouldly;
using Zenote.Core.Answering;
using Zenote.Core.Domain;

namespace Zenote.Tests.Unit;

public class CitationParserTests
{
    private static readonly ScoredChunk[] Sources =
    [
        new(new Chunk(10, Guid.NewGuid(), "a.pdf", 3, "First passage."), 0.9),
        new(new Chunk(20, Guid.NewGuid(), "b.pdf", 8, "Second passage."), 0.8),
    ];

    [Fact]
    public void Maps_markers_to_their_sources()
    {
        var (text, citations) = CitationParser.Parse("Fact one [1]. Fact two [2].", Sources);

        text.ShouldBe("Fact one [1]. Fact two [2].");
        citations.Select(c => (c.Marker, c.FileName, c.Page)).ShouldBe([(1, "a.pdf", 3), (2, "b.pdf", 8)]);
    }

    [Fact]
    public void Repeated_markers_give_one_citation()
    {
        var (_, citations) = CitationParser.Parse("One [1]. Again [1].", Sources);

        citations.Count.ShouldBe(1);
    }

    [Theory]
    [InlineData("Fact [9].", "Fact.")]
    [InlineData("Fact [0].", "Fact.")]
    [InlineData("Fact [1][9].", "Fact [1].")]
    public void Removes_markers_with_no_matching_source(string input, string expected)
    {
        CitationParser.Parse(input, Sources).Text.ShouldBe(expected);
    }

    [Fact]
    public void Splits_grouped_markers()
    {
        var (text, citations) = CitationParser.Parse("Fact [1, 2].", Sources);

        text.ShouldBe("Fact [1][2].");
        citations.Count.ShouldBe(2);
    }

    [Fact]
    public void Text_without_markers_has_no_citations()
    {
        var (text, citations) = CitationParser.Parse("No citations here.", Sources);

        text.ShouldBe("No citations here.");
        citations.ShouldBeEmpty();
    }

    [Fact]
    public void Long_passages_are_shortened()
    {
        var chunk = new ScoredChunk(new Chunk(1, Guid.NewGuid(), "a.pdf", 1, new string('x', 2000)), 0.9);

        CitationParser.ToCitation(1, chunk).Snippet.Length.ShouldBe(601);
    }
}
