using Shouldly;
using Zenote.Infrastructure.Pdf;

namespace Zenote.Tests.Unit;

public class TextCleanupTests
{
    [Theory]
    [InlineData("a   b\t c", "a b c")]
    [InlineData("  padded  ", "padded")]
    [InlineData("line one  \n  line two", "line one\nline two")]
    [InlineData("one\n\n\n\n\ntwo", "one\n\ntwo")]
    [InlineData("windows\r\nline", "windows\nline")]
    public void Normalises_whitespace(string input, string expected) =>
        TextCleanup.Clean(input).ShouldBe(expected);

    [Fact]
    public void Keeps_paragraph_breaks() =>
        TextCleanup.Clean("first paragraph\n\nsecond paragraph").ShouldBe("first paragraph\n\nsecond paragraph");

    [Fact]
    public void Rejoins_words_split_across_lines() =>
        TextCleanup.Clean("an exam-\nple here").ShouldBe("an example here");

    [Fact]
    public void Keeps_the_hyphen_before_a_capital_letter() =>
        TextCleanup.Clean("Anglo-\nSaxon").ShouldBe("Anglo-\nSaxon");

    [Fact]
    public void Replaces_ligatures() =>
        TextCleanup.Clean("ﬁnd the oﬃce").ShouldBe("find the office");
}
