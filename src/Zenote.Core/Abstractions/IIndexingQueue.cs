using Zenote.Core.Domain;

namespace Zenote.Core.Abstractions;

public interface IIndexingQueue
{
    /// Raised on a background thread.
    event EventHandler<IndexingProgress>? ProgressChanged;

    ValueTask EnqueueAsync(Document document, CancellationToken ct);

    /// Picks up documents left unfinished when the app last closed.
    Task ResumeAsync(CancellationToken ct);

    /// Completes once the document has stopped indexing.
    Task CancelAsync(Guid documentId);
}
