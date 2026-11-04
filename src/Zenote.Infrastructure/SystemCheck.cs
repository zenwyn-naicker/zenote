using Microsoft.Extensions.Options;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using Zenote.Core.Abstractions;
using Zenote.Core.Options;
using Zenote.Infrastructure.Persistence;

namespace Zenote.Infrastructure;

public sealed class SystemCheck(
    IAiHealthCheck aiHealth,
    IPageRenderer pageRenderer,
    IOptions<ModelOptions> options) : ISystemCheck
{
    public async Task<IReadOnlyList<CheckResult>> RunAsync(CancellationToken ct)
    {
        var ai = await aiHealth.CheckAsync(ct);
        var model = options.Value;

        return
        [
            new CheckResult("Ollama", ai.Reachable, ai.Reachable ? null : "Not running. Open Ollama from Applications."),
            ModelResult(model.ChatModel, ai),
            ModelResult(model.EmbeddingModel, ai),
            CheckOllamaIsLocal(model.OllamaUrl),
            CheckSqliteVec(),
            await CheckPageRenderingAsync(ct),
        ];
    }

    private static CheckResult ModelResult(string name, AiHealth ai)
    {
        var missing = ai.MissingModels.Contains(name);

        // Without Ollama we can't tell whether a model is installed, so don't suggest pulling it.
        return new CheckResult(name, !missing, missing && ai.Reachable ? $"Run: ollama pull {name}" : null);
    }

    // Answers are only private if Ollama runs on this Mac, because it receives your documents' text.
    private static CheckResult CheckOllamaIsLocal(string url)
    {
        var local = Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.IsLoopback;
        return new CheckResult("Privacy", local, local ? null : "Ollama isn't on this Mac, so document text is sent to another computer.");
    }

    private static CheckResult CheckSqliteVec()
    {
        try
        {
            using var connection = new SqliteConnectionFactory(":memory:").Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT vec_version()";
            return new CheckResult("sqlite-vec", true, (string?)command.ExecuteScalar());
        }
        catch (Exception ex)
        {
            return new CheckResult("sqlite-vec", false, ex.Message);
        }
    }

    private async Task<CheckResult> CheckPageRenderingAsync(CancellationToken ct)
    {
        var path = Path.Combine(Path.GetTempPath(), $"zenote-check-{Guid.NewGuid():N}.pdf");

        try
        {
            await File.WriteAllBytesAsync(path, BuildOnePagePdf(), ct);
            var png = await pageRenderer.RenderPngAsync(path, 1, 200, ct);
            return new CheckResult("Page rendering", png.Length > 0);
        }
        catch (Exception ex)
        {
            return new CheckResult("Page rendering", false, ex.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static byte[] BuildOnePagePdf()
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        builder.AddPage(PageSize.A4).AddText("Zenote", 12, new PdfPoint(25, 700), font);
        return builder.Build();
    }
}
