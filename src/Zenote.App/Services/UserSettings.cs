using System.Text.Json;
using System.Text.Json.Nodes;

namespace Zenote.App.Services;

/// The few settings people can change, kept apart from the defaults shipped in appsettings.json.
public sealed class UserSettings(string path)
{
    public string Path { get; } = path;

    public void SaveChatModel(string model)
    {
        var settings = File.Exists(Path) ? JsonNode.Parse(File.ReadAllText(Path))!.AsObject() : [];

        if (settings["Models"] is not JsonObject models)
        {
            models = [];
            settings["Models"] = models;
        }

        models["ChatModel"] = model;
        File.WriteAllText(Path, settings.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }
}
