namespace Zenote.Core.Domain;

public sealed record ChunkDraft(int Page, int Ordinal, string Text);

public sealed record Chunk(long Id, Guid DocumentId, string FileName, int Page, string Text);

public sealed record ScoredChunk(Chunk Chunk, double Similarity);

/// <param name="Relevant">False when even the best match is too weak to answer from.</param>
public sealed record RetrievalResult(IReadOnlyList<ScoredChunk> Chunks, bool Relevant);

public sealed record IndexingProgress(Guid DocumentId, DocumentStatus Stage, int Completed, int Total, string? Error = null);
