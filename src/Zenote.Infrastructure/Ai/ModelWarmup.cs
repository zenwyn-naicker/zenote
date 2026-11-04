using Microsoft.Extensions.AI;
using Zenote.Core.Abstractions;

namespace Zenote.Infrastructure.Ai;

public sealed class ModelWarmup(ITextEmbedder embedder, IChatClient chat) : IModelWarmup
{
    public async Task WarmUpAsync(CancellationToken ct)
    {
        try
        {
            await embedder.EmbedQueryAsync("warm up", ct);
            await chat.GetResponseAsync("Hi", new ChatOptions { MaxOutputTokens = 1 }, ct);
        }
        catch (HttpRequestException)
        {
            // Ollama isn't running. The status bar already says so.
        }
    }
}
