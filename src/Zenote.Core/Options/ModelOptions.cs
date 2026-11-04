namespace Zenote.Core.Options;

public sealed class ModelOptions
{
    public string OllamaUrl { get; set; } = "http://localhost:11434";
    public string ChatModel { get; set; } = "llama3.2";
    public string EmbeddingModel { get; set; } = "nomic-embed-text";
}
