using Microsoft.Extensions.Options;
using Shouldly;
using Zenote.Core.Domain;
using Zenote.Core.Options;
using Zenote.Infrastructure.Persistence;
using Zenote.Tests.Support;

namespace Zenote.Tests.Integration;

[Trait("Category", "Integration")]
public sealed class IndexingTests : IDisposable
{
    private readonly TestWorkspace _workspace = new();
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;
    private readonly byte[] _pdf = TestPdfs.Text("Employees receive twenty days of annual leave each year.", "The boiler must be serviced by a licensed engineer.");

    public void Dispose() => _workspace.Dispose();

    private async Task<Guid> NewNotebookAsync(string title = "Work") => (await _workspace.Notebooks.CreateAsync(title, _ct)).Id;

    private async Task<Document> ReloadAsync(Document document) =>
        (await _workspace.Documents.ListAsync(document.NotebookId, _ct)).Single(d => d.Id == document.Id);

    [Fact]
    public async Task Indexing_stores_a_chunk_and_vector_per_page()
    {
        var notebook = await NewNotebookAsync();

        var document = await _workspace.ImportAndWaitAsync(notebook, "a.pdf", _pdf);

        (await ReloadAsync(document)).Status.ShouldBe(DocumentStatus.Ready);
        _workspace.Count("chunks").ShouldBe(2);
        _workspace.Count("vec_chunks").ShouldBe(2);
    }

    [Fact]
    public async Task Search_finds_the_right_page()
    {
        var notebook = await NewNotebookAsync();
        await _workspace.ImportAndWaitAsync(notebook, "a.pdf", _pdf);

        var query = await _workspace.Embedder.EmbedQueryAsync("licensed engineer boiler", _ct);
        var results = await _workspace.Vectors.SearchAsync(notebook, query, 2, _ct);
        var chunks = await _workspace.Documents.GetChunksAsync(results.Select(r => r.ChunkId).ToList(), _ct);

        chunks[0].Page.ShouldBe(2);
        chunks[0].FileName.ShouldBe("a.pdf");
        results[0].Similarity.ShouldBeGreaterThan(results[1].Similarity);
    }

    [Fact]
    public async Task Search_never_returns_another_notebooks_chunks()
    {
        var mine = await NewNotebookAsync("Mine");
        var other = await NewNotebookAsync("Other");
        await _workspace.ImportAndWaitAsync(other, "other.pdf", _pdf);

        var query = await _workspace.Embedder.EmbedQueryAsync("annual leave", _ct);

        (await _workspace.Vectors.SearchAsync(mine, query, 5, _ct)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Failed_indexing_records_the_reason_and_leaves_nothing_behind()
    {
        _workspace.Embedder.FailWith = new HttpRequestException();
        var notebook = await NewNotebookAsync();

        var document = await _workspace.ImportAndWaitAsync(notebook, "a.pdf", _pdf, DocumentStatus.Failed);

        var saved = await ReloadAsync(document);
        saved.Status.ShouldBe(DocumentStatus.Failed);
        saved.Error!.ShouldContain("Ollama");
        _workspace.Count("chunks").ShouldBe(0);
    }

    [Fact]
    public async Task Retry_after_a_failure_succeeds()
    {
        _workspace.Embedder.FailWith = new HttpRequestException();
        var notebook = await NewNotebookAsync();
        var document = await _workspace.ImportAndWaitAsync(notebook, "a.pdf", _pdf, DocumentStatus.Failed);
        _workspace.Embedder.FailWith = null;

        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _workspace.Queue.ProgressChanged += (_, p) => { if (p.Stage == DocumentStatus.Ready) ready.TrySetResult(); };
        await _workspace.Queue.EnqueueAsync(document, _ct);
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(20), _ct);

        _workspace.Count("vec_chunks").ShouldBe(2);
    }

    [Fact]
    public async Task Cancelling_mid_document_clears_its_chunks()
    {
        _workspace.Embedder.Gate = new TaskCompletionSource();
        var notebook = await NewNotebookAsync();
        var result = await _workspace.Library.ImportAsync(notebook, _workspace.WriteFile("a.pdf", _pdf), _ct);
        await _workspace.Embedder.Entered.Task.WaitAsync(TimeSpan.FromSeconds(20), _ct);

        await _workspace.Queue.CancelAsync(result.Document.Id);

        (await ReloadAsync(result.Document)).Status.ShouldBe(DocumentStatus.Cancelled);
        _workspace.Count("chunks").ShouldBe(0);
    }

    [Fact]
    public async Task Deleting_a_source_while_it_indexes_is_safe()
    {
        _workspace.Embedder.Gate = new TaskCompletionSource();
        var notebook = await NewNotebookAsync();
        var result = await _workspace.Library.ImportAsync(notebook, _workspace.WriteFile("a.pdf", _pdf), _ct);
        await _workspace.Embedder.Entered.Task.WaitAsync(TimeSpan.FromSeconds(20), _ct);

        await _workspace.Library.DeleteDocumentAsync(result.Document, _ct);

        _workspace.Count("documents").ShouldBe(0);
        _workspace.Count("chunks").ShouldBe(0);
        _workspace.Count("vec_chunks").ShouldBe(0);
    }

    [Fact]
    public async Task Deleting_a_source_removes_its_vectors()
    {
        var notebook = await NewNotebookAsync();
        var document = await _workspace.ImportAndWaitAsync(notebook, "a.pdf", _pdf);

        await _workspace.Library.DeleteDocumentAsync(document, _ct);

        _workspace.Count("chunks").ShouldBe(0);
        _workspace.Count("vec_chunks").ShouldBe(0);
    }

    [Fact]
    public async Task Deleting_a_notebook_removes_its_vectors_only()
    {
        var keep = await NewNotebookAsync("Keep");
        var remove = await NewNotebookAsync("Remove");
        await _workspace.ImportAndWaitAsync(keep, "keep.pdf", _pdf);
        await _workspace.ImportAndWaitAsync(remove, "remove.pdf", _pdf);

        await _workspace.Library.DeleteNotebookAsync(remove, _ct);

        _workspace.Count("vec_chunks").ShouldBe(2);
    }

    [Fact]
    public async Task Unfinished_documents_are_found_after_a_restart()
    {
        var notebook = await NewNotebookAsync();
        var document = await _workspace.ImportAndWaitAsync(notebook, "a.pdf", _pdf);
        await _workspace.Documents.UpdateStatusAsync(document.Id, DocumentStatus.Embedding, null, _ct);

        (await _workspace.Documents.ListUnfinishedAsync(_ct)).Single().Id.ShouldBe(document.Id);
    }

    [Fact]
    public async Task Changing_the_embedding_model_requeues_everything()
    {
        var notebook = await NewNotebookAsync();
        var document = await _workspace.ImportAndWaitAsync(notebook, "a.pdf", _pdf);

        var other = Options.Create(new ModelOptions { EmbeddingModel = "qwen3-embedding:0.6b", EmbeddingDimensions = 1024 });
        new DatabaseInitializer(_workspace.Connections, other).Initialize();

        (await ReloadAsync(document)).Status.ShouldBe(DocumentStatus.Queued);
        _workspace.Count("chunks").ShouldBe(0);
        _workspace.Count("vec_chunks").ShouldBe(0);
    }

    [Fact]
    public async Task Reopening_with_the_same_model_keeps_the_index()
    {
        var notebook = await NewNotebookAsync();
        await _workspace.ImportAndWaitAsync(notebook, "a.pdf", _pdf);

        new DatabaseInitializer(_workspace.Connections, Options.Create(new ModelOptions())).Initialize();

        _workspace.Count("vec_chunks").ShouldBe(2);
    }
}
