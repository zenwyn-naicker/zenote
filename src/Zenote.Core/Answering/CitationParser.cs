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
                .SelectMany(Expand)
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

        return (MarkerSeparator().Replace(cleaned, "][").Trim(), citations.Values.ToList());
    }

    // Small models sometimes write a range, like [1-6].
    private static IEnumerable<int> Expand(string part)
    {
        var ends = part.Split(['-', '–'], StringSplitOptions.TrimEntries);
        var first = int.Parse(ends[0]);
        var last = int.Parse(ends[^1]);

        return last < first ? [first] : Enumerable.Range(first, last - first + 1);
    }

    public static Citation ToCitation(int marker, ScoredChunk source)
    {
        var chunk = source.Chunk;
        var snippet = chunk.Text.Length <= MaxSnippetChars ? chunk.Text : $"{chunk.Text[..MaxSnippetChars]}…";

        return new Citation(marker, chunk.DocumentId, chunk.FileName, chunk.Page, snippet);
    }

    [GeneratedRegex(@"(?<space>\s*)\[(?<numbers>\d{1,3}(?:\s*[,\-–]\s*\d{1,3})*)\]")]
    private static partial Regex Marker();

    [GeneratedRegex(@"\]\s*,\s*\[")]
    private static partial Regex MarkerSeparator();
}
