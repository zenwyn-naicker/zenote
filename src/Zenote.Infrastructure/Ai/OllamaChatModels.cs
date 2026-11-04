using OllamaSharp;
using Zenote.Core.Abstractions;

namespace Zenote.Infrastructure.Ai;

public sealed class OllamaChatModels(IOllamaApiClient client, SwitchableChatClient chat) : IChatModels
{
    private const string LatestTag = ":latest";

    public string Current => chat.Model;

    public async Task<IReadOnlyList<string>> ListAsync(CancellationToken ct)
    {
        var models = await client.ListLocalModelsAsync(ct);

        return models
            .Select(model => model.Name)
            .Where(name => !name.Contains("embed", StringComparison.OrdinalIgnoreCase))
            // Cloud models would send documents off this Mac, which Zenote promises not to do.
            .Where(name => !name.Contains("cloud", StringComparison.OrdinalIgnoreCase))
            .Select(name => name.EndsWith(LatestTag, StringComparison.Ordinal) ? name[..^LatestTag.Length] : name)
            .Order()
            .ToList();
    }

    public void Select(string model) => chat.Switch(model);
}
