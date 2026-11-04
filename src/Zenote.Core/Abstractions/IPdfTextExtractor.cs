using Zenote.Core.Domain;

namespace Zenote.Core.Abstractions;

public interface IPdfTextExtractor
{
    /// <exception cref="PdfExtractionException">The file is damaged, encrypted or not a PDF.</exception>
    Task<ExtractedDocument> ExtractAsync(string filePath, CancellationToken ct);
}

public sealed class PdfExtractionException(string message, Exception innerException) : Exception(message, innerException);
