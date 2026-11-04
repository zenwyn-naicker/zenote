using OllamaSharp;
using Zenote.Core.Abstractions;

namespace Zenote.Infrastructure.Ai;

public sealed class OllamaModelInstaller(IOllamaApiClient client) : IModelInstaller
{
    public async Task InstallAsync(string model, IProgress<double> progress, CancellationToken ct)
    {
        await foreach (var update in client.PullModelAsync(model, ct))
        {
            if (update is { Total: > 0 })
            {
                progress.Report(update.Percent);
            }
        }
    }
}
