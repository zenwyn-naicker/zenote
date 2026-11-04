using Zenote.Core.Domain;

namespace Zenote.Core.Abstractions;

public interface IRetriever
{
    Task<RetrievalResult> RetrieveAsync(Guid notebookId, string query, CancellationToken ct);
}
