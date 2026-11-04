using Avalonia;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Styling;
using Shouldly;

namespace Zenote.Tests.Ui;

[Trait("Category", "Integration")]
public class NotebookCardUiTests
{
    private static void Run(Action test) =>
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(NotebookCardUiTests).Assembly)
            .Dispatch(test, TestContext.Current.CancellationToken).GetAwaiter().GetResult();

    private static Color Brush(string key, ThemeVariant theme)
    {
        Application.Current!.TryGetResource(key, theme, out var brush).ShouldBeTrue();
        return ((ISolidColorBrush)brush!).Color;
    }

    [Fact]
    public void Cards_are_neutral_and_stand_out_from_the_page_in_both_themes() => Run(() =>
    {
        foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            Brush("NotebookCardBrush", theme).ShouldNotBe(Brush("PageBrush", theme));
        }

        Brush("NotebookCardBrush", ThemeVariant.Light).ShouldNotBe(Brush("NotebookCardBrush", ThemeVariant.Dark));
    });
}
