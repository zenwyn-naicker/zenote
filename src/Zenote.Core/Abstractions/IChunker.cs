using Zenote.Core.Domain;

namespace Zenote.Core.Abstractions;

public interface IChunker
{
    IReadOnlyList<ChunkDraft> Chunk(ExtractedDocument document);
}
