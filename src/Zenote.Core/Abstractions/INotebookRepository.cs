using Zenote.Core.Domain;

namespace Zenote.Core.Abstractions;

public interface INotebookRepository
{
    Task<Notebook> CreateAsync(string title, CancellationToken ct);
    Task<IReadOnlyList<NotebookSummary>> ListAsync(CancellationToken ct);
    Task RenameAsync(Guid id, string title, CancellationToken ct);
    Task DeleteAsync(Guid id, CancellationToken ct);
}
