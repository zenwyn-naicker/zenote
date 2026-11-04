using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using Zenote.Core.Options;

namespace Zenote.Infrastructure.Persistence;

public sealed class DatabaseInitializer(SqliteConnectionFactory connections, IOptions<ModelOptions> options)
{
    // Each entry moves the schema up one version. Never edit one that has shipped; add a new one.
    private static readonly string[] Migrations =
    [
        """
        CREATE TABLE notebooks(
          id TEXT PRIMARY KEY,
          title TEXT NOT NULL,
          updated_at TEXT NOT NULL);

        CREATE TABLE documents(
          id TEXT PRIMARY KEY,
          notebook_id TEXT NOT NULL REFERENCES notebooks(id) ON DELETE CASCADE,
          file_name TEXT NOT NULL,
          stored_path TEXT NOT NULL,
          content_hash TEXT NOT NULL,
          page_count INT NOT NULL,
          pages_without_text INT NOT NULL,
          status TEXT NOT NULL,
          error TEXT,
          imported_at TEXT NOT NULL,
          UNIQUE(notebook_id, content_hash));
        """,
        """
        CREATE TABLE meta(key TEXT PRIMARY KEY, value TEXT NOT NULL);

        CREATE TABLE chunks(
          id INTEGER PRIMARY KEY,
          document_id TEXT NOT NULL REFERENCES documents(id) ON DELETE CASCADE,
          page INT NOT NULL,
          ordinal INT NOT NULL,
          text TEXT NOT NULL);

        CREATE INDEX chunks_document ON chunks(document_id);
        """,
        """
        CREATE TABLE conversations(
          id TEXT PRIMARY KEY,
          notebook_id TEXT NOT NULL REFERENCES notebooks(id) ON DELETE CASCADE,
          title TEXT NOT NULL,
          updated_at TEXT NOT NULL);

        CREATE TABLE messages(
          sequence INTEGER PRIMARY KEY,
          id TEXT NOT NULL UNIQUE,
          conversation_id TEXT NOT NULL REFERENCES conversations(id) ON DELETE CASCADE,
          role TEXT NOT NULL,
          content TEXT NOT NULL,
          not_found INT NOT NULL,
          citations_json TEXT NOT NULL,
          created_at TEXT NOT NULL);

        CREATE INDEX messages_conversation ON messages(conversation_id);
        """,
    ];

    public void Initialize()
    {
        using var connection = connections.Open();
        using var command = connection.CreateCommand();

        command.CommandText = "PRAGMA journal_mode = WAL; PRAGMA user_version;";
        var version = 0;
        using (var reader = command.ExecuteReader())
        {
            reader.NextResult();
            reader.Read();
            version = reader.GetInt32(0);
        }

        foreach (var (migration, index) in Migrations.Select((sql, i) => (sql, i)).Skip(version))
        {
            using var transaction = connection.BeginTransaction();
            command.Transaction = transaction;
            command.CommandText = $"{migration} PRAGMA user_version = {index + 1};";
            command.ExecuteNonQuery();
            transaction.Commit();
        }

        EnsureVectorIndex(connection);
    }

    // Vectors from different models can't be compared, so changing the model rebuilds the index.
    // Documents go back to Queued, and the indexing queue re-embeds them on launch.
    private void EnsureVectorIndex(SqliteConnection connection)
    {
        var model = options.Value;
        var signature = $"{model.EmbeddingModel}:{model.EmbeddingDimensions}";

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM meta WHERE key = 'embedding_signature'";

        if ((string?)command.ExecuteScalar() == signature)
        {
            return;
        }

        using var transaction = connection.BeginTransaction();
        command.Transaction = transaction;
        command.CommandText = $"""
            DROP TABLE IF EXISTS vec_chunks;
            CREATE VIRTUAL TABLE vec_chunks USING vec0(
              notebook_id TEXT PARTITION KEY,
              embedding float[{model.EmbeddingDimensions}] distance_metric=cosine);
            DELETE FROM chunks;
            UPDATE documents SET status = 'Queued', error = NULL;
            INSERT OR REPLACE INTO meta(key, value) VALUES ('embedding_signature', $signature);
            """;
        command.Parameters.AddWithValue("$signature", signature);
        command.ExecuteNonQuery();
        transaction.Commit();
    }
}
