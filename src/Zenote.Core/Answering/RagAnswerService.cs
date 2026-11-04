using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using Zenote.Core.Abstractions;
using Zenote.Core.Domain;

namespace Zenote.Core.Answering;

public sealed partial class RagAnswerService(IRetriever retriever, IChatClient chat) : IRagAnswerService
{
    public const string NotFoundText = "I couldn't find this in your documents.";

    private const int ClosestMatchCount = 3;
    private const int ShortAnswerLength = 160;

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

        // Some clients end the stream quietly when cancelled, which mustn't look like a finished answer.
        ct.ThrowIfCancellationRequested();

        var answer = text.ToString().Trim();

        if (answer.Length == 0 || answer.StartsWith(PromptBuilder.NotFoundSignal, StringComparison.Ordinal))
        {
            yield return NotFound(retrieval.Chunks);
            yield break;
        }

        var (cleaned, citations) = CitationParser.Parse(RemoveSourceLabels(answer, retrieval.Chunks), retrieval.Chunks);

        if (!HasWords(cleaned) || (citations.Count == 0 && IsNonAnswer(cleaned)))
        {
            yield return NotFound(retrieval.Chunks);
            yield break;
        }

        // With no citations, the pages the model was given are still the best way to check its answer.
        yield return new AnswerCompleted(new Answer(
            cleaned,
            NotFound: false,
            citations.Count > 0 ? citations : Unnumbered(retrieval.Chunks)));
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

    // Small models often decline in their own words instead of using the signal.
    private static bool IsNonAnswer(string answer) =>
        answer.Length <= ShortAnswerLength && DeclinedInOwnWords().IsMatch(answer);

    // Small models sometimes repeat a source's heading from the prompt, like "[3] a.pdf, page 3".
    private static string RemoveSourceLabels(string answer, IReadOnlyList<ScoredChunk> sources) =>
        sources.Aggregate(answer, (text, source) =>
            text.Replace($"{source.Chunk.FileName}, page {source.Chunk.Page}", "", StringComparison.OrdinalIgnoreCase));

    private static bool HasWords(string answer) => CitationMarker().Replace(answer, "").Any(char.IsLetterOrDigit);

    private static AnswerCompleted NotFound(IReadOnlyList<ScoredChunk> chunks) =>
        new(new Answer(
            NotFoundText,
            NotFound: true,
            Unnumbered(chunks.Take(ClosestMatchCount))));

    private static List<Citation> Unnumbered(IEnumerable<ScoredChunk> chunks) =>
        chunks.Select(chunk => CitationParser.ToCitation(0, chunk)).ToList();

    [GeneratedRegex(@"(\bnot\b|n't\b|\bno\b).{0,40}\b(stated|mentioned|provided|specified|included|contain|information|found)\b", RegexOptions.IgnoreCase)]
    private static partial Regex DeclinedInOwnWords();

    [GeneratedRegex(@"\[\d+\]")]
    private static partial Regex CitationMarker();
}
