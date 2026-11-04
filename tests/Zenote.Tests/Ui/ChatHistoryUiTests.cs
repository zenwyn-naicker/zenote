using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NSubstitute;
using Shouldly;
using Zenote.App.Services;
using Zenote.App.ViewModels;
using Zenote.App.Views;
using Zenote.Core.Abstractions;
using Zenote.Core.Domain;

namespace Zenote.Tests.Ui;

/// Clicks through the real history menu. An exception here would crash the app.
[Trait("Category", "Integration")]
public class ChatHistoryUiTests
{
    private static readonly Guid Notebook = Guid.NewGuid();

    private static Conversation Chat(string title) => new(Guid.NewGuid(), Notebook, title, DateTimeOffset.UtcNow);

    private static void Run(Func<Task> test) =>
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(ChatHistoryUiTests).Assembly)
            .Dispatch(test, TestContext.Current.CancellationToken).GetAwaiter().GetResult();

    private static async Task<(Window Window, ChatViewModel ViewModel, Button History)> OpenHistoryAsync(params string[] titles)
    {
        var conversations = Substitute.For<IConversationRepository>();
        conversations.ListAsync(Notebook, Arg.Any<CancellationToken>()).Returns(titles.Select(Chat).ToList());
        conversations.GetMessagesAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(call =>
        [
            new Message(Guid.NewGuid(), call.Arg<Guid>(), MessageRole.User, "Question", false, [], DateTimeOffset.UtcNow),
            new Message(Guid.NewGuid(), call.Arg<Guid>(), MessageRole.Assistant, "Answer [1].", false,
                [new Citation(1, Guid.NewGuid(), "a.pdf", 2, "Passage")], DateTimeOffset.UtcNow),
        ]);

        var notebook = new NotebookViewModel(
            new Notebook(Notebook, "Work", DateTimeOffset.UtcNow),
            Substitute.For<IDocumentRepository>(),
            Substitute.For<IDocumentLibrary>(),
            Substitute.For<IFilePicker>(),
            Substitute.For<IIndexingQueue>(),
            Substitute.For<IRagAnswerService>(),
            Substitute.For<IPageRenderer>(),
            conversations,
            Substitute.For<INotebookRepository>());
        await notebook.LoadAsync();
        var viewModel = notebook.Chat;

        // The same nesting as the app: bindings hand the chat down through the notebook screen.
        var window = new Window { Width = 1200, Height = 800, Content = new NotebookView { DataContext = notebook } };
        window.Show();
        Settle();

        var history = window.GetVisualDescendants().OfType<Button>().First(b => ToolTip.GetTip(b) as string == "Chat history");
        Click(history);

        return (window, viewModel, history);
    }

    private static void Click(Control control)
    {
        var top = TopLevel.GetTopLevel(control)!;
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), top)!.Value;

        top.MouseMove(point, RawInputModifiers.None);
        top.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
        top.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
        Settle();
    }

    // Lets layout, rendering and posted work finish, as they would between real clicks.
    private static void Settle()
    {
        for (var i = 0; i < 5; i++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
    }

    private static ListBoxItem Item(Window window, string title) =>
        window.GetVisualDescendants().OfType<ListBoxItem>()
            .First(item => (item.DataContext as ConversationItemViewModel)?.Title == title);

    private static Button DeleteButton(ListBoxItem item) =>
        item.GetVisualDescendants().OfType<Button>().First(b => ToolTip.GetTip(b) as string == "Delete chat");

    [Fact]
    public void The_menu_stays_open_until_a_chat_is_picked() => Run(async () =>
    {
        var (window, _, history) = await OpenHistoryAsync("First", "Second");

        ((PopupFlyoutBase)history.Flyout!).IsOpen.ShouldBeTrue();
        window.Close();
    });

    [Fact]
    public void Deleting_the_open_chat_does_not_crash() => Run(async () =>
    {
        var (window, viewModel, _) = await OpenHistoryAsync("First", "Second");

        Click(DeleteButton(Item(window, "First")));

        viewModel.Conversations.Select(c => c.Title).ShouldBe(["Second"]);
        viewModel.SelectedConversation.ShouldBeNull();
        window.Close();
    });

    [Fact]
    public void Deleting_another_chat_keeps_the_open_one() => Run(async () =>
    {
        var (window, viewModel, _) = await OpenHistoryAsync("First", "Second");

        Click(DeleteButton(Item(window, "Second")));

        viewModel.Conversations.Select(c => c.Title).ShouldBe(["First"]);
        viewModel.SelectedConversation!.Title.ShouldBe("First");
        window.Close();
    });

    [Fact]
    public void Deleting_the_last_chat_does_not_crash() => Run(async () =>
    {
        var (window, viewModel, _) = await OpenHistoryAsync("Only");

        Click(DeleteButton(Item(window, "Only")));

        viewModel.Conversations.ShouldBeEmpty();
        window.Close();
    });

    [Fact]
    public void Picking_a_chat_opens_it_and_closes_the_menu() => Run(async () =>
    {
        var (window, viewModel, history) = await OpenHistoryAsync("First", "Second");

        Click(Item(window, "Second"));

        viewModel.SelectedConversation!.Title.ShouldBe("Second");
        ((PopupFlyoutBase)history.Flyout!).IsOpen.ShouldBeFalse();
        window.Close();
    });
}
