using OllamaSharp;
using Zenote.Core.Abstractions;

namespace Zenote.Infrastructure.Ai;

public sealed class OllamaModelInstaller(IOllamaApiClient client) : IModelInstaller
{
    public async Task InstallAsync(string model, IProgress<double> progress, CancellationToken ct)
    {
        // A model downloads as several pieces, each counting from zero, so progress is added up across
        // them. A new piece can briefly lower the overall figure, so it's only ever allowed to rise.
        var pieces = new Dictionary<string, (long Completed, long Total)>();
        var shown = 0.0;

        await foreach (var update in client.PullModelAsync(model, ct))
        {
            if (update is not { Total: > 0, Digest: not null })
            {
                continue;
            }

            pieces[update.Digest] = (update.Completed, update.Total);

            var overall = 100.0 * pieces.Values.Sum(piece => piece.Completed) / pieces.Values.Sum(piece => piece.Total);

            if (overall > shown)
            {
                shown = overall;
                progress.Report(shown);
            }
        }
    }
}
