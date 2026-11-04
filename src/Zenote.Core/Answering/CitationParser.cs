using System.Text.RegularExpressions;
using Zenote.Core.Domain;

namespace Zenote.Core.Answering;

public static partial class CitationParser
{
    private const int MaxSnippetChars = 600;

    /// Keeps markers that point at a real source and removes the rest,
    /// because small models sometimes invent source numbers.
    public static (string Text, IReadOnlyList<Citation> Citations) Parse(string text, IReadOnlyList<ScoredChunk> sources)
    {
        var citations = new Dictionary<int, Citation>();

        var cleaned = Marker().Replace(text, match =>
        {
            var valid = match.Groups["numbers"].Value
                .Split(',', StringSplitOptions.TrimEntries)
                .Select(int.Parse)
                .Where(number => number >= 1 && number <= sources.Count)
                .Distinct()
                .ToList();

            foreach (var number in valid)
            {
                citations.TryAdd(number, ToCitation(number, sources[number - 1]));
            }

            return valid.Count == 0
                ? ""
                : match.Groups["space"].Value + string.Concat(valid.Select(number => $"[{number}]"));
        });

        return (cleaned.Trim(), citations.Values.ToList());
    }

    public static Citation ToCitation(int marker, ScoredChunk source)
    {
        var chunk = source.Chunk;
        var snippet = chunk.Text.Length <= MaxSnippetChars ? chunk.Text : $"{chunk.Text[..MaxSnippetChars]}…";

        return new Citation(marker, chunk.DocumentId, chunk.FileName, chunk.Page, snippet);
    }

    [GeneratedRegex(@"(?<space>\s*)\[(?<numbers>\d{1,3}(?:\s*,\s*\d{1,3})*)\]")]
    private static partial Regex Marker();
}
