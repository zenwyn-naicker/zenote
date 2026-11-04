using System.Text.RegularExpressions;

namespace Zenote.Infrastructure.Pdf;

public static partial class TextCleanup
{
    private static readonly (string Ligature, string Letters)[] Ligatures =
    [
        ("ﬀ", "ff"), ("ﬁ", "fi"), ("ﬂ", "fl"), ("ﬃ", "ffi"), ("ﬄ", "ffl"), ("ﬅ", "st"), ("ﬆ", "st"),
    ];

    public static string Clean(string text)
    {
        foreach (var (ligature, letters) in Ligatures)
        {
            text = text.Replace(ligature, letters);
        }

        text = text.Replace("\r\n", "\n").Replace('\r', '\n');
        text = HorizontalSpace().Replace(text, " ");
        text = SpaceAroundNewline().Replace(text, "\n");
        text = HyphenatedLineBreak().Replace(text, "$1$2");
        text = ExtraBlankLines().Replace(text, "\n\n");

        return text.Trim();
    }

    [GeneratedRegex(@"[ \t ]+")]
    private static partial Regex HorizontalSpace();

    [GeneratedRegex(@" ?\n ?")]
    private static partial Regex SpaceAroundNewline();

    // Only rejoin before a lowercase letter, so "Anglo-\nSaxon" keeps its hyphen.
    [GeneratedRegex(@"(\p{L})-\n(\p{Ll})")]
    private static partial Regex HyphenatedLineBreak();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex ExtraBlankLines();
}
