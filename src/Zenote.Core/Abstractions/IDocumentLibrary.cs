using Zenote.Core.Domain;

namespace Zenote.Core.Abstractions;

public sealed record ImportResult(Document Document, bool AlreadyImported);

public interface IDocumentLibrary
{
    Task<ImportResult> ImportAsync(Guid notebookId, string sourcePath, CancellationToken ct);
    Task DeleteDocumentAsync(Document document, CancellationToken ct);
    Task DeleteNotebookAsync(Guid notebookId, CancellationToken ct);
}
