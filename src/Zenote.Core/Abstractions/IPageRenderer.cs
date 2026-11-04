namespace Zenote.Core.Abstractions;

public interface IPageRenderer
{
    /// <param name="pageNumber">1-based page number, matching what citations show.</param>
    Task<byte[]> RenderPngAsync(string filePath, int pageNumber, int widthPx, CancellationToken ct);
}
