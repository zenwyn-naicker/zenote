using System.Diagnostics;

namespace Zenote.App.Services;

public interface IExternalLauncher
{
    /// <returns>False if the application isn't installed.</returns>
    bool OpenApplication(string name);

    void OpenWebsite(string url);
}

public sealed class ExternalLauncher : IExternalLauncher
{
    public bool OpenApplication(string name)
    {
        using var process = Process.Start(new ProcessStartInfo("open") { ArgumentList = { "-a", name }, RedirectStandardError = true });
        process?.WaitForExit();
        return process?.ExitCode == 0;
    }

    public void OpenWebsite(string url) => Process.Start(new ProcessStartInfo("open") { ArgumentList = { url } })?.Dispose();
}
