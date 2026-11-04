using Zenote.Core.Abstractions;
using Zenote.Core.Domain;

namespace Zenote.Infrastructure.Persistence;

public sealed class NotebookRepository(SqliteConnectionFactory connections) : INotebookRepository
{
    public async Task<Notebook> CreateAsync(string title, CancellationToken ct)
    {
        var notebook = new Notebook(Guid.NewGuid(), title, DateTimeOffset.UtcNow);

        using var connection = connections.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO notebooks(id, title, updated_at) VALUES ($id, $title, $updated)";
        command.Parameters.AddWithValue("$id", notebook.Id.ToString());
        command.Parameters.AddWithValue("$title", notebook.Title);
        command.Parameters.AddWithValue("$updated", notebook.UpdatedAt.ToString("O"));
        await command.ExecuteNonQueryAsync(ct);

        return notebook;
    }

    public async Task<IReadOnlyList<NotebookSummary>> ListAsync(CancellationToken ct)
    {
        using var connection = connections.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT n.id, n.title, n.updated_at, COUNT(d.id)
            FROM notebooks n
            LEFT JOIN documents d ON d.notebook_id = n.id
            GROUP BY n.id
            ORDER BY n.updated_at DESC
            """;

        var notebooks = new List<NotebookSummary>();
        using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var notebook = new Notebook(
                Guid.Parse(reader.GetString(0)),
                reader.GetString(1),
                DateTimeOffset.Parse(reader.GetString(2)));

            notebooks.Add(new NotebookSummary(notebook, reader.GetInt32(3)));
        }

        return notebooks;
    }

    public async Task RenameAsync(Guid id, string title, CancellationToken ct)
    {
        using var connection = connections.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE notebooks SET title = $title, updated_at = $updated WHERE id = $id";
        command.Parameters.AddWithValue("$id", id.ToString());
        command.Parameters.AddWithValue("$title", title);
        command.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        using var connection = connections.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM notebooks WHERE id = $id";
        command.Parameters.AddWithValue("$id", id.ToString());
        await command.ExecuteNonQueryAsync(ct);
    }
}
