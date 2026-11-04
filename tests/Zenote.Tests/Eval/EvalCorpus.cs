using System.Text.Json;
using Zenote.Core.Options;
using Zenote.Tests.Support;

namespace Zenote.Tests.Eval;

public sealed record EvalQuestion(string Question, string File, int[] Pages, string[] Facts);

public sealed record GoldenSet(EvalQuestion[] Answerable, string[] Unanswerable);

/// Set ZENOTE_EVAL_MODEL, ZENOTE_EVAL_DIMENSIONS, ZENOTE_EVAL_DOCUMENT_PREFIX and ZENOTE_EVAL_QUERY_PREFIX
/// to compare embedding models, and ZENOTE_EVAL_CHAT_MODEL to compare chat models.
public static class EvalCorpus
{
    private const string Folder = "Fixtures/eval";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static async Task ImportAsync(TestWorkspace workspace, Guid notebookId, CancellationToken ct)
    {
        foreach (var path in Directory.GetFiles(Folder, "*.txt"))
        {
            var pages = (await File.ReadAllTextAsync(path, ct)).Split("=== page ===");
            var name = Path.ChangeExtension(Path.GetFileName(path), ".pdf");
            await workspace.ImportAndWaitAsync(notebookId, name, TestPdfs.Paragraphs(pages));
        }
    }

    public static async Task<GoldenSet> LoadQuestionsAsync(CancellationToken ct) =>
        JsonSerializer.Deserialize<GoldenSet>(await File.ReadAllTextAsync(Path.Combine(Folder, "eval.json"), ct), JsonOptions)!;

    public static ModelOptions ModelsFromEnvironment()
    {
        var models = new ModelOptions();

        models.ChatModel = Environment.GetEnvironmentVariable("ZENOTE_EVAL_CHAT_MODEL") ?? models.ChatModel;
        models.EmbeddingModel = Environment.GetEnvironmentVariable("ZENOTE_EVAL_MODEL") ?? models.EmbeddingModel;
        models.DocumentPrefix = Environment.GetEnvironmentVariable("ZENOTE_EVAL_DOCUMENT_PREFIX") ?? models.DocumentPrefix;
        models.QueryPrefix = Environment.GetEnvironmentVariable("ZENOTE_EVAL_QUERY_PREFIX") ?? models.QueryPrefix;

        if (int.TryParse(Environment.GetEnvironmentVariable("ZENOTE_EVAL_DIMENSIONS"), out var dimensions))
        {
            models.EmbeddingDimensions = dimensions;
        }

        return models;
    }
}
