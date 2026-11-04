using System.Runtime.InteropServices;
using Microsoft.Data.Sqlite;
using Shouldly;
using Zenote.Infrastructure.Persistence;

namespace Zenote.Tests.Integration;

[Trait("Category", "Integration")]
public class SqliteVecTests
{
    [Fact]
    public void Extension_loads_and_reports_its_version()
    {
        using var connection = new SqliteConnectionFactory(":memory:").Open();

        Scalar(connection, "SELECT vec_version()").ShouldBe("v0.1.9");
    }

    [Fact]
    public void Search_finds_the_nearest_vector_inside_one_notebook_only()
    {
        using var connection = new SqliteConnectionFactory(":memory:").Open();
        Execute(connection, """
            CREATE VIRTUAL TABLE vec_chunks USING vec0(
              notebook_id TEXT PARTITION KEY,
              embedding float[3] distance_metric=cosine)
            """);
        Insert(connection, 1, "a", [1, 0, 0]);
        Insert(connection, 2, "a", [0, 1, 0]);
        Insert(connection, 3, "b", [1, 0, 0]);

        using var search = connection.CreateCommand();
        search.CommandText = """
            SELECT rowid FROM vec_chunks
            WHERE embedding MATCH $query AND k = 5 AND notebook_id = 'a'
            ORDER BY distance
            """;
        search.Parameters.AddWithValue("$query", ToBlob([0.9f, 0.1f, 0]));

        var ids = new List<long>();
        using var reader = search.ExecuteReader();
        while (reader.Read())
        {
            ids.Add(reader.GetInt64(0));
        }

        ids.ShouldBe([1L, 2L]);
    }

    private static void Insert(SqliteConnection connection, long id, string notebookId, float[] vector)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO vec_chunks(rowid, notebook_id, embedding) VALUES ($id, $notebook, $vector)";
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$notebook", notebookId);
        command.Parameters.AddWithValue("$vector", ToBlob(vector));
        command.ExecuteNonQuery();
    }

    private static byte[] ToBlob(float[] vector) => MemoryMarshal.AsBytes(vector.AsSpan()).ToArray();

    private static object? Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
