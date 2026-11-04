using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Zenote.App.ViewModels;

namespace Zenote.App.Views;

public partial class ChatView : UserControl
{
    private static readonly TimeSpan CopiedTickDuration = TimeSpan.FromSeconds(1.5);

    public ChatView()
    {
        InitializeComponent();

        // Closes the menu when a chat is clicked, but not for its rename and delete buttons. It's posted so
        // that the list finishes updating its selection first: closing during that update throws.
        HistoryList.AddHandler(
            PointerReleasedEvent,
            (_, e) =>
            {
                if ((e.Source as Visual)?.FindAncestorOfType<Button>(includeSelf: true) is null)
                {
                    Dispatcher.UIThread.Post(() => HistoryButton.Flyout?.Hide());
                }
            },
            RoutingStrategies.Bubble,
            handledEventsToo: true);

        // Keeps the newest text in view as an answer streams in.
        Scroller.PropertyChanged += (_, e) =>
        {
            if (e.Property == ScrollViewer.ExtentProperty)
            {
                Scroller.ScrollToEnd();
            }
        };
    }

    private async void OnCopyClicked(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not MessageViewModel message ||
            TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
        {
            return;
        }

        await clipboard.SetTextAsync(message.CopyText);

        message.IsCopied = true;
        await Task.Delay(CopiedTickDuration);
        message.IsCopied = false;
    }
}
