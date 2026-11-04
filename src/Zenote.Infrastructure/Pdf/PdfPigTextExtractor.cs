using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using Zenote.Core.Abstractions;
using Zenote.Core.Domain;

namespace Zenote.Infrastructure.Pdf;

public sealed class PdfPigTextExtractor : IPdfTextExtractor
{
    // A page with less text than this is treated as an image, such as a scan.
    private const int MinCharactersForText = 20;

    public Task<ExtractedDocument> ExtractAsync(string filePath, CancellationToken ct) =>
        Task.Run(() => Extract(filePath, ct), ct);

    private static ExtractedDocument Extract(string filePath, CancellationToken ct)
    {
        try
        {
            using var document = PdfDocument.Open(filePath);

            var pages = new List<ExtractedPage>(document.NumberOfPages);
            foreach (var page in document.GetPages())
            {
                ct.ThrowIfCancellationRequested();

                var text = TextCleanup.Clean(ContentOrderTextExtractor.GetText(page));
                var hasText = text.Count(c => !char.IsWhiteSpace(c)) >= MinCharactersForText;
                pages.Add(new ExtractedPage(page.Number, text, hasText));
            }

            return new ExtractedDocument(pages);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new PdfExtractionException($"Couldn't read {Path.GetFileName(filePath)}.", ex);
        }
    }
}
