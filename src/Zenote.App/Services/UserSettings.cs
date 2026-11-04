using System.Text.Json;
using System.Text.Json.Nodes;

namespace Zenote.App.Services;

/// The few settings people can change, kept apart from the defaults shipped in appsettings.json.
public sealed class UserSettings(string path)
{
    public string Path { get; } = path;

    /// False if the file exists but is damaged, in which case the app runs on its defaults.
    public bool IsReadable => !File.Exists(Path) || Read() is not null;

    public void SaveChatModel(string model)
    {
        var settings = Read() ?? [];

        if (settings["Models"] is not JsonObject models)
        {
            models = [];
            settings["Models"] = models;
        }

        models["ChatModel"] = model;
        File.WriteAllText(Path, settings.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    private JsonObject? Read()
    {
        try
        {
            return File.Exists(Path) ? JsonNode.Parse(File.ReadAllText(Path)) as JsonObject : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
