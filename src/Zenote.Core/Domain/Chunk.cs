namespace Zenote.Core.Domain;

public sealed record ChunkDraft(int Page, int Ordinal, string Text);

public sealed record Chunk(long Id, Guid DocumentId, string FileName, int Page, string Text);

public sealed record IndexingProgress(Guid DocumentId, DocumentStatus Stage, int Completed, int Total, string? Error = null);
