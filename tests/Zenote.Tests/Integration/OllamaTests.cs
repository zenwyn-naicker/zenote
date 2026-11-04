using Microsoft.Extensions.AI;
using OllamaSharp;
using Shouldly;
using Zenote.Core.Options;

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
