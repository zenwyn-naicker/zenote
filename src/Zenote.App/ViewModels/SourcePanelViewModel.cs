using System.Diagnostics;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Zenote.Core.Abstractions;
using Zenote.Core.Domain;

namespace Zenote.App.ViewModels;

public sealed partial class SourcePanelViewModel(IPageRenderer renderer) : ObservableObject
{
    // Twice the panel's width, so pages stay sharp on Retina displays.
    private const int RenderWidth = 760;
    private const int CacheSize = 10;

    private readonly Dictionary<(string Path, int Page), byte[]> _cache = [];
    private readonly Queue<(string Path, int Page)> _cacheOrder = new();

    private Document? _document;
    private int _renderRequest;

    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    [ObservableProperty]
    public partial string Title { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSnippet))]
    public partial string? Snippet { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageLabel))]
    [NotifyCanExecuteChangedFor(nameof(PreviousPageCommand), nameof(NextPageCommand))]
    public partial int Page { get; set; }

    [ObservableProperty]
    public partial byte[]? PageImage { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDocument), nameof(PageLabel))]
    public partial int PageCount { get; set; }

    [ObservableProperty]
    public partial string? Problem { get; set; }

    public bool HasSnippet => Snippet is not null;

    public bool IsShowing(Document document) => IsOpen && _document?.Id == document.Id;

    public bool HasDocument => PageCount > 0;

    public string PageLabel => $"Page {Page} of {PageCount}";

    private bool CanGoBack => Page > 1;

    private bool CanGoForward => Page < PageCount;

    public async Task OpenAsync(Document document, int page)
    {
        _document = document;
        Title = document.FileName;
        Snippet = null;
        PageCount = document.PageCount;
        IsOpen = true;

        await ShowPageAsync(page);
    }

    /// For citations whose source has since been removed from the notebook.
    public void OpenSnippetOnly(Citation citation)
    {
        _document = null;
        _renderRequest++;
        Title = citation.FileName;
        Snippet = Reflow(citation.Snippet);
        PageCount = 0;
        PageImage = null;
        Problem = "This source is no longer in the notebook.";
        IsOpen = true;
    }

    [RelayCommand]
    public void Close()
    {
        IsOpen = false;
        _document = null;
        PageImage = null;
    }

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private Task PreviousPageAsync() => ShowPageAsync(Page - 1);

    [RelayCommand(CanExecute = nameof(CanGoForward))]
    private Task NextPageAsync() => ShowPageAsync(Page + 1);

    [RelayCommand]
    private void OpenInPreview()
    {
        if (_document is not null)
        {
            Process.Start(new ProcessStartInfo("open") { ArgumentList = { _document.StoredPath } });
        }
    }

    // PDFs break lines to fit the page. Joining them lets the text wrap to the panel instead.
    private static string? Reflow(string? text) => text is null ? null : SingleLineBreak().Replace(text, " ");

    private async Task ShowPageAsync(int page)
    {
        var document = _document!;
        var request = ++_renderRequest;

        Page = page;
        Problem = null;

        try
        {
            var image = await RenderAsync(document.StoredPath, page);

            // Another page may have been asked for while this one rendered.
            if (request == _renderRequest)
            {
                PageImage = image;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // PDFs can fail in many ways, and one bad page shouldn't take the app down.
            if (request == _renderRequest)
            {
                PageImage = null;
                Problem = "This page couldn't be shown.";
            }
        }
    }

    private async Task<byte[]> RenderAsync(string path, int page)
    {
        if (_cache.TryGetValue((path, page), out var cached))
        {
            return cached;
        }

        var image = await renderer.RenderPngAsync(path, page, RenderWidth, CancellationToken.None);

        _cache[(path, page)] = image;
        _cacheOrder.Enqueue((path, page));

        if (_cacheOrder.Count > CacheSize)
        {
            _cache.Remove(_cacheOrder.Dequeue());
        }

        return image;
    }

    [GeneratedRegex(@"(?<!\n)\n(?!\n)")]
    private static partial Regex SingleLineBreak();
}
