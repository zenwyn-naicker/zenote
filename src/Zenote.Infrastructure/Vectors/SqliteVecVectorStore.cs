using System.Runtime.InteropServices;
using Zenote.Core.Abstractions;
using Zenote.Infrastructure.Persistence;

namespace Zenote.Infrastructure.Vectors;

public sealed class SqliteVecVectorStore(SqliteConnectionFactory connections) : IVectorStore
{
    public async Task AddAsync(Guid notebookId, IReadOnlyList<(long ChunkId, ReadOnlyMemory<float> Vector)> items, CancellationToken ct)
    {
        using var connection = connections.Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO vec_chunks(rowid, notebook_id, embedding) VALUES ($id, $notebook, $vector)";
        var id = command.Parameters.Add("$id", Microsoft.Data.Sqlite.SqliteType.Integer);
        var vector = command.Parameters.Add("$vector", Microsoft.Data.Sqlite.SqliteType.Blob);
        command.Parameters.AddWithValue("$notebook", notebookId.ToString());

        foreach (var item in items)
        {
            id.Value = item.ChunkId;
            vector.Value = ToBlob(item.Vector);
            await command.ExecuteNonQueryAsync(ct);
        }

        transaction.Commit();
    }

    public async Task<IReadOnlyList<(long ChunkId, double Similarity)>> SearchAsync(
        Guid notebookId, ReadOnlyMemory<float> query, int k, CancellationToken ct)
    {
        using var connection = connections.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT rowid, distance FROM vec_chunks
            WHERE embedding MATCH $query AND k = $k AND notebook_id = $notebook
            ORDER BY distance
            """;
        command.Parameters.AddWithValue("$query", ToBlob(query));
        command.Parameters.AddWithValue("$k", k);
        command.Parameters.AddWithValue("$notebook", notebookId.ToString());

        var results = new List<(long, double)>();
        using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add((reader.GetInt64(0), 1 - reader.GetDouble(1)));
        }

        return results;
    }

    private static byte[] ToBlob(ReadOnlyMemory<float> vector) => MemoryMarshal.AsBytes(vector.Span).ToArray();
}
