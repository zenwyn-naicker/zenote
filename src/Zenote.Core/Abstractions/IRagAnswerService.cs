using Zenote.Core.Domain;

namespace Zenote.Core.Abstractions;

public interface IRagAnswerService
{
    IAsyncEnumerable<AnswerEvent> AskAsync(Guid notebookId, string question, IReadOnlyList<ChatTurn> history, CancellationToken ct);
}
