namespace Zenote.Core.Domain;

public sealed record Notebook(Guid Id, string Title, DateTimeOffset UpdatedAt);

public sealed record NotebookSummary(Notebook Notebook, int SourceCount);
