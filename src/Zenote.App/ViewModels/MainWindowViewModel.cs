using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Zenote.Core.Abstractions;
using Zenote.Core.Domain;

namespace Zenote.App.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly ISystemCheck _systemCheck;
    private readonly NotebooksViewModel _notebooks;
    private readonly Func<Notebook, NotebookViewModel> _createNotebook;
    private readonly IIndexingQueue _indexing;

    public MainWindowViewModel(
        ISystemCheck systemCheck,
        NotebooksViewModel notebooks,
        Func<Notebook, NotebookViewModel> createNotebook,
        IIndexingQueue indexing)
    {
        _systemCheck = systemCheck;
        _indexing = indexing;
        _notebooks = notebooks;
        _createNotebook = createNotebook;
        _notebooks.OpenRequested = OpenNotebookAsync;
        CurrentPage = notebooks;
    }

    [ObservableProperty]
    public partial ObservableObject CurrentPage { get; set; }

    public ObservableCollection<CheckItemViewModel> Checks { get; } = [];

    public async Task StartAsync()
    {
        await _notebooks.LoadAsync();
        await _indexing.ResumeAsync(CancellationToken.None);
        await RefreshAsync(CancellationToken.None);
    }

    [RelayCommand]
    private async Task RefreshAsync(CancellationToken ct)
    {
        var results = await _systemCheck.RunAsync(ct);

        Checks.Clear();
        foreach (var result in results)
        {
            Checks.Add(new CheckItemViewModel(result));
        }
    }

    private async Task OpenNotebookAsync(Notebook notebook)
    {
        var page = _createNotebook(notebook);
        page.BackRequested = ShowNotebooksAsync;
        await page.LoadAsync();
        CurrentPage = page;
    }

    private async Task ShowNotebooksAsync()
    {
        (CurrentPage as IDisposable)?.Dispose();
        await _notebooks.LoadAsync();
        CurrentPage = _notebooks;
    }
}

public sealed class CheckItemViewModel(CheckResult result)
{
    public string Label { get; } = $"{(result.Ok ? "✓" : "✕")} {result.Name}";
    public string? Detail { get; } = result.Detail;
    public bool Ok { get; } = result.Ok;
    public bool Failed { get; } = !result.Ok;
}
