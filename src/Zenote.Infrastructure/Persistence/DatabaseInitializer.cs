namespace Zenote.Infrastructure.Persistence;

public sealed class DatabaseInitializer(SqliteConnectionFactory connections)
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
    }
}
