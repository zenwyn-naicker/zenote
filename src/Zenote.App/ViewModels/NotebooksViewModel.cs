using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Zenote.Core.Abstractions;
using Zenote.Core.Domain;

namespace Zenote.App.ViewModels;

public sealed partial class NotebooksViewModel(INotebookRepository notebooks, IDocumentLibrary library) : ObservableObject
{
    private const string DefaultTitle = "Untitled notebook";

    public Func<Notebook, Task>? OpenRequested { get; set; }

    public ObservableCollection<NotebookCardViewModel> Cards { get; } = [];

    /// The "create" tile followed by the notebooks, so they flow together in one grid.
    public ObservableCollection<object> Tiles { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial bool IsLoaded { get; set; }

    public bool IsEmpty => IsLoaded && Cards.Count == 0;

    public async Task LoadAsync()
    {
        var summaries = await notebooks.ListAsync(CancellationToken.None);

        Cards.Clear();
        foreach (var summary in summaries)
        {
            Cards.Add(new NotebookCardViewModel(summary, this));
        }

        Tiles.Clear();
        Tiles.Add(new CreateNotebookTile(CreateCommand));
        foreach (var card in Cards)
        {
            Tiles.Add(card);
        }

        IsLoaded = true;
        OnPropertyChanged(nameof(IsEmpty));
    }

    [RelayCommand]
    private async Task CreateAsync()
    {
        var notebook = await notebooks.CreateAsync(DefaultTitle, CancellationToken.None);
        await OpenAsync(notebook);
    }

    internal Task OpenAsync(Notebook notebook) => OpenRequested?.Invoke(notebook) ?? Task.CompletedTask;

    internal async Task RenameAsync(NotebookCardViewModel card, string title)
    {
        await notebooks.RenameAsync(card.Notebook.Id, title, CancellationToken.None);
        await LoadAsync();
    }

    internal async Task DeleteAsync(NotebookCardViewModel card)
    {
        await library.DeleteNotebookAsync(card.Notebook.Id, CancellationToken.None);
        await LoadAsync();
    }
}

public sealed record CreateNotebookTile(System.Windows.Input.ICommand Create);

public sealed partial class NotebookCardViewModel(NotebookSummary summary, NotebooksViewModel parent) : ObservableObject
{
    public Notebook Notebook { get; } = summary.Notebook;
    public string Title { get; } = summary.Notebook.Title;
    public string Summary { get; } = $"{summary.Notebook.UpdatedAt.ToLocalTime():d MMM yyyy} · " +
        (summary.SourceCount == 1 ? "1 source" : $"{summary.SourceCount} sources");
    public string DeletePrompt { get; } = $"Delete \"{summary.Notebook.Title}\" and its sources? This can't be undone.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    public partial bool IsRenaming { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    public partial bool IsConfirmingDelete { get; set; }

    [ObservableProperty]
    public partial string EditTitle { get; set; } = summary.Notebook.Title;

    public bool IsIdle => !IsRenaming && !IsConfirmingDelete;

    [RelayCommand]
    private Task OpenAsync() => parent.OpenAsync(Notebook);

    [RelayCommand]
    private void StartRename() => IsRenaming = true;

    [RelayCommand]
    private void Cancel()
    {
        IsRenaming = false;
        IsConfirmingDelete = false;
        EditTitle = Title;
    }

    [RelayCommand]
    private async Task SaveRenameAsync()
    {
        if (string.IsNullOrWhiteSpace(EditTitle))
        {
            Cancel();
            return;
        }

        await parent.RenameAsync(this, EditTitle.Trim());
    }

    [RelayCommand]
    private void StartDelete() => IsConfirmingDelete = true;

    [RelayCommand]
    private Task ConfirmDeleteAsync() => parent.DeleteAsync(this);
}
