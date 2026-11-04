using PDFtoImage;
using Zenote.Core.Abstractions;

namespace Zenote.Infrastructure.Pdf;

public sealed class PdfiumPageRenderer : IPageRenderer
{
    public Task<byte[]> RenderPngAsync(string filePath, int pageNumber, int widthPx, CancellationToken ct) =>
        Task.Run(() =>
        {
            using var pdf = File.OpenRead(filePath);
            using var png = new MemoryStream();
            Conversion.SavePng(png, pdf, pageNumber - 1, options: new RenderOptions { Width = widthPx, WithAspectRatio = true });
            return png.ToArray();
        }, ct);
}
