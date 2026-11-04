using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Zenote.App.Services;
using Zenote.Core.Abstractions;
using Zenote.Core.Domain;

namespace Zenote.App.ViewModels;

public sealed partial class NotebookViewModel(
    Notebook notebook,
    IDocumentRepository documents,
    IDocumentLibrary library,
    IPdfTextExtractor extractor,
    IFilePicker filePicker) : ObservableObject
{
    private int _pagesRequest;

    public Func<Task>? BackRequested { get; set; }

    public string Title { get; } = notebook.Title;

    public ObservableCollection<SourceItemViewModel> Sources { get; } = [];

    public ObservableCollection<PageItemViewModel> Pages { get; } = [];

    [ObservableProperty]
    public partial SourceItemViewModel? SelectedSource { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    public partial string? Message { get; set; }

    [ObservableProperty]
    public partial bool IsImporting { get; set; }

    public bool HasMessage => Message is not null;

    public bool HasNoSources => Sources.Count == 0;

    public async Task LoadAsync()
    {
        var items = await documents.ListAsync(notebook.Id, CancellationToken.None);

        Sources.Clear();
        foreach (var document in items)
        {
            Sources.Add(new SourceItemViewModel(document, this));
        }

        OnPropertyChanged(nameof(HasNoSources));
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
                var result = await library.ImportAsync(notebook.Id, path, CancellationToken.None);

                if (result.AlreadyImported)
                {
                    problems.Add($"{name} is already in this notebook.");
                    continue;
                }

                var source = new SourceItemViewModel(result.Document, this);
                Sources.Add(source);
                SelectedSource = source;
            }
            catch (PdfExtractionException ex)
            {
                problems.Add($"{ex.Message} It may be damaged or password-protected.");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                problems.Add($"Couldn't open {name}.");
            }
        }

        IsImporting = false;
        Message = problems.Count == 0 ? null : string.Join(" ", problems);
        OnPropertyChanged(nameof(HasNoSources));
    }

    public async Task ShowPagesAsync(SourceItemViewModel? source)
    {
        var request = ++_pagesRequest;
        Pages.Clear();

        if (source is null)
        {
            return;
        }

        try
        {
            var extracted = await extractor.ExtractAsync(source.Document.StoredPath, CancellationToken.None);

            // Another source may have been selected while this one was being read.
            if (request != _pagesRequest)
            {
                return;
            }

            foreach (var page in extracted.Pages)
            {
                Pages.Add(new PageItemViewModel(page));
            }
        }
        catch (PdfExtractionException ex)
        {
            Message = ex.Message;
        }
    }

    internal async Task DeleteSourceAsync(SourceItemViewModel source)
    {
        await library.DeleteDocumentAsync(source.Document, CancellationToken.None);

        Sources.Remove(source);
        if (SelectedSource == source)
        {
            SelectedSource = null;
        }

        OnPropertyChanged(nameof(HasNoSources));
    }

    partial void OnSelectedSourceChanged(SourceItemViewModel? value) => _ = ShowPagesAsync(value);

    [RelayCommand]
    private async Task AddPdfsAsync() => await ImportFilesAsync(await filePicker.PickPdfsAsync());

    [RelayCommand]
    private Task BackAsync() => BackRequested?.Invoke() ?? Task.CompletedTask;
}

public sealed partial class SourceItemViewModel(Document document, NotebookViewModel parent) : ObservableObject
{
    public Document Document { get; } = document;
    public string FileName { get; } = document.FileName;
    public string Summary { get; } = document.PageCount == 1 ? "1 page" : $"{document.PageCount} pages";
    public bool LooksScanned { get; } = document.LooksScanned;

    [RelayCommand]
    private Task DeleteAsync() => parent.DeleteSourceAsync(this);
}

public sealed class PageItemViewModel(ExtractedPage page)
{
    public string Heading { get; } = $"Page {page.PageNumber}";
    public string Text { get; } = page.HasText ? page.Text : "No text found on this page.";
    public bool HasText { get; } = page.HasText;
}
