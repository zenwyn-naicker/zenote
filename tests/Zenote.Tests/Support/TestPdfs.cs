using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace Zenote.Tests.Support;

/// Builds small PDFs in code, so the repo needs no binary fixtures.
public static class TestPdfs
{
    private static readonly byte[] OnePixelPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    public static byte[] Text(params string[] pages)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);

        foreach (var text in pages)
        {
            builder.AddPage(PageSize.A4).AddText(text, 11, new PdfPoint(40, 760), font);
        }

        return builder.Build();
    }

    public static byte[] TwoColumns(string[] left, string[] right)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        var page = builder.AddPage(PageSize.A4);

        WriteColumn(page, left, 40);
        WriteColumn(page, right, 320);

        return builder.Build();

        void WriteColumn(PdfPageBuilder target, string[] lines, double x)
        {
            for (var i = 0; i < lines.Length; i++)
            {
                target.AddText(lines[i], 11, new PdfPoint(x, 760 - (i * 16)), font);
            }
        }
    }

    public static byte[] ImageOnly(int pages)
    {
        var builder = new PdfDocumentBuilder();

        for (var i = 0; i < pages; i++)
        {
            var page = builder.AddPage(PageSize.A4);
            page.AddPng(new MemoryStream(OnePixelPng), new PdfRectangle(40, 40, 500, 760));
        }

        return builder.Build();
    }
}
