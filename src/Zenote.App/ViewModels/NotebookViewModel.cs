using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Zenote.App.Services;
using Zenote.Core.Abstractions;
using Zenote.Core.Domain;

namespace Zenote.App.ViewModels;

public sealed partial class NotebookViewModel : ObservableObject, IDisposable
{
    private readonly Notebook _notebook;
    private readonly IDocumentRepository _documents;
    private readonly IDocumentLibrary _library;
    private readonly IFilePicker _filePicker;
    private readonly IIndexingQueue _indexing;
    private readonly INotebookRepository _notebooks;
    private readonly SynchronizationContext? _uiContext = SynchronizationContext.Current;

    public NotebookViewModel(
        Notebook notebook,
        IDocumentRepository documents,
        IDocumentLibrary library,
        IFilePicker filePicker,
        IIndexingQueue indexing,
        IRagAnswerService answers,
        IPageRenderer pageRenderer,
        IConversationRepository conversations,
        INotebookRepository notebooks)
    {
        _notebook = notebook;
        _documents = documents;
        _library = library;
        _filePicker = filePicker;
        _indexing = indexing;
        _notebooks = notebooks;
        _savedTitle = notebook.Title;
        Title = notebook.Title;
        Chat = new ChatViewModel(notebook.Id, answers, conversations)
        {
            OpenCitation = OpenCitationAsync,
            NotebookTitle = notebook.Title,
        };
        Sources.CollectionChanged += (_, _) =>
        {
            Chat.SourceCount = Sources.Count;
            OnPropertyChanged(nameof(HasNoSources));
        };
        Panel = new SourcePanelViewModel(pageRenderer);
        Panel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SourcePanelViewModel.IsOpen))
            {
                OnPropertyChanged(nameof(ShowSources));

                if (!Panel.IsOpen)
                {
                    SelectedSource = null;
                }
            }
        };
        _indexing.ProgressChanged += OnIndexingProgress;
    }

    private string _savedTitle;

    public Func<Task>? BackRequested { get; set; }

    [ObservableProperty]
    public partial string Title { get; set; }

    public ChatViewModel Chat { get; }

    public SourcePanelViewModel Panel { get; }

    public ObservableCollection<SourceItemViewModel> Sources { get; } = [];

    [ObservableProperty]
    public partial SourceItemViewModel? SelectedSource { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    public partial string? Message { get; set; }

    [ObservableProperty]
    public partial bool IsImporting { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSources))]
    public partial bool IsNarrow { get; set; }

    public bool HasMessage => Message is not null;

    /// In a narrow window the sources make way for the page viewer, so the chat keeps its room.
    public bool ShowSources => !(IsNarrow && Panel.IsOpen);

    public bool HasNoSources => Sources.Count == 0;

    public async Task LoadAsync()
    {
        var items = await _documents.ListAsync(_notebook.Id, CancellationToken.None);

        Sources.Clear();
        foreach (var document in items)
        {
            Sources.Add(new SourceItemViewModel(document, this));
        }

        await Chat.LoadAsync();
    }

    public async Task ImportFilesAsync(IEnumerable<string> paths)
    {
        var problems = new List<string>();
        Message = null;
        IsImporting = true;

        foreach (var path in paths)
        {
            var name = Path.GetFileName(path);

            if (!path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                problems.Add($"{name} isn't a PDF.");
                continue;
            }

            try
            {
                var result = await _library.ImportAsync(_notebook.Id, path, CancellationToken.None);

                if (result.AlreadyImported)
                {
                    problems.Add($"{name} is already in this notebook.");
                    continue;
                }

                Sources.Add(new SourceItemViewModel(result.Document, this));
            }
            catch (PdfExtractionException ex)
            {
                problems.Add($"{ex.Message} It may be damaged or password-protected.");
            }
            catch (FileTooLargeException ex)
            {
                problems.Add(ex.Message);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                problems.Add($"Couldn't open {name}.");
            }
        }

        IsImporting = false;
        Message = problems.Count == 0 ? null : string.Join(" ", problems.Distinct());
    }

    public async Task SaveTitleAsync()
    {
        var title = Title.Trim();

        if (title.Length == 0)
        {
            Title = _savedTitle;
            return;
        }

        if (title != _savedTitle)
        {
            await _notebooks.RenameAsync(_notebook.Id, title, CancellationToken.None);
            _savedTitle = title;
            Chat.NotebookTitle = title;
        }
    }

    public async Task OpenCitationAsync(Citation citation)
    {
        SelectedSource = null;

        if (Sources.FirstOrDefault(source => source.Document.Id == citation.DocumentId) is { } source)
        {
            await Panel.OpenAsync(source.Document, citation.Page);
        }
        else
        {
            Panel.OpenSnippetOnly(citation);
        }
    }

    internal async Task DeleteSourceAsync(SourceItemViewModel source)
    {
        await _library.DeleteDocumentAsync(source.Document, CancellationToken.None);

        Sources.Remove(source);
        if (Panel.IsShowing(source.Document))
        {
            Panel.Close();
        }
    }

    internal Task CancelIndexingAsync(SourceItemViewModel source) => _indexing.CancelAsync(source.Document.Id);

    internal async Task RetryIndexingAsync(SourceItemViewModel source)
    {
        source.Apply(new IndexingProgress(source.Document.Id, DocumentStatus.Queued, 0, 0));
        await _indexing.EnqueueAsync(source.Document, CancellationToken.None);
    }

    public void Dispose() => _indexing.ProgressChanged -= OnIndexingProgress;

    private void OnIndexingProgress(object? sender, IndexingProgress progress)
    {
        if (_uiContext is null)
        {
            Apply(progress);
        }
        else
        {
            _uiContext.Post(_ => Apply(progress), null);
        }
    }

    private void Apply(IndexingProgress progress) =>
        Sources.FirstOrDefault(source => source.Document.Id == progress.DocumentId)?.Apply(progress);

    partial void OnSelectedSourceChanged(SourceItemViewModel? value)
    {
        if (value is not null)
        {
            _ = Panel.OpenAsync(value.Document, page: 1);
        }
    }

    [RelayCommand]
    private async Task AddPdfsAsync() => await ImportFilesAsync(await _filePicker.PickPdfsAsync());

    [RelayCommand]
    private async Task BackAsync()
    {
        await SaveTitleAsync();

        if (BackRequested is { } back)
        {
            await back();
        }
    }
}

public sealed partial class SourceItemViewModel(Document document, NotebookViewModel parent) : ObservableObject
{
    private readonly string _pageSummary = document.PageCount == 1 ? "1 page" : $"{document.PageCount} pages";

    public Document Document { get; } = document;
    public string FileName { get; } = document.FileName;
    public bool LooksScanned { get; } = document.LooksScanned;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary), nameof(IsIndexing), nameof(CanRetry))]
    public partial DocumentStatus Status { get; set; } = document.Status;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary), nameof(ProgressPercent))]
    public partial int Completed { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary), nameof(ProgressPercent))]
    public partial int Total { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    public partial string? Error { get; set; } = document.Error;

    public bool IsIndexing => Status is DocumentStatus.Queued or DocumentStatus.Extracting or DocumentStatus.Embedding;

    public bool CanRetry => Status is DocumentStatus.Failed or DocumentStatus.Cancelled;

    public double ProgressPercent => Total == 0 ? 0 : 100.0 * Completed / Total;

    public string Summary => Status switch
    {
        DocumentStatus.Queued => "Waiting…",
        DocumentStatus.Extracting => "Reading…",
        DocumentStatus.Embedding => Total == 0 ? "Indexing…" : $"Indexing {Completed} of {Total}",
        DocumentStatus.Failed => Error ?? "Indexing failed.",
        DocumentStatus.Cancelled => "Indexing cancelled.",
        _ => _pageSummary,
    };

    public void Apply(IndexingProgress progress)
    {
        Status = progress.Stage;
        Completed = progress.Completed;
        Total = progress.Total;
        Error = progress.Error;
    }

    [ObservableProperty]
    public partial bool IsConfirmingRemove { get; set; }

    [RelayCommand]
    private void StartRemove() => IsConfirmingRemove = true;

    [RelayCommand]
    private void KeepSource() => IsConfirmingRemove = false;

    [RelayCommand]
    private Task DeleteAsync() => parent.DeleteSourceAsync(this);

    [RelayCommand]
    private Task CancelAsync() => parent.CancelIndexingAsync(this);

    [RelayCommand]
    private Task RetryAsync() => parent.RetryIndexingAsync(this);
}
