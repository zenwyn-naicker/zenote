using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.AI;
using Zenote.Core.Abstractions;
using Zenote.Core.Domain;

namespace Zenote.Core.Answering;

public sealed class RagAnswerService(IRetriever retriever, IChatClient chat) : IRagAnswerService
{
    public const string NotFoundText = "I couldn't find this in your documents.";

    private const int ClosestMatchCount = 3;

    public async IAsyncEnumerable<AnswerEvent> AskAsync(
        Guid notebookId,
        string question,
        IReadOnlyList<ChatTurn> history,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var retrieval = await retriever.RetrieveAsync(notebookId, RetrievalQuery(question, history), ct);

        if (!retrieval.Relevant)
        {
            yield return NotFound(retrieval.Chunks);
            yield break;
        }

        var messages = PromptBuilder.Build(question, history, retrieval.Chunks);
        var options = new ChatOptions { Temperature = 0.1f };
        var text = new StringBuilder();
        var released = false;

        await foreach (var update in chat.GetStreamingResponseAsync(messages, options, ct))
        {
            if (string.IsNullOrEmpty(update.Text))
            {
                continue;
            }

            text.Append(update.Text);

            if (released)
            {
                yield return new TokenReceived(update.Text);
            }
            else if (!CouldBeNotFoundSignal(text))
            {
                released = true;
                yield return new TokenReceived(text.ToString().TrimStart());
            }
        }

        var answer = text.ToString().Trim();

        if (answer.Length == 0 || answer.StartsWith(PromptBuilder.NotFoundSignal, StringComparison.Ordinal))
        {
            yield return NotFound(retrieval.Chunks);
            yield break;
        }

        var (cleaned, citations) = CitationParser.Parse(answer, retrieval.Chunks);
        yield return new AnswerCompleted(new Answer(cleaned, NotFound: false, citations));
    }

    // Follow-ups like "what about the second one?" mean little alone. Searching with the previous
    // question as well finds the right passages without a slow extra model call.
    private static string RetrievalQuery(string question, IReadOnlyList<ChatTurn> history)
    {
        var previous = history.LastOrDefault(turn => turn.Role == MessageRole.User);
        return previous is null ? question : $"{previous.Text} {question}";
    }

    // Held back until it can't be the signal, so "NOT_FOUND" never flashes up in the chat.
    private static bool CouldBeNotFoundSignal(StringBuilder text)
    {
        var sofar = text.ToString().TrimStart();

        return PromptBuilder.NotFoundSignal.StartsWith(sofar, StringComparison.Ordinal) ||
               sofar.StartsWith(PromptBuilder.NotFoundSignal, StringComparison.Ordinal);
    }

    private static AnswerCompleted NotFound(IReadOnlyList<ScoredChunk> chunks) =>
        new(new Answer(
            NotFoundText,
            NotFound: true,
            chunks.Take(ClosestMatchCount).Select(chunk => CitationParser.ToCitation(0, chunk)).ToList()));
}
