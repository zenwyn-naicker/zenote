using Microsoft.Extensions.Options;
using Shouldly;
using Zenote.Core.Options;
using Zenote.Infrastructure.Persistence;
using Zenote.Tests.Support;

namespace Zenote.Tests.Integration;

[Trait("Category", "Integration")]
public sealed class NotebookRepositoryTests : IDisposable
{
    private readonly TestWorkspace _workspace = new();
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    public void Dispose() => _workspace.Dispose();

    [Fact]
    public async Task Notebooks_survive_reopening_the_database()
    {
        await _workspace.Notebooks.CreateAsync("Taxes 2026", _ct);

        var connections = new SqliteConnectionFactory(_workspace.Paths.DatabasePath);
        new DatabaseInitializer(connections, Options.Create(new ModelOptions())).Initialize();
        var notebooks = await new NotebookRepository(connections).ListAsync(_ct);

        notebooks.Single().Notebook.Title.ShouldBe("Taxes 2026");
    }

    [Fact]
    public async Task Lists_the_most_recently_updated_first()
    {
        var older = await _workspace.Notebooks.CreateAsync("Older", _ct);
        await _workspace.Notebooks.CreateAsync("Newer", _ct);
        await _workspace.Notebooks.RenameAsync(older.Id, "Renamed", _ct);

        var notebooks = await _workspace.Notebooks.ListAsync(_ct);

        notebooks.Select(n => n.Notebook.Title).ShouldBe(["Renamed", "Newer"]);
    }

    [Fact]
    public async Task Counts_sources_per_notebook()
    {
        var notebook = await _workspace.Notebooks.CreateAsync("Work", _ct);
        await _workspace.Library.ImportAsync(notebook.Id, _workspace.WriteFile("a.pdf", TestPdfs.Text("Some text in a document.")), _ct);

        (await _workspace.Notebooks.ListAsync(_ct)).Single().SourceCount.ShouldBe(1);
    }
}
