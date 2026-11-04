namespace Zenote.Core.Abstractions;

/// Zenote keeps its own copy of each PDF, so sources still work if the original moves.
public interface IDocumentFileStore
{
    /// <returns>The path of the stored copy.</returns>
    Task<string> CopyInAsync(Guid notebookId, Guid documentId, string sourcePath, CancellationToken ct);
    void Delete(string storedPath);
    void DeleteNotebook(Guid notebookId);
}
