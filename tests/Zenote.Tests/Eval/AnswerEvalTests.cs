using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Options;
using OllamaSharp;
using Zenote.Core.Answering;
using Zenote.Core.Domain;
using Zenote.Core.Options;
using Zenote.Core.Retrieval;
using Zenote.Infrastructure;
using Zenote.Infrastructure.Ai;
using Zenote.Tests.Support;

namespace Zenote.Tests.Eval;

/// Scores whole answers. Takes a few minutes, so it only runs when ZENOTE_EVAL_ANSWERS is set.
[Trait("Category", "Eval")]
public class AnswerEvalTests
{
    [Fact]
    public async Task Report_answer_quality()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("ZENOTE_EVAL_ANSWERS") is not null, "ZENOTE_EVAL_ANSWERS is not set.");

        var ct = TestContext.Current.CancellationToken;
        var models = EvalCorpus.ModelsFromEnvironment();
        var url = new Uri(models.OllamaUrl);
        var embedder = new OllamaTextEmbedder(new OllamaApiClient(url, models.EmbeddingModel), Options.Create(models));

        using var workspace = new TestWorkspace(embedder, models);
        var notebook = await workspace.Notebooks.CreateAsync("Eval", ct);
        await EvalCorpus.ImportAsync(workspace, notebook.Id, ct);
        var golden = await EvalCorpus.LoadQuestionsAsync(ct);

        var retriever = new Retriever(embedder, workspace.Vectors, workspace.Documents, Options.Create(new RetrievalOptions()));
        var service = new RagAnswerService(retriever, ServiceCollectionExtensions.CreateChatClient(url, models, models.ChatModel));

        await AskAsync(service, notebook.Id, "Warm up.", ct);

        var correct = 0;
        var cited = 0;
        var firstWords = new List<double>();
        var report = new StringBuilder();

        foreach (var item in golden.Answerable)
        {
            var (answer, firstWord) = await AskAsync(service, notebook.Id, item.Question, ct);

            var hasFact = !answer.NotFound && item.Facts.Any(fact => answer.Text.Contains(fact, StringComparison.OrdinalIgnoreCase));
            var citesPage = !answer.NotFound && answer.Citations.Any(c => c.FileName == item.File && item.Pages.Contains(c.Page));

            correct += hasFact ? 1 : 0;
            cited += citesPage ? 1 : 0;

            if (firstWord is { } ms)
            {
                firstWords.Add(ms);
            }

            if (!hasFact || !citesPage)
            {
                report.AppendLine($"  fact={hasFact} cite={citesPage} Q: {item.Question}\n    A: {answer.Text.ReplaceLineEndings(" ")}");
            }
        }

        var declined = 0;
        foreach (var question in golden.Unanswerable)
        {
            var (answer, _) = await AskAsync(service, notebook.Id, question, ct);
            declined += answer.NotFound ? 1 : 0;

            if (!answer.NotFound)
            {
                report.AppendLine($"  ANSWERED Q: {question}\n    A: {answer.Text.ReplaceLineEndings(" ")}");
            }
        }

        var total = golden.Answerable.Length;
        var summary = $"""
            Chat model {models.ChatModel}
            Correct fact: {correct}/{total} ({correct / (double)total:P0})
            Cites the right page: {cited}/{total} ({cited / (double)total:P0})
            Declined unanswerable: {declined}/{golden.Unanswerable.Length}
            Median first word: {Median(firstWords):F0} ms

            {report}
            """;

        await File.WriteAllTextAsync(
            Path.Combine(Path.GetTempPath(), $"zenote-answers-{models.ChatModel.Replace(':', '-')}.txt"), summary, ct);
    }

    private static async Task<(Answer Answer, double? FirstWordMs)> AskAsync(
        RagAnswerService service, Guid notebookId, string question, CancellationToken ct)
    {
        var timer = Stopwatch.StartNew();
        double? firstWord = null;
        Answer? answer = null;

        await foreach (var answerEvent in service.AskAsync(notebookId, question, [], ct))
        {
            if (answerEvent is TokenReceived && firstWord is null)
            {
                firstWord = timer.Elapsed.TotalMilliseconds;
            }
            else if (answerEvent is AnswerCompleted completed)
            {
                answer = completed.Answer;
            }
        }

        return (answer!, firstWord);
    }

    private static double Median(List<double> values) =>
        values.Count == 0 ? 0 : values.Order().ElementAt(values.Count / 2);
}
