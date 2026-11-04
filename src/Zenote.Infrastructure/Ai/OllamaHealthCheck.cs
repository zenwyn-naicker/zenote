using Microsoft.Extensions.Options;
using OllamaSharp;
using Zenote.Core.Abstractions;
using Zenote.Core.Options;

namespace Zenote.Infrastructure.Ai;

public sealed class OllamaHealthCheck(IOllamaApiClient client, IOptions<ModelOptions> options) : IAiHealthCheck
{
    private readonly ModelOptions _options = options.Value;

    public async Task<AiHealth> CheckAsync(CancellationToken ct)
    {
        string[] required = [_options.ChatModel, _options.EmbeddingModel];

        try
        {
            var installed = (await client.ListLocalModelsAsync(ct)).Select(m => m.Name).ToList();
            return new AiHealth(true, required.Where(name => !IsInstalled(name, installed)).ToList());
        }
        catch (HttpRequestException)
        {
            return new AiHealth(false, required);
        }
    }

    // Ollama lists "llama3.2:latest" for a model requested as "llama3.2".
    private static bool IsInstalled(string model, List<string> installed) =>
        installed.Contains(model, StringComparer.OrdinalIgnoreCase) ||
        (!model.Contains(':') && installed.Contains($"{model}:latest", StringComparer.OrdinalIgnoreCase));
}
