using System.Security.Cryptography;
using Zenote.Core.Abstractions;
using Zenote.Core.Domain;

namespace Zenote.Core.Documents;

public sealed class DocumentLibrary(
    IPdfTextExtractor extractor,
    IDocumentRepository documents,
    INotebookRepository notebooks,
    IDocumentFileStore files,
    IIndexingQueue indexing) : IDocumentLibrary
{
    private const long MaxFileBytes = 500L * 1024 * 1024;

    public async Task<ImportResult> ImportAsync(Guid notebookId, string sourcePath, CancellationToken ct)
    {
        if (new FileInfo(sourcePath).Length > MaxFileBytes)
        {
            throw new FileTooLargeException($"{Path.GetFileName(sourcePath)} is over the 500 MB limit.");
        }

        var hash = await HashFileAsync(sourcePath, ct);

        if (await documents.FindByHashAsync(notebookId, hash, ct) is { } existing)
        {
            return new ImportResult(existing, AlreadyImported: true);
        }

        // Extract first, so a file that can't be read is never copied in.
        var extracted = await extractor.ExtractAsync(sourcePath, ct);

        var id = Guid.NewGuid();
        var storedPath = await files.CopyInAsync(notebookId, id, sourcePath, ct);

        var document = new Document(
            id,
            notebookId,
            Path.GetFileName(sourcePath),
            storedPath,
            hash,
            extracted.Pages.Count,
            extracted.Pages.Count(page => !page.HasText),
            DocumentStatus.Queued,
            Error: null,
            DateTimeOffset.UtcNow);

        try
        {
            await documents.SaveAsync(document, ct);
        }
        catch
        {
            files.Delete(storedPath);
            throw;
        }

        await indexing.EnqueueAsync(document, ct);

        return new ImportResult(document, AlreadyImported: false);
    }

    public async Task DeleteDocumentAsync(Document document, CancellationToken ct)
    {
        await indexing.CancelAsync(document.Id);
        await documents.DeleteAsync(document.Id, ct);
        files.Delete(document.StoredPath);
    }

    public async Task DeleteNotebookAsync(Guid notebookId, CancellationToken ct)
    {
        foreach (var document in await documents.ListAsync(notebookId, ct))
        {
            await indexing.CancelAsync(document.Id);
        }

        await notebooks.DeleteAsync(notebookId, ct);
        files.DeleteNotebook(notebookId);
    }

    private static async Task<string> HashFileAsync(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, ct));
    }
}
