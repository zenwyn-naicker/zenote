using System.Threading.Channels;
using Zenote.Core.Abstractions;
using Zenote.Core.Domain;

namespace Zenote.Core.Indexing;

/// Indexes one document at a time on a single background worker, so the UI never waits on Ollama.
public sealed class IndexingQueue : IIndexingQueue, IDisposable
{
    private const int EmbeddingBatchSize = 16;

    private readonly IPdfTextExtractor _extractor;
    private readonly IChunker _chunker;
    private readonly ITextEmbedder _embedder;
    private readonly IDocumentRepository _documents;
    private readonly IVectorStore _vectors;

    private readonly Channel<Document> _queue = Channel.CreateUnbounded<Document>();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _worker;

    private readonly Lock _lock = new();
    private readonly HashSet<Guid> _pending = [];
    private readonly HashSet<Guid> _cancelled = [];
    private RunningDocument? _running;

    public IndexingQueue(
        IPdfTextExtractor extractor,
        IChunker chunker,
        ITextEmbedder embedder,
        IDocumentRepository documents,
        IVectorStore vectors)
    {
        _extractor = extractor;
        _chunker = chunker;
        _embedder = embedder;
        _documents = documents;
        _vectors = vectors;
        _worker = Task.Run(RunAsync);
    }

    public event EventHandler<IndexingProgress>? ProgressChanged;

    public ValueTask EnqueueAsync(Document document, CancellationToken ct)
    {
        lock (_lock)
        {
            _pending.Add(document.Id);
            _cancelled.Remove(document.Id);
        }

        return _queue.Writer.WriteAsync(document, ct);
    }

    public async Task ResumeAsync(CancellationToken ct)
    {
        foreach (var document in await _documents.ListUnfinishedAsync(ct))
        {
            await EnqueueAsync(document, ct);
        }
    }

    public async Task CancelAsync(Guid documentId)
    {
        RunningDocument? running = null;

        lock (_lock)
        {
            if (_running?.Id == documentId)
            {
                running = _running;
            }
            else if (_pending.Contains(documentId))
            {
                _cancelled.Add(documentId);
            }
        }

        if (running is not null)
        {
            await running.Source.CancelAsync();
            await running.Finished.Task;
        }
    }

    public void Dispose()
    {
        _shutdown.Cancel();
        _queue.Writer.TryComplete();

        try
        {
            _worker.Wait();
        }
        catch (AggregateException ex) when (ex.InnerException is OperationCanceledException)
        {
            // Expected while the worker is stopping.
        }

        _shutdown.Dispose();
    }

    private async Task RunAsync()
    {
        await foreach (var document in _queue.Reader.ReadAllAsync(_shutdown.Token))
        {
            if (TryStart(document) is { } running)
            {
                await ProcessAsync(document, running);
            }
            else
            {
                await FinishAsync(document, DocumentStatus.Cancelled, null);
            }
        }
    }

    private RunningDocument? TryStart(Document document)
    {
        lock (_lock)
        {
            _pending.Remove(document.Id);

            if (_cancelled.Remove(document.Id))
            {
                return null;
            }

            _running = new RunningDocument(document.Id, CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token));
            return _running;
        }
    }

    private async Task ProcessAsync(Document document, RunningDocument running)
    {
        try
        {
            await IndexAsync(document, running.Source.Token);
            await FinishAsync(document, DocumentStatus.Ready, null);
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
            // Stays unfinished, so ResumeAsync picks it up next launch.
        }
        catch (OperationCanceledException)
        {
            await ClearAsync(document);
            await FinishAsync(document, DocumentStatus.Cancelled, null);
        }
        catch (HttpRequestException)
        {
            await ClearAsync(document);
            await FinishAsync(document, DocumentStatus.Failed, "Couldn't reach Ollama. Open it, then retry.");
        }
        catch (Exception ex)
        {
            await ClearAsync(document);
            await FinishAsync(document, DocumentStatus.Failed, ex.Message);
        }
        finally
        {
            lock (_lock)
            {
                _running = null;
            }

            running.Source.Dispose();
            running.Finished.SetResult();
        }
    }

    private async Task IndexAsync(Document document, CancellationToken ct)
    {
        await SetStageAsync(document, DocumentStatus.Extracting, 0, 0, ct);
        await _documents.DeleteChunksAsync(document.Id, ct);

        var extracted = await _extractor.ExtractAsync(document.StoredPath, ct);
        var chunks = _chunker.Chunk(extracted);
        var ids = await _documents.InsertChunksAsync(document.Id, chunks, ct);

        await SetStageAsync(document, DocumentStatus.Embedding, 0, chunks.Count, ct);

        for (var start = 0; start < chunks.Count; start += EmbeddingBatchSize)
        {
            var batch = chunks.Skip(start).Take(EmbeddingBatchSize).ToList();
            var texts = batch.Select(chunk => $"{document.FileName}, p.{chunk.Page}\n{chunk.Text}").ToList();
            var vectors = await _embedder.EmbedDocumentsAsync(texts, ct);

            await _vectors.AddAsync(document.NotebookId, [.. vectors.Select((vector, i) => (ids[start + i], vector))], ct);

            ProgressChanged?.Invoke(this, new IndexingProgress(
                document.Id, DocumentStatus.Embedding, start + batch.Count, chunks.Count));
        }
    }

    private async Task SetStageAsync(Document document, DocumentStatus stage, int completed, int total, CancellationToken ct)
    {
        await _documents.UpdateStatusAsync(document.Id, stage, null, ct);
        ProgressChanged?.Invoke(this, new IndexingProgress(document.Id, stage, completed, total));
    }

    private async Task FinishAsync(Document document, DocumentStatus status, string? error)
    {
        await _documents.UpdateStatusAsync(document.Id, status, error, CancellationToken.None);
        ProgressChanged?.Invoke(this, new IndexingProgress(document.Id, status, 0, 0, error));
    }

    private Task ClearAsync(Document document) => _documents.DeleteChunksAsync(document.Id, CancellationToken.None);

    private sealed record RunningDocument(Guid Id, CancellationTokenSource Source)
    {
        public TaskCompletionSource Finished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
