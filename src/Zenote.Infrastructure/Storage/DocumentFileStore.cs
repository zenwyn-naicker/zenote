using Zenote.Core.Abstractions;

namespace Zenote.Infrastructure.Storage;

public sealed class DocumentFileStore(AppPaths paths) : IDocumentFileStore
{
    public async Task<string> CopyInAsync(Guid notebookId, Guid documentId, string sourcePath, CancellationToken ct)
    {
        var folder = NotebookFolder(notebookId);
        Directory.CreateDirectory(folder);

        var storedPath = Path.Combine(folder, $"{documentId}.pdf");

        await using var source = File.OpenRead(sourcePath);
        await using var target = File.Create(storedPath);
        await source.CopyToAsync(target, ct);

        return storedPath;
    }

    public void Delete(string storedPath) => File.Delete(storedPath);

    public void DeleteNotebook(Guid notebookId)
    {
        var folder = NotebookFolder(notebookId);

        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private string NotebookFolder(Guid notebookId) => Path.Combine(paths.DocumentsFolder, notebookId.ToString());
}
