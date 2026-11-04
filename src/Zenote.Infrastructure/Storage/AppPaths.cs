namespace Zenote.Infrastructure.Storage;

public sealed class AppPaths(string root)
{
    public static AppPaths Default { get; } = new(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Zenote"));

    public string Root { get; } = root;
    public string DatabasePath => Path.Combine(Root, "zenote.db");
    public string DocumentsFolder => Path.Combine(Root, "documents");
}
