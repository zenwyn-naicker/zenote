using NSubstitute;
using Shouldly;
using Zenote.App.ViewModels;
using Zenote.Core.Abstractions;
using Zenote.Core.Domain;

namespace Zenote.Tests.Unit;

public class NotebooksViewModelTests
{
    private readonly INotebookRepository _notebooks = Substitute.For<INotebookRepository>();
    private readonly IDocumentLibrary _library = Substitute.For<IDocumentLibrary>();

    private NotebooksViewModel CreateViewModel() => new(_notebooks, _library);

    private static NotebookSummary Summary(string title, int sources) =>
        new(new Notebook(Guid.NewGuid(), title, DateTimeOffset.UtcNow), sources);

    [Fact]
    public async Task Create_makes_an_untitled_notebook_and_opens_it()
    {
        var created = new Notebook(Guid.NewGuid(), "Untitled notebook", DateTimeOffset.UtcNow);
        _notebooks.CreateAsync("Untitled notebook", Arg.Any<CancellationToken>()).Returns(created);
        Notebook? opened = null;
        var viewModel = CreateViewModel();
        viewModel.OpenRequested = notebook => { opened = notebook; return Task.CompletedTask; };

        await viewModel.CreateCommand.ExecuteAsync(null);

        opened.ShouldBe(created);
    }

    [Fact]
    public async Task Cards_describe_their_source_counts()
    {
        _notebooks.ListAsync(Arg.Any<CancellationToken>()).Returns([Summary("One", 1), Summary("Many", 3)]);
        var viewModel = CreateViewModel();

        await viewModel.LoadAsync();

        viewModel.Cards[0].Summary.ShouldEndWith("· 1 source");
        viewModel.Cards[1].Summary.ShouldEndWith("· 3 sources");
        viewModel.IsEmpty.ShouldBeFalse();
    }

    [Fact]
    public async Task Delete_needs_confirming_first()
    {
        var summary = Summary("Work", 0);
        _notebooks.ListAsync(Arg.Any<CancellationToken>()).Returns([summary]);
        var viewModel = CreateViewModel();
        await viewModel.LoadAsync();
        var card = viewModel.Cards[0];

        card.StartDeleteCommand.Execute(null);
        await _library.DidNotReceive().DeleteNotebookAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());

        await card.ConfirmDeleteCommand.ExecuteAsync(null);
        await _library.Received(1).DeleteNotebookAsync(summary.Notebook.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Blank_rename_changes_nothing()
    {
        _notebooks.ListAsync(Arg.Any<CancellationToken>()).Returns([Summary("Work", 0)]);
        var viewModel = CreateViewModel();
        await viewModel.LoadAsync();
        var card = viewModel.Cards[0];

        card.StartRenameCommand.Execute(null);
        card.EditTitle = "  ";
        await card.SaveRenameCommand.ExecuteAsync(null);

        await _notebooks.DidNotReceive().RenameAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        card.IsRenaming.ShouldBeFalse();
        card.EditTitle.ShouldBe("Work");
    }
}
