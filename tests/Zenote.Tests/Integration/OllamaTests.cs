using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OllamaSharp;
using Shouldly;
using Zenote.Core.Options;
using Zenote.Infrastructure.Ai;

namespace Zenote.Tests.Integration;

/// Live model tests. They skip themselves when Ollama isn't running.
[Trait("Category", "RequiresOllama")]
public class OllamaTests
{
    private static readonly ModelOptions Models = new();

    [Fact]
    public async Task Embedding_generator_returns_768_dimensions()
    {
        await SkipIfOllamaIsDown();
        IEmbeddingGenerator<string, Embedding<float>> generator = new OllamaApiClient(new Uri(Models.OllamaUrl), Models.EmbeddingModel);

        var embeddings = await generator.GenerateAsync(["search_query: vacation days"], cancellationToken: TestContext.Current.CancellationToken);

        embeddings[0].Vector.Length.ShouldBe(768);
    }

    [Fact]
    public async Task Chat_client_streams_a_reply()
    {
        await SkipIfOllamaIsDown();
        IChatClient client = new OllamaApiClient(new Uri(Models.OllamaUrl), Models.ChatModel);

        var updates = 0;
        await foreach (var update in client.GetStreamingResponseAsync("Say hello in one word.", cancellationToken: TestContext.Current.CancellationToken))
        {
            updates += string.IsNullOrEmpty(update.Text) ? 0 : 1;
        }

        updates.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Related_text_scores_higher_than_unrelated_text()
    {
        await SkipIfOllamaIsDown();
        var embedder = new OllamaTextEmbedder(new OllamaApiClient(new Uri(Models.OllamaUrl), Models.EmbeddingModel), Options.Create(Models));
        var ct = TestContext.Current.CancellationToken;

        var documents = await embedder.EmbedDocumentsAsync(
            ["Employees receive twenty days of annual vacation each year.", "The boiler must be serviced by a licensed engineer."], ct);
        var query = await embedder.EmbedQueryAsync("How much leave do I get?", ct);

        Similarity(query, documents[0]).ShouldBeGreaterThan(Similarity(query, documents[1]));
    }

    private static double Similarity(ReadOnlyMemory<float> a, ReadOnlyMemory<float> b) =>
        System.Numerics.Tensors.TensorPrimitives.CosineSimilarity(a.Span, b.Span);

    private static async Task SkipIfOllamaIsDown()
    {
        try
        {
            await new OllamaApiClient(new Uri(Models.OllamaUrl)).GetVersionAsync(TestContext.Current.CancellationToken);
        }
        catch (HttpRequestException)
        {
            Assert.Skip("Ollama is not running.");
        }
    }
}
