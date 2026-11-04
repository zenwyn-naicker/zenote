using Microsoft.Data.Sqlite;
using Zenote.Core.Documents;
using Zenote.Infrastructure.Pdf;
using Zenote.Infrastructure.Persistence;
using Zenote.Infrastructure.Storage;

namespace Zenote.Tests.Support;

/// A real database and document folder in a temp directory.
public sealed class TestWorkspace : IDisposable
{
    public TestWorkspace()
    {
        Paths = new AppPaths(Path.Combine(Path.GetTempPath(), $"zenote-test-{Guid.NewGuid():N}"));
        Directory.CreateDirectory(Paths.Root);

        Connections = new SqliteConnectionFactory(Paths.DatabasePath);
        new DatabaseInitializer(Connections).Initialize();

        Notebooks = new NotebookRepository(Connections);
        Documents = new DocumentRepository(Connections);
        Library = new DocumentLibrary(new PdfPigTextExtractor(), Documents, Notebooks, new DocumentFileStore(Paths));
    }

    public AppPaths Paths { get; }
    public SqliteConnectionFactory Connections { get; }
    public NotebookRepository Notebooks { get; }
    public DocumentRepository Documents { get; }
    public DocumentLibrary Library { get; }

    public string WriteFile(string name, byte[] bytes)
    {
        var folder = Path.Combine(Paths.Root, "incoming");
        Directory.CreateDirectory(folder);

        var path = Path.Combine(folder, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(Paths.Root, recursive: true);
    }
}
