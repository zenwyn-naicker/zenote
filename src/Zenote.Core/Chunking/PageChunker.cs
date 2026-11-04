using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Zenote.Core.Abstractions;
using Zenote.Core.Domain;
using Zenote.Core.Options;

namespace Zenote.Core.Chunking;

/// Splits each page on its biggest natural breaks. Chunks never cross pages,
/// so every citation points to exactly one page.
public sealed partial class PageChunker(IOptions<ChunkingOptions> options) : IChunker
{
    private readonly ChunkingOptions _options = options.Value;

    public IReadOnlyList<ChunkDraft> Chunk(ExtractedDocument document)
    {
        var chunks = new List<ChunkDraft>();

        foreach (var page in document.Pages.Where(p => p.HasText))
        {
            foreach (var text in ChunkPage(page.Text))
            {
                chunks.Add(new ChunkDraft(page.PageNumber, chunks.Count, text));
            }
        }

        return chunks;
    }

    private List<string> ChunkPage(string text)
    {
        var chunks = new List<string>();
        var overlap = "";
        var fresh = "";

        foreach (var piece in Split(text, level: 0))
        {
            if (fresh.Length > 0 && overlap.Length + fresh.Length + piece.Length > _options.TargetChars)
            {
                chunks.Add((overlap + fresh).Trim());
                overlap = Overlap(overlap + fresh);
                fresh = "";
            }

            fresh += piece;
        }

        if (string.IsNullOrWhiteSpace(fresh))
        {
            return chunks;
        }

        // A tiny last piece carries little meaning alone, so it joins the chunk before it.
        if (chunks.Count > 0 && fresh.Trim().Length < _options.MinChars)
        {
            chunks[^1] = $"{chunks[^1]} {fresh.Trim()}";
        }
        else
        {
            chunks.Add((overlap + fresh).Trim());
        }

        return chunks;
    }

    private IEnumerable<string> Split(string text, int level)
    {
        if (text.Length <= _options.TargetChars)
        {
            return [text];
        }

        var parts = level switch
        {
            0 => ParagraphBreak().Split(text),
            1 => LineBreak().Split(text),
            2 => SentenceBreak().Split(text),
            3 => WordBreak().Split(text),
            _ => text.Chunk(_options.TargetChars).Select(chars => new string(chars)).ToArray(),
        };

        return level > 3 ? parts : parts.SelectMany(part => Split(part, level + 1));
    }

    private string Overlap(string chunk)
    {
        if (chunk.Length <= _options.OverlapChars)
        {
            return chunk;
        }

        var tail = chunk[^_options.OverlapChars..];

        var sentence = SentenceBreak().Match(tail);
        if (sentence.Success)
        {
            return tail[sentence.Index..].TrimStart();
        }

        var word = tail.IndexOf(' ');
        return word < 0 ? tail : tail[(word + 1)..];
    }

    // Lookbehinds keep each separator on the end of the piece before it, so pieces join back up exactly.
    [GeneratedRegex(@"(?<=\n\n)")]
    private static partial Regex ParagraphBreak();

    [GeneratedRegex(@"(?<=\n)")]
    private static partial Regex LineBreak();

    [GeneratedRegex(@"(?<=[.!?]\s)")]
    private static partial Regex SentenceBreak();

    [GeneratedRegex(@"(?<=\s)")]
    private static partial Regex WordBreak();
}
