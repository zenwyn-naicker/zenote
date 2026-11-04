using Shouldly;
using Zenote.Core.Abstractions;
using Zenote.Infrastructure.Pdf;
using Zenote.Tests.Support;

namespace Zenote.Tests.Integration;

[Trait("Category", "Integration")]
public sealed class PdfPigTextExtractorTests : IDisposable
{
    private readonly TestWorkspace _workspace = new();
    private readonly PdfPigTextExtractor _extractor = new();

    public void Dispose() => _workspace.Dispose();

    private Task<Core.Domain.ExtractedDocument> ExtractAsync(byte[] pdf) =>
        _extractor.ExtractAsync(_workspace.WriteFile("test.pdf", pdf), TestContext.Current.CancellationToken);

    [Fact]
    public async Task Extracts_each_page_with_its_number()
    {
        var document = await ExtractAsync(TestPdfs.Text(
            "The first page talks about annual leave.",
            "The second page talks about sick leave."));

        document.Pages.Select(p => p.PageNumber).ShouldBe([1, 2]);
        document.Pages[0].Text.ShouldContain("annual leave");
        document.Pages[1].Text.ShouldContain("sick leave");
        document.Pages.ShouldAllBe(p => p.HasText);
    }

    [Fact]
    public async Task Keeps_two_columns_apart()
    {
        var document = await ExtractAsync(TestPdfs.TwoColumns(
            ["left column line one", "left column line two"],
            ["right column line one", "right column line two"]));

        var text = document.Pages[0].Text;

        text.IndexOf("left column line two", StringComparison.Ordinal)
            .ShouldBeLessThan(text.IndexOf("right column line one", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Marks_image_only_pages_as_having_no_text()
    {
        var document = await ExtractAsync(TestPdfs.ImageOnly(pages: 2));

        document.Pages.Count.ShouldBe(2);
        document.Pages.ShouldAllBe(p => !p.HasText);
    }

    [Fact]
    public async Task Reports_a_damaged_file_clearly()
    {
        var ex = await Should.ThrowAsync<PdfExtractionException>(() => ExtractAsync([1, 2, 3, 4]));

        ex.Message.ShouldContain("test.pdf");
    }
}
