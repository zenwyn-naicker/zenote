using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Zenote.App.ViewModels;

namespace Zenote.App.Views;

public partial class NotebookView : UserControl
{
    private const double NarrowWidth = 1120;

    public NotebookView()
    {
        InitializeComponent();
        AddHandler(DragDrop.DropEvent, OnDrop);

        SizeChanged += (_, e) =>
        {
            if (DataContext is NotebookViewModel viewModel)
            {
                viewModel.IsNarrow = e.NewSize.Width < NarrowWidth;
            }
        };

        TitleBox.LostFocus += async (_, _) => await SaveTitleAsync();
        TitleBox.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                await SaveTitleAsync();
                Focus();
            }
        };
    }

    private Task SaveTitleAsync() => DataContext is NotebookViewModel viewModel ? viewModel.SaveTitleAsync() : Task.CompletedTask;

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
