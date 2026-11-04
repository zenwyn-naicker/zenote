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
    private readonly IModelWarmup _warmup;

    public MainWindowViewModel(
        ISystemCheck systemCheck,
        NotebooksViewModel notebooks,
        Func<Notebook, NotebookViewModel> createNotebook,
        IIndexingQueue indexing,
        IModelWarmup warmup,
        SettingsViewModel settings)
    {
        _systemCheck = systemCheck;
        _warmup = warmup;
        Settings = settings;
        _indexing = indexing;
        _notebooks = notebooks;
        _createNotebook = createNotebook;
        _notebooks.OpenRequested = OpenNotebookAsync;
        CurrentPage = notebooks;
    }

    [ObservableProperty]
    public partial ObservableObject CurrentPage { get; set; }

    public SettingsViewModel Settings { get; }

    public ObservableCollection<CheckItemViewModel> Checks { get; } = [];

    /// The status banner only appears when something needs attention.
    public bool HasProblems => Checks.Any(check => check.Failed);

    public string ProblemText => string.Join("  ·  ", Checks.Where(check => check.NeedsAction).Select(check => check.Summary));

    public async Task StartAsync()
    {
        await _notebooks.LoadAsync();
        await _indexing.ResumeAsync(CancellationToken.None);
        await RefreshAsync(CancellationToken.None);
        await _warmup.WarmUpAsync(CancellationToken.None);
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

        OnPropertyChanged(nameof(HasProblems));
        OnPropertyChanged(nameof(ProblemText));
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
    public bool Failed { get; } = !result.Ok;

    /// Models can't be checked while Ollama is down, and that failure already says what to do.
    public bool NeedsAction { get; } = !result.Ok && result.Detail is not null;
    public string Summary { get; } = result.Detail is { } detail ? $"{result.Name}: {detail}" : result.Name;
}
