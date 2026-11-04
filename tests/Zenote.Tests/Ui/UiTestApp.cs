using Avalonia;
using Avalonia.Headless;
using Zenote.App;
using Zenote.Tests.Ui;

[assembly: AvaloniaTestApplication(typeof(UiTestApp))]

namespace Zenote.Tests.Ui;

/// Runs the real views and styles without a screen, so tests can click through them.
public static class UiTestApp
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App.App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
