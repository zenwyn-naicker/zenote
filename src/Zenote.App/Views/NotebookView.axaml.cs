using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Zenote.App.ViewModels;

namespace Zenote.App.Views;

public partial class NotebookView : UserControl
{
    public NotebookView()
    {
        InitializeComponent();
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not NotebookViewModel viewModel || e.DataTransfer.TryGetFiles() is not { } files)
        {
            return;
        }

        var paths = files.Select(file => file.TryGetLocalPath()).OfType<string>().ToList();
        await viewModel.ImportFilesAsync(paths);
    }
}
