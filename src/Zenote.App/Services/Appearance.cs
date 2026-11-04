using Avalonia;
using Avalonia.Styling;

namespace Zenote.App.Services;

public interface IAppearance
{
    /// <param name="appearance">System, Light or Dark.</param>
    void Apply(string appearance);
}

public sealed class AvaloniaAppearance : IAppearance
{
    public void Apply(string appearance)
    {
        if (Application.Current is { } app)
        {
            app.RequestedThemeVariant = appearance switch
            {
                "Light" => ThemeVariant.Light,
                "Dark" => ThemeVariant.Dark,
                _ => ThemeVariant.Default,
            };
        }
    }
}
