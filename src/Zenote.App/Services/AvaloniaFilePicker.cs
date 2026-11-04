using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

namespace Zenote.App.Services;

public sealed class AvaloniaFilePicker : IFilePicker
{
    private static readonly FilePickerFileType Pdf = new("PDF documents")
    {
        Patterns = ["*.pdf"],
        AppleUniformTypeIdentifiers = ["com.adobe.pdf"],
    };

    public async Task<IReadOnlyList<string>> PickPdfsAsync()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime { MainWindow: { } window })
        {
            return [];
        }

        var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Add PDFs",
            AllowMultiple = true,
            FileTypeFilter = [Pdf],
        });

        return files.Select(file => file.TryGetLocalPath()).OfType<string>().ToList();
    }
}
