using Microsoft.Data.Sqlite;
using Zenote.Core.Abstractions;
using Zenote.Core.Domain;

namespace Zenote.Infrastructure.Persistence;

public sealed class DocumentRepository(SqliteConnectionFactory connections) : IDocumentRepository
{
    private const string Columns =
        "id, notebook_id, file_name, stored_path, content_hash, page_count, pages_without_text, status, error, imported_at";

    public async Task<Document?> FindByHashAsync(Guid notebookId, string contentHash, CancellationToken ct)
    {
        using var connection = connections.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {Columns} FROM documents WHERE notebook_id = $notebook AND content_hash = $hash";
        command.Parameters.AddWithValue("$notebook", notebookId.ToString());
        command.Parameters.AddWithValue("$hash", contentHash);

        using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Read(reader) : null;
    }

    public async Task<IReadOnlyList<Document>> ListAsync(Guid notebookId, CancellationToken ct)
    {
        using var connection = connections.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {Columns} FROM documents WHERE notebook_id = $notebook ORDER BY imported_at";
        command.Parameters.AddWithValue("$notebook", notebookId.ToString());

        var documents = new List<Document>();
        using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            documents.Add(Read(reader));
        }

        return documents;
    }

    public async Task<IReadOnlyList<Document>> ListUnfinishedAsync(CancellationToken ct)
    {
        using var connection = connections.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT {Columns} FROM documents
            WHERE status IN ('Queued', 'Extracting', 'Embedding')
            ORDER BY imported_at
            """;

        var documents = new List<Document>();
        using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            documents.Add(Read(reader));
        }

        return documents;
    }

    public async Task SaveAsync(Document document, CancellationToken ct)
    {
        using var connection = connections.Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            INSERT OR REPLACE INTO documents({Columns})
            VALUES ($id, $notebook, $name, $path, $hash, $pages, $empty, $status, $error, $imported);
            UPDATE notebooks SET updated_at = $imported WHERE id = $notebook;
            """;
        command.Parameters.AddWithValue("$id", document.Id.ToString());
        command.Parameters.AddWithValue("$notebook", document.NotebookId.ToString());
        command.Parameters.AddWithValue("$name", document.FileName);
        command.Parameters.AddWithValue("$path", document.StoredPath);
        command.Parameters.AddWithValue("$hash", document.ContentHash);
        command.Parameters.AddWithValue("$pages", document.PageCount);
        command.Parameters.AddWithValue("$empty", document.PagesWithoutText);
        command.Parameters.AddWithValue("$status", document.Status.ToString());
        command.Parameters.AddWithValue("$error", (object?)document.Error ?? DBNull.Value);
        command.Parameters.AddWithValue("$imported", document.ImportedAt.ToString("O"));
        await command.ExecuteNonQueryAsync(ct);
        transaction.Commit();
    }

    public async Task UpdateStatusAsync(Guid id, DocumentStatus status, string? error, CancellationToken ct)
    {
        using var connection = connections.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE documents SET status = $status, error = $error WHERE id = $id";
        command.Parameters.AddWithValue("$id", id.ToString());
        command.Parameters.AddWithValue("$status", status.ToString());
        command.Parameters.AddWithValue("$error", (object?)error ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<long>> InsertChunksAsync(Guid documentId, IReadOnlyList<ChunkDraft> chunks, CancellationToken ct)
    {
        using var connection = connections.Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO chunks(document_id, page, ordinal, text) VALUES ($document, $page, $ordinal, $text);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$document", documentId.ToString());
        var page = command.Parameters.Add("$page", SqliteType.Integer);
        var ordinal = command.Parameters.Add("$ordinal", SqliteType.Integer);
        var text = command.Parameters.Add("$text", SqliteType.Text);

        var ids = new List<long>(chunks.Count);
        foreach (var chunk in chunks)
        {
            page.Value = chunk.Page;
            ordinal.Value = chunk.Ordinal;
            text.Value = chunk.Text;
            ids.Add((long)(await command.ExecuteScalarAsync(ct))!);
        }

        transaction.Commit();
        return ids;
    }

    public async Task<IReadOnlyList<Chunk>> GetChunksAsync(IReadOnlyList<long> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        using var connection = connections.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT c.id, c.document_id, d.file_name, c.page, c.text
            FROM chunks c
            JOIN documents d ON d.id = c.document_id
            WHERE c.id IN ({string.Join(",", ids)})
            """;

        var chunks = new Dictionary<long, Chunk>();
        using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var id = reader.GetInt64(0);
            chunks[id] = new Chunk(id, Guid.Parse(reader.GetString(1)), reader.GetString(2), reader.GetInt32(3), reader.GetString(4));
        }

        return ids.Where(chunks.ContainsKey).Select(id => chunks[id]).ToList();
    }

    public async Task DeleteChunksAsync(Guid documentId, CancellationToken ct)
    {
        using var connection = connections.Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            DELETE FROM vec_chunks WHERE rowid IN (SELECT id FROM chunks WHERE document_id = $id);
            DELETE FROM chunks WHERE document_id = $id;
            """;
        command.Parameters.AddWithValue("$id", documentId.ToString());
        await command.ExecuteNonQueryAsync(ct);
        transaction.Commit();
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        using var connection = connections.Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            DELETE FROM vec_chunks WHERE rowid IN (SELECT id FROM chunks WHERE document_id = $id);
            DELETE FROM documents WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", id.ToString());
        await command.ExecuteNonQueryAsync(ct);
        transaction.Commit();
    }

    private static Document Read(SqliteDataReader reader) => new(
        Guid.Parse(reader.GetString(0)),
        Guid.Parse(reader.GetString(1)),
        reader.GetString(2),
        reader.GetString(3),
        reader.GetString(4),
        reader.GetInt32(5),
        reader.GetInt32(6),
        Enum.Parse<DocumentStatus>(reader.GetString(7)),
        reader.IsDBNull(8) ? null : reader.GetString(8),
        DateTimeOffset.Parse(reader.GetString(9)));
}
