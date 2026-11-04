using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using Zenote.Core.Abstractions;
using Zenote.Core.Chunking;
using Zenote.Core.Documents;
using Zenote.Core.Domain;
using Zenote.Core.Indexing;
using Zenote.Core.Options;
using Zenote.Infrastructure.Pdf;
using Zenote.Infrastructure.Persistence;
using Zenote.Infrastructure.Storage;
using Zenote.Infrastructure.Vectors;

namespace Zenote.Tests.Support;

/// A real database and document folder in a temp directory.
public sealed class TestWorkspace : IDisposable
{
    public TestWorkspace(ITextEmbedder? embedder = null, ModelOptions? models = null)
    {
        Embedder = embedder ?? new FakeEmbedder();

        Paths = new AppPaths(Path.Combine(Path.GetTempPath(), $"zenote-test-{Guid.NewGuid():N}"));
        Directory.CreateDirectory(Paths.Root);

        Connections = new SqliteConnectionFactory(Paths.DatabasePath);
        new DatabaseInitializer(Connections, Options.Create(models ?? new ModelOptions())).Initialize();

        Notebooks = new NotebookRepository(Connections);
        Documents = new DocumentRepository(Connections);
        Vectors = new SqliteVecVectorStore(Connections);
        Queue = new IndexingQueue(
            new PdfPigTextExtractor(),
            new PageChunker(Options.Create(new ChunkingOptions())),
            Embedder,
            Documents,
            Vectors);
        Library = new DocumentLibrary(new PdfPigTextExtractor(), Documents, Notebooks, new DocumentFileStore(Paths), Queue);
    }

    public AppPaths Paths { get; }
    public SqliteConnectionFactory Connections { get; }
    public NotebookRepository Notebooks { get; }
    public DocumentRepository Documents { get; }
    public SqliteVecVectorStore Vectors { get; }
    public ITextEmbedder Embedder { get; }
    public FakeEmbedder FakeEmbedder => (FakeEmbedder)Embedder;
    public IndexingQueue Queue { get; }
    public DocumentLibrary Library { get; }

    public string WriteFile(string name, byte[] bytes)
    {
        var folder = Path.Combine(Paths.Root, "incoming");
        Directory.CreateDirectory(folder);

        var path = Path.Combine(folder, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    /// Imports a PDF and waits for indexing to reach the given status.
    public async Task<Document> ImportAndWaitAsync(Guid notebookId, string name, byte[] pdf, DocumentStatus status = DocumentStatus.Ready)
    {
        // Asynchronous continuations keep the test off the indexing worker's thread.
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Guid? target = null;

        void OnProgress(object? sender, IndexingProgress progress)
        {
            if (progress.Stage == status && (target is null || target == progress.DocumentId))
            {
                finished.TrySetResult();
            }
        }

        Queue.ProgressChanged += OnProgress;
        var result = await Library.ImportAsync(notebookId, WriteFile(name, pdf), TestContext.Current.CancellationToken);
        target = result.Document.Id;
        await finished.Task.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
        Queue.ProgressChanged -= OnProgress;

        return result.Document;
    }

    public long Count(string table)
    {
        using var connection = Connections.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table}";
        return (long)command.ExecuteScalar()!;
    }

    public void Dispose()
    {
        Queue.Dispose();
        SqliteConnection.ClearAllPools();
        Directory.Delete(Paths.Root, recursive: true);
    }
}
