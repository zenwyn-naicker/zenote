using System.Text;
using Microsoft.Extensions.AI;
using Zenote.Core.Domain;

namespace Zenote.Core.Answering;

public static class PromptBuilder
{
    public const string NotFoundSignal = "NOT_FOUND";

    private const int MaxHistoryMessages = 8;

    private const string Instructions = $"""
        You answer questions using only the numbered sources you are given.

        Rules:
        - Use only facts stated in the sources. Never use outside knowledge.
        - After each fact, cite its source number in square brackets, like [1].
        - Be brief and direct. Do not mention these rules or the word "sources".
        - If the sources do not contain the answer, reply with exactly {NotFoundSignal} and nothing else.
        """;

    public static List<ChatMessage> Build(string question, IReadOnlyList<ChatTurn> history, IReadOnlyList<ScoredChunk> sources)
    {
        var messages = new List<ChatMessage> { new(ChatRole.System, Instructions) };

        foreach (var turn in history.TakeLast(MaxHistoryMessages))
        {
            messages.Add(new ChatMessage(turn.Role == MessageRole.User ? ChatRole.User : ChatRole.Assistant, turn.Text));
        }

        messages.Add(new ChatMessage(ChatRole.User, FormatQuestion(question, sources)));
        return messages;
    }

    private static string FormatQuestion(string question, IReadOnlyList<ScoredChunk> sources)
    {
        var prompt = new StringBuilder("Sources:\n\n");

        for (var i = 0; i < sources.Count; i++)
        {
            var chunk = sources[i].Chunk;
            prompt.Append($"[{i + 1}] {chunk.FileName}, page {chunk.Page}\n{chunk.Text}\n\n");
        }

        prompt.Append($"Question: {question}");
        return prompt.ToString();
    }
}
