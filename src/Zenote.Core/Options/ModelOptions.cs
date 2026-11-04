namespace Zenote.Core.Options;

public sealed class ModelOptions
{
    public string OllamaUrl { get; set; } = "http://localhost:11434";
    public string ChatModel { get; set; } = "llama3.2";
    public int ChatContextTokens { get; set; } = 8192;
    public string EmbeddingModel { get; set; } = "nomic-embed-text";
    public int EmbeddingDimensions { get; set; } = 768;
    public string DocumentPrefix { get; set; } = "search_document: ";
    public string QueryPrefix { get; set; } = "search_query: ";
}
