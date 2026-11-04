using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;
using Zenote.App.Services;
using Zenote.App.ViewModels;
using Zenote.Core.Abstractions;
using Zenote.Core.Domain;

namespace Zenote.Tests.Unit;

public class NotebookViewModelTests
{
    private static readonly Notebook Notebook = new(Guid.NewGuid(), "Work", DateTimeOffset.UtcNow);

    private readonly IDocumentRepository _documents = Substitute.For<IDocumentRepository>();
    private readonly IDocumentLibrary _library = Substitute.For<IDocumentLibrary>();
    private readonly IPageRenderer _renderer = Substitute.For<IPageRenderer>();
    private readonly IIndexingQueue _indexing = Substitute.For<IIndexingQueue>();

    private NotebookViewModel CreateViewModel() =>
        new(Notebook, _documents, _library, Substitute.For<IFilePicker>(), _indexing, Substitute.For<IRagAnswerService>(), _renderer);

    private static Document Doc(string name, int pages = 1, int empty = 0) => new(
        Guid.NewGuid(), Notebook.Id, name, $"/stored/{name}", "hash", pages, empty, DocumentStatus.Queued, null, DateTimeOffset.UtcNow);

    private void GivenImportReturns(Document document, bool alreadyImported = false) =>
        _library.ImportAsync(Notebook.Id, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ImportResult(document, alreadyImported));

    [Fact]
    public async Task Imported_source_appears()
    {
        GivenImportReturns(Doc("a.pdf"));
        var viewModel = CreateViewModel();

        await viewModel.ImportFilesAsync(["/tmp/a.pdf"]);

        viewModel.Sources.Single().FileName.ShouldBe("a.pdf");
        viewModel.HasNoSources.ShouldBeFalse();
        viewModel.IsImporting.ShouldBeFalse();
    }

    [Fact]
    public async Task Skips_files_that_are_not_pdfs()
    {
        var viewModel = CreateViewModel();

        await viewModel.ImportFilesAsync(["/tmp/notes.txt"]);

        viewModel.Sources.ShouldBeEmpty();
        viewModel.Message.ShouldBe("notes.txt isn't a PDF.");
    }

    [Fact]
    public async Task Says_when_a_file_is_already_in_the_notebook()
    {
        GivenImportReturns(Doc("a.pdf"), alreadyImported: true);
        var viewModel = CreateViewModel();

        await viewModel.ImportFilesAsync(["/tmp/a.pdf"]);

        viewModel.Sources.ShouldBeEmpty();
        viewModel.Message.ShouldBe("a.pdf is already in this notebook.");
    }

    [Fact]
    public async Task One_bad_file_does_not_stop_the_rest()
    {
        _library.ImportAsync(Notebook.Id, "/tmp/bad.pdf", Arg.Any<CancellationToken>())
            .ThrowsAsync(new PdfExtractionException("Couldn't read bad.pdf.", new Exception()));
        _library.ImportAsync(Notebook.Id, "/tmp/good.pdf", Arg.Any<CancellationToken>())
            .Returns(new ImportResult(Doc("good.pdf"), false));
        var viewModel = CreateViewModel();

        await viewModel.ImportFilesAsync(["/tmp/bad.pdf", "/tmp/good.pdf"]);

        viewModel.Sources.Single().FileName.ShouldBe("good.pdf");
        viewModel.Message!.ShouldContain("bad.pdf");
    }

    [Fact]
    public async Task Selecting_a_source_opens_its_first_page()
    {
        _documents.ListAsync(Notebook.Id, Arg.Any<CancellationToken>()).Returns([Doc("a.pdf", pages: 3)]);
        _renderer.RenderPngAsync("/stored/a.pdf", 1, Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([1, 2, 3]);
        var viewModel = CreateViewModel();
        await viewModel.LoadAsync();

        viewModel.SelectedSource = viewModel.Sources[0];

        viewModel.Panel.IsOpen.ShouldBeTrue();
        viewModel.Panel.Title.ShouldBe("a.pdf");
        viewModel.Panel.PageLabel.ShouldBe("Page 1 of 3");
        viewModel.Panel.HasSnippet.ShouldBeFalse();
    }

    [Fact]
    public async Task Closing_the_panel_clears_the_selection()
    {
        _documents.ListAsync(Notebook.Id, Arg.Any<CancellationToken>()).Returns([Doc("a.pdf")]);
        var viewModel = CreateViewModel();
        await viewModel.LoadAsync();
        viewModel.SelectedSource = viewModel.Sources[0];

        viewModel.Panel.CloseCommand.Execute(null);

        viewModel.SelectedSource.ShouldBeNull();
    }

    [Fact]
    public async Task A_citation_opens_its_page_with_the_passage()
    {
        var document = Doc("a.pdf", pages: 9);
        _documents.ListAsync(Notebook.Id, Arg.Any<CancellationToken>()).Returns([document]);
        _renderer.RenderPngAsync("/stored/a.pdf", 7, Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([7]);
        var viewModel = CreateViewModel();
        await viewModel.LoadAsync();

        await viewModel.OpenCitationAsync(new Citation(1, document.Id, "a.pdf", 7, "The passage."));

        viewModel.Panel.Page.ShouldBe(7);
        viewModel.Panel.Snippet.ShouldBe("The passage.");
        viewModel.Panel.PageImage.ShouldBe([(byte)7]);
    }

    [Fact]
    public async Task A_citation_to_a_removed_source_still_shows_its_passage()
    {
        var viewModel = CreateViewModel();
        await viewModel.LoadAsync();

        await viewModel.OpenCitationAsync(new Citation(1, Guid.NewGuid(), "gone.pdf", 2, "The passage."));

        viewModel.Panel.IsOpen.ShouldBeTrue();
        viewModel.Panel.Snippet.ShouldBe("The passage.");
        viewModel.Panel.HasDocument.ShouldBeFalse();
        viewModel.Panel.Problem!.ShouldContain("no longer");
    }

    [Fact]
    public async Task Source_shows_indexing_progress_then_its_page_count()
    {
        var document = Doc("a.pdf", pages: 3);
        _documents.ListAsync(Notebook.Id, Arg.Any<CancellationToken>()).Returns([document]);
        var viewModel = CreateViewModel();
        await viewModel.LoadAsync();
        var source = viewModel.Sources[0];

        _indexing.ProgressChanged += Raise.Event<EventHandler<IndexingProgress>>(
            _indexing, new IndexingProgress(document.Id, DocumentStatus.Embedding, 4, 16));

        source.Summary.ShouldBe("Indexing 4 of 16");
        source.ProgressPercent.ShouldBe(25);
        source.IsIndexing.ShouldBeTrue();

        _indexing.ProgressChanged += Raise.Event<EventHandler<IndexingProgress>>(
            _indexing, new IndexingProgress(document.Id, DocumentStatus.Ready, 0, 0));

        source.Summary.ShouldBe("3 pages");
        source.IsIndexing.ShouldBeFalse();
    }

    [Fact]
    public async Task Failed_source_shows_the_reason_and_can_retry()
    {
        var document = Doc("a.pdf");
        _documents.ListAsync(Notebook.Id, Arg.Any<CancellationToken>()).Returns([document]);
        var viewModel = CreateViewModel();
        await viewModel.LoadAsync();
        var source = viewModel.Sources[0];

        _indexing.ProgressChanged += Raise.Event<EventHandler<IndexingProgress>>(
            _indexing, new IndexingProgress(document.Id, DocumentStatus.Failed, 0, 0, "Couldn't reach Ollama."));

        source.Summary.ShouldBe("Couldn't reach Ollama.");
        source.CanRetry.ShouldBeTrue();

        await source.RetryCommand.ExecuteAsync(null);

        await _indexing.Received(1).EnqueueAsync(document, Arg.Any<CancellationToken>());
        source.Status.ShouldBe(DocumentStatus.Queued);
    }

    [Fact]
    public void Scanned_badge_follows_the_document()
    {
        var viewModel = CreateViewModel();

        new SourceItemViewModel(Doc("scan.pdf", pages: 4, empty: 3), viewModel).LooksScanned.ShouldBeTrue();
        new SourceItemViewModel(Doc("mostly-text.pdf", pages: 4, empty: 1), viewModel).LooksScanned.ShouldBeFalse();
    }
}
