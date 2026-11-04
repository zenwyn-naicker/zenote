using Microsoft.Extensions.AI;

namespace Zenote.Infrastructure.Ai;

/// Lets the chat model change while the app runs, without anything else having to know.
public sealed class SwitchableChatClient(Func<string, IChatClient> create, string model) : IChatClient
{
    private IChatClient _inner = create(model);

    public string Model { get; private set; } = model;

    public void Switch(string model)
    {
        if (model == Model)
        {
            return;
        }

        _inner = create(model);
        Model = model;
    }

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        _inner.GetResponseAsync(messages, options, cancellationToken);

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        _inner.GetStreamingResponseAsync(messages, options, cancellationToken);

    public object? GetService(Type serviceType, object? serviceKey = null) => _inner.GetService(serviceType, serviceKey);

    public void Dispose() => _inner.Dispose();
}
