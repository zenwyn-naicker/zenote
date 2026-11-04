using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Zenote.App.Views;

public partial class ChatView : UserControl
{
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
}
