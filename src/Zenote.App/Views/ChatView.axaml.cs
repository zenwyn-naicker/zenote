using Avalonia.Controls;

namespace Zenote.App.Views;

public partial class ChatView : UserControl
{
    public ChatView()
    {
        InitializeComponent();

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
