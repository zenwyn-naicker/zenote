using Shouldly;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using Zenote.Infrastructure.Pdf;

namespace Zenote.Tests.Integration;

[Trait("Category", "Integration")]
public class PageRendererTests
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47];

    [Fact]
    public async Task Renders_the_requested_page_as_png()
    {
        var path = Path.Combine(Path.GetTempPath(), $"zenote-test-{Guid.NewGuid():N}.pdf");
        await File.WriteAllBytesAsync(path, BuildTwoPagePdf(), TestContext.Current.CancellationToken);

        try
        {
            var png = await new PdfiumPageRenderer().RenderPngAsync(path, 2, 300, TestContext.Current.CancellationToken);

            png.Take(4).ShouldBe(PngSignature);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static byte[] BuildTwoPagePdf()
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        builder.AddPage(PageSize.A4).AddText("Page one", 12, new PdfPoint(25, 700), font);
        builder.AddPage(PageSize.A4).AddText("Page two", 12, new PdfPoint(25, 700), font);
        return builder.Build();
    }
}
