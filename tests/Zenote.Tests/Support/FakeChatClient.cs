using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace Zenote.Tests.Support;

/// Streams the given tokens, and records what it was asked.
public sealed class FakeChatClient(params string[] tokens) : IChatClient
{
    public List<ChatMessage> ReceivedMessages { get; private set; } = [];
    public ChatOptions? ReceivedOptions { get; private set; }
    public int Calls { get; private set; }

    /// When set, the stream pauses here after the first token until cancelled.
    public bool HangAfterFirstToken { get; set; }

    /// Ends the stream instead of throwing when cancelled, as some clients do.
    public bool EndQuietlyOnCancel { get; set; }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Calls++;
        ReceivedMessages = messages.ToList();
        ReceivedOptions = options;

        foreach (var token in tokens)
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, token);
            await Task.Yield();

            if (HangAfterFirstToken)
            {
                if (EndQuietlyOnCancel)
                {
                    await Task.WhenAny(Task.Delay(Timeout.Infinite, cancellationToken));
                    yield break;
                }

                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
        }
    }

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}
