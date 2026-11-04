namespace Zenote.Core.Domain;

public enum DocumentStatus { Queued, Extracting, Embedding, Ready, Failed, Cancelled }

public sealed record Document(
    Guid Id,
    Guid NotebookId,
    string FileName,
    string StoredPath,
    string ContentHash,
    int PageCount,
    int PagesWithoutText,
    DocumentStatus Status,
    string? Error,
    DateTimeOffset ImportedAt)
{
    public bool LooksScanned => PagesWithoutText * 2 > PageCount;
}

public sealed record ExtractedPage(int PageNumber, string Text, bool HasText);

public sealed record ExtractedDocument(IReadOnlyList<ExtractedPage> Pages);
