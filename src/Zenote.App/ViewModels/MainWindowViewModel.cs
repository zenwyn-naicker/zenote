using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Zenote.App.Services;
using Zenote.Core.Abstractions;
using Zenote.Core.Domain;

namespace Zenote.App.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject
{
    private static readonly TimeSpan OllamaStartupWait = TimeSpan.FromSeconds(3);

    private readonly ISystemCheck _systemCheck;
    private readonly NotebooksViewModel _notebooks;
    private readonly Func<Notebook, NotebookViewModel> _createNotebook;
    private readonly IIndexingQueue _indexing;
    private readonly IModelWarmup _warmup;
    private readonly IAiHealthCheck _aiHealth;
    private readonly IModelInstaller _installer;
    private readonly IExternalLauncher _launcher;
    private IReadOnlyList<string> _missingModels = [];

    public MainWindowViewModel(
        ISystemCheck systemCheck,
        NotebooksViewModel notebooks,
        Func<Notebook, NotebookViewModel> createNotebook,
        IIndexingQueue indexing,
        IModelWarmup warmup,
        SettingsViewModel settings,
        IAiHealthCheck aiHealth,
        IModelInstaller installer,
        IExternalLauncher launcher)
    {
        _aiHealth = aiHealth;
        _installer = installer;
        _launcher = launcher;
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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanDownloadModels), nameof(ProblemText))]
    public partial bool OllamaIsRunning { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanDownloadModels), nameof(ProblemText))]
    public partial bool IsDownloading { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProblemText))]
    public partial string? DownloadStatus { get; set; }

    [ObservableProperty]
    public partial double DownloadPercent { get; set; }

    /// The status banner only appears when something needs attention.
    public bool HasProblems => IsDownloading || Checks.Any(check => check.Failed);

    public bool NeedsOllama => !OllamaIsRunning;

    public bool CanDownloadModels => OllamaIsRunning && _missingModels.Count > 0 && !IsDownloading;

    public string ProblemText
    {
        get
        {
            if (DownloadStatus is not null)
            {
                return DownloadStatus;
            }

            if (!OllamaIsRunning)
            {
                return "Zenote needs Ollama running to read your documents.";
            }

            if (_missingModels.Count > 0)
            {
                return "Zenote needs to download its AI models. It's a one-time download of a few GB.";
            }

            return string.Join("  ·  ", Checks.Where(check => check.NeedsAction).Select(check => check.Summary));
        }
    }

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
        var health = await _aiHealth.CheckAsync(ct);

        _missingModels = health.Reachable ? health.MissingModels : [];
        OllamaIsRunning = health.Reachable;

        Checks.Clear();
        foreach (var result in results)
        {
            Checks.Add(new CheckItemViewModel(result));
        }

        OnPropertyChanged(nameof(HasProblems));
        OnPropertyChanged(nameof(NeedsOllama));
        OnPropertyChanged(nameof(CanDownloadModels));
        OnPropertyChanged(nameof(ProblemText));
    }

    [RelayCommand]
    private async Task DownloadModelsAsync()
    {
        IsDownloading = true;
        OnPropertyChanged(nameof(HasProblems));

        try
        {
            foreach (var model in _missingModels.ToList())
            {
                DownloadStatus = $"Downloading {model}…";
                var progress = new Progress<double>(percent =>
                {
                    DownloadPercent = percent;
                    DownloadStatus = $"Downloading {model}… {percent:F0}%";
                });

                await _installer.InstallAsync(model, progress, CancellationToken.None);
            }

            DownloadStatus = null;
        }
        catch (HttpRequestException)
        {
            DownloadStatus = "The download stopped. Check your internet connection, then try again.";
        }
        finally
        {
            IsDownloading = false;
        }

        var failed = DownloadStatus is not null;
        await RefreshAsync(CancellationToken.None);

        if (!failed)
        {
            // Settings may already be open, showing the empty list from before the download.
            await Settings.ModelPicker.LoadAsync();
            await _warmup.WarmUpAsync(CancellationToken.None);
        }
    }

    [RelayCommand]
    private async Task OpenOllamaAsync()
    {
        if (!_launcher.OpenApplication("Ollama"))
        {
            _launcher.OpenWebsite("https://ollama.com/download");
            return;
        }

        await Task.Delay(OllamaStartupWait);
        await RefreshAsync(CancellationToken.None);
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
