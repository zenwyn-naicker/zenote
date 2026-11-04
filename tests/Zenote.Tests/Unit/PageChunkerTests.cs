using Microsoft.Extensions.Options;
using Shouldly;
using Zenote.Core.Chunking;
using Zenote.Core.Domain;
using Zenote.Core.Options;

namespace Zenote.Tests.Unit;

public class PageChunkerTests
{
    private static readonly ChunkingOptions Settings = new();

    private static PageChunker Chunker() => new(Options.Create(Settings));

    private static ExtractedDocument Doc(params string[] pages) =>
        new(pages.Select((text, i) => new ExtractedPage(i + 1, text, text.Length > 0)).ToList());

    private static string Sentences(int count, string word = "alpha") =>
        string.Join(" ", Enumerable.Range(1, count).Select(i => $"Sentence number {i} says {word} and carries on for a little while."));

    [Fact]
    public void A_short_page_is_one_chunk()
    {
        var chunks = Chunker().Chunk(Doc("A short page of text."));

        chunks.Single().ShouldBe(new ChunkDraft(1, 0, "A short page of text."));
    }

    [Fact]
    public void Empty_pages_produce_no_chunks()
    {
        Chunker().Chunk(Doc("", "")).ShouldBeEmpty();
    }

    [Fact]
    public void Chunks_never_cross_pages()
    {
        var chunks = Chunker().Chunk(Doc(Sentences(60, "alpha"), Sentences(60, "beta")));

        chunks.Where(c => c.Page == 1).ShouldAllBe(c => !c.Text.Contains("beta"));
        chunks.Where(c => c.Page == 2).ShouldAllBe(c => !c.Text.Contains("alpha"));
    }

    [Fact]
    public void Ordinals_run_through_the_whole_document()
    {
        var chunks = Chunker().Chunk(Doc(Sentences(60), Sentences(60)));

        chunks.Select(c => c.Ordinal).ShouldBe(Enumerable.Range(0, chunks.Count));
    }

    [Fact]
    public void Chunks_stay_near_the_target_size()
    {
        var chunks = Chunker().Chunk(Doc(Sentences(200)));

        chunks.Count.ShouldBeGreaterThan(3);
        chunks.ShouldAllBe(c => c.Text.Length <= Settings.TargetChars + Settings.MinChars);
    }

    [Fact]
    public void Neighbouring_chunks_overlap_on_a_sentence_start()
    {
        var chunks = Chunker().Chunk(Doc(Sentences(200)));

        for (var i = 1; i < chunks.Count; i++)
        {
            var opening = chunks[i].Text[..30];

            chunks[i - 1].Text.ShouldContain(opening);
            opening.ShouldStartWith("Sentence number");
        }
    }

    [Fact]
    public void No_text_is_lost()
    {
        var text = Sentences(120);

        var chunks = Chunker().Chunk(Doc(text));

        foreach (var i in Enumerable.Range(1, 120))
        {
            chunks.ShouldContain(c => c.Text.Contains($"Sentence number {i} says"));
        }
    }

    [Fact]
    public void Prefers_paragraph_breaks()
    {
        var first = Sentences(14, "first");
        var second = Sentences(14, "second");

        var chunks = Chunker().Chunk(Doc($"{first}\n\n{second}"));

        chunks[0].Text.ShouldBe(first);
    }

    [Fact]
    public void A_tiny_last_piece_joins_the_chunk_before_it()
    {
        var chunks = Chunker().Chunk(Doc($"{Sentences(24)}\n\nThe end."));

        chunks[^1].Text.ShouldEndWith("The end.");
        chunks[^1].Text.Length.ShouldBeGreaterThan(Settings.MinChars);
    }

    [Fact]
    public void Text_with_no_spaces_is_still_split()
    {
        var chunks = Chunker().Chunk(Doc(new string('x', Settings.TargetChars * 3)));

        chunks.Count.ShouldBeGreaterThan(2);
        chunks.ShouldAllBe(c => c.Text.Length <= Settings.TargetChars + Settings.OverlapChars);
    }
}
