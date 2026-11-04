using Microsoft.Data.Sqlite;

namespace Zenote.Infrastructure.Persistence;

public sealed class SqliteConnectionFactory(string databasePath)
{
    private const string VecEntryPoint = "sqlite3_vec_init";

    public SqliteConnection Open()
    {
        var connection = new SqliteConnection($"Data Source={databasePath}");
        connection.Open();
        connection.EnableExtensions(true);
        connection.LoadExtension(FindVecLibrary(), VecEntryPoint);
        connection.EnableExtensions(false);
        return connection;
    }

    // SQLite doesn't follow NuGet's probing rules, so it needs a full path. The library sits beside the
    // app in a RID-specific publish, and under runtimes/ in a normal build.
    private static string FindVecLibrary()
    {
        string[] candidates =
        [
            Path.Combine(AppContext.BaseDirectory, "vec0.dylib"),
            Path.Combine(AppContext.BaseDirectory, "runtimes", "osx-arm64", "native", "vec0.dylib"),
        ];

        return candidates.FirstOrDefault(File.Exists)
            ?? throw new FileNotFoundException("vec0.dylib was not found.", candidates[0]);
    }
}
