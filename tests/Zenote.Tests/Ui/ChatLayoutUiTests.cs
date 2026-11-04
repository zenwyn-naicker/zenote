using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NSubstitute;
using Shouldly;
using Zenote.App.ViewModels;
using Zenote.App.Views;
using Zenote.Core.Abstractions;

namespace Zenote.Tests.Ui;

[Trait("Category", "Integration")]
public class ChatLayoutUiTests
{
    private static void Run(Action test) =>
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(ChatLayoutUiTests).Assembly)
            .Dispatch(test, TestContext.Current.CancellationToken).GetAwaiter().GetResult();

    private static double DisclaimerHeight(double windowWidth)
    {
        var chat = new ChatViewModel(Guid.NewGuid(), Substitute.For<IRagAnswerService>(), Substitute.For<IConversationRepository>());
        var window = new Window { Width = windowWidth, Height = 600, Content = new ChatView { DataContext = chat } };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var height = window.GetVisualDescendants().OfType<TextBlock>()
            .Single(t => t.Text?.StartsWith("Zenote can be inaccurate", StringComparison.Ordinal) == true).Bounds.Height;
        window.Close();
        return height;
    }

    [Fact]
    public void The_disclaimer_wraps_instead_of_being_cut_off_in_a_narrow_chat() => Run(() =>
        DisclaimerHeight(330).ShouldBeGreaterThan(DisclaimerHeight(900)));
}
