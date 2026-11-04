using System.Text;
using Microsoft.Extensions.Options;
using OllamaSharp;
using Shouldly;
using Zenote.Core.Domain;
using Zenote.Core.Options;
using Zenote.Core.Retrieval;
using Zenote.Infrastructure.Ai;
using Zenote.Tests.Support;

namespace Zenote.Tests.Eval;

/// Measures retrieval against known answers. Needs Ollama, so it skips itself on CI.
/// See EvalCorpus for how to compare embedding models.
[Trait("Category", "Eval")]
public class RetrievalEvalTests
{
    [Fact]
    public async Task Retrieval_meets_its_quality_floors()
    {
        var ct = TestContext.Current.CancellationToken;
        var models = EvalCorpus.ModelsFromEnvironment();
        var client = new OllamaApiClient(new Uri(models.OllamaUrl), models.EmbeddingModel);

        try
        {
            await client.GetVersionAsync(ct);
        }
        catch (HttpRequestException)
        {
            Assert.Skip("Ollama is not running.");
        }

        using var workspace = new TestWorkspace(new OllamaTextEmbedder(client, Options.Create(models)), models);
        var notebook = await workspace.Notebooks.CreateAsync("Eval", ct);

        await EvalCorpus.ImportAsync(workspace, notebook.Id, ct);
        var golden = await EvalCorpus.LoadQuestionsAsync(ct);

        var retriever = new Retriever(workspace.Embedder, workspace.Vectors, workspace.Documents, Options.Create(new RetrievalOptions()));

        var ranks = new List<int?>();
        var answerableScores = new List<double>();
        var report = new StringBuilder();

        foreach (var item in golden.Answerable)
        {
            var result = await retriever.RetrieveAsync(notebook.Id, item.Question, ct);
            var rank = result.Chunks
                .Select((match, index) => (match, index))
                .Where(x => IsExpected(x.match, item))
                .Select(x => (int?)x.index + 1)
                .FirstOrDefault();

            ranks.Add(rank);
            answerableScores.Add(result.Chunks[0].Similarity);

            if (rank != 1)
            {
                report.AppendLine($"  rank {rank?.ToString() ?? "miss"}: {item.Question}");
            }
        }

        var unanswerableScores = new List<double>();
        foreach (var question in golden.Unanswerable)
        {
            var result = await retriever.RetrieveAsync(notebook.Id, question, ct);
            unanswerableScores.Add(result.Chunks[0].Similarity);
        }

        var hit1 = HitRate(ranks, 1);
        var hit3 = HitRate(ranks, 3);
        var hit6 = HitRate(ranks, 6);
        var mrr = ranks.Average(rank => rank is { } r ? 1.0 / r : 0);

        var summary = new StringBuilder();
        summary.AppendLine($"Model {models.EmbeddingModel}: Hit@1 {hit1:P0}, Hit@3 {hit3:P0}, Hit@6 {hit6:P0}, MRR {mrr:F3}");
        summary.AppendLine($"Best-match score: answerable {answerableScores.Min():F3} to {answerableScores.Max():F3}, unanswerable {unanswerableScores.Min():F3} to {unanswerableScores.Max():F3}");
        summary.Append(GateSweep(answerableScores, unanswerableScores));
        summary.Append(report);

        File.WriteAllText(Path.Combine(Path.GetTempPath(), $"zenote-eval-{models.EmbeddingModel.Replace(':', '-')}.txt"), summary.ToString());
        TestContext.Current.SendDiagnosticMessage(summary.ToString());

        hit6.ShouldBeGreaterThanOrEqualTo(0.95);
        hit1.ShouldBeGreaterThanOrEqualTo(0.75);
    }

    private static bool IsExpected(ScoredChunk match, EvalQuestion item) =>
        match.Chunk.FileName == item.File && item.Pages.Contains(match.Chunk.Page);

    private static double HitRate(List<int?> ranks, int k) => ranks.Count(rank => rank <= k) / (double)ranks.Count;

    private static string GateSweep(List<double> answerable, List<double> unanswerable)
    {
        var lines = new StringBuilder("Gate at: answerable kept / unanswerable blocked\n");

        for (var gate = 0.40; gate <= 0.801; gate += 0.05)
        {
            var kept = answerable.Count(score => score >= gate) / (double)answerable.Count;
            var blocked = unanswerable.Count(score => score < gate) / (double)unanswerable.Count;
            lines.AppendLine($"  {gate:F2}: {kept:P0} / {blocked:P0}");
        }

        return lines.ToString();
    }
}
