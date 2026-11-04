using Shouldly;
using Zenote.Core.Abstractions;
using Zenote.Tests.Support;

namespace Zenote.Tests.Integration;

[Trait("Category", "Integration")]
public sealed class DocumentLibraryTests : IDisposable
{
    private readonly TestWorkspace _workspace = new();
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;
    private readonly byte[] _pdf = TestPdfs.Text("A page with enough text to count as real text.");

    public void Dispose() => _workspace.Dispose();

    [Fact]
    public async Task Very_large_files_are_refused()
    {
        var notebook = await _workspace.Notebooks.CreateAsync("Work", _ct);
        var path = _workspace.WriteFile("huge.pdf", []);
        await using (var file = File.OpenWrite(path))
        {
            file.SetLength(501L * 1024 * 1024);
        }

        var ex = await Should.ThrowAsync<FileTooLargeException>(() => _workspace.Library.ImportAsync(notebook.Id, path, _ct));

        ex.Message.ShouldContain("500 MB");
    }

    [Fact]
    public void The_data_folder_is_private()
    {
        var paths = new Zenote.Infrastructure.Storage.AppPaths(Path.Combine(_workspace.Paths.Root, "private"));

        paths.EnsurePrivateRoot();

        File.GetUnixFileMode(paths.Root).ShouldBe(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    [Fact]
    public async Task Import_keeps_a_copy_that_outlives_the_original()
    {
        var notebook = await _workspace.Notebooks.CreateAsync("Work", _ct);
        var original = _workspace.WriteFile("handbook.pdf", _pdf);

        var result = await _workspace.Library.ImportAsync(notebook.Id, original, _ct);
        File.Delete(original);

        result.Document.FileName.ShouldBe("handbook.pdf");
        result.Document.PageCount.ShouldBe(1);
        File.Exists(result.Document.StoredPath).ShouldBeTrue();
    }

    [Fact]
    public async Task Importing_the_same_file_twice_keeps_one_source()
    {
        var notebook = await _workspace.Notebooks.CreateAsync("Work", _ct);

        await _workspace.Library.ImportAsync(notebook.Id, _workspace.WriteFile("a.pdf", _pdf), _ct);
        var second = await _workspace.Library.ImportAsync(notebook.Id, _workspace.WriteFile("renamed.pdf", _pdf), _ct);

        second.AlreadyImported.ShouldBeTrue();
        (await _workspace.Documents.ListAsync(notebook.Id, _ct)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task The_same_file_can_sit_in_two_notebooks()
    {
        var first = await _workspace.Notebooks.CreateAsync("One", _ct);
        var second = await _workspace.Notebooks.CreateAsync("Two", _ct);
        var path = _workspace.WriteFile("a.pdf", _pdf);

        await _workspace.Library.ImportAsync(first.Id, path, _ct);
        var result = await _workspace.Library.ImportAsync(second.Id, path, _ct);

        result.AlreadyImported.ShouldBeFalse();
    }

    [Fact]
    public async Task Mostly_empty_documents_look_scanned()
    {
        var notebook = await _workspace.Notebooks.CreateAsync("Scans", _ct);

        var result = await _workspace.Library.ImportAsync(notebook.Id, _workspace.WriteFile("scan.pdf", TestPdfs.ImageOnly(3)), _ct);

        result.Document.PagesWithoutText.ShouldBe(3);
        result.Document.LooksScanned.ShouldBeTrue();
    }

    [Fact]
    public async Task A_damaged_file_is_not_kept()
    {
        var notebook = await _workspace.Notebooks.CreateAsync("Work", _ct);

        await Should.ThrowAsync<PdfExtractionException>(() =>
            _workspace.Library.ImportAsync(notebook.Id, _workspace.WriteFile("bad.pdf", [1, 2, 3]), _ct));

        (await _workspace.Documents.ListAsync(notebook.Id, _ct)).ShouldBeEmpty();
        Directory.Exists(_workspace.Paths.DocumentsFolder).ShouldBeFalse();
    }

    [Fact]
    public async Task Deleting_a_source_removes_its_row_and_file()
    {
        var notebook = await _workspace.Notebooks.CreateAsync("Work", _ct);
        var result = await _workspace.Library.ImportAsync(notebook.Id, _workspace.WriteFile("a.pdf", _pdf), _ct);

        await _workspace.Library.DeleteDocumentAsync(result.Document, _ct);

        (await _workspace.Documents.ListAsync(notebook.Id, _ct)).ShouldBeEmpty();
        File.Exists(result.Document.StoredPath).ShouldBeFalse();
    }

    [Fact]
    public async Task Deleting_a_notebook_removes_its_sources_and_files()
    {
        var notebook = await _workspace.Notebooks.CreateAsync("Work", _ct);
        var result = await _workspace.Library.ImportAsync(notebook.Id, _workspace.WriteFile("a.pdf", _pdf), _ct);

        await _workspace.Library.DeleteNotebookAsync(notebook.Id, _ct);

        (await _workspace.Documents.ListAsync(notebook.Id, _ct)).ShouldBeEmpty();
        File.Exists(result.Document.StoredPath).ShouldBeFalse();
    }
}
