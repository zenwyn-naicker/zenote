namespace Zenote.Infrastructure.Storage;

public sealed class AppPaths(string root)
{
    public static AppPaths Default { get; } = new(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Zenote"));

    public string Root { get; } = root;
    public string DatabasePath => Path.Combine(Root, "zenote.db");
    public string DocumentsFolder => Path.Combine(Root, "documents");

    /// Creates the data folder, readable only by the current user, because it holds people's documents.
    public void EnsurePrivateRoot()
    {
        Directory.CreateDirectory(Root);
        File.SetUnixFileMode(Root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }
}
