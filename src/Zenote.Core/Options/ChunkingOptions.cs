namespace Zenote.Core.Options;

public sealed class ChunkingOptions
{
    private const int CharsPerToken = 4;

    public int TargetTokens { get; set; } = 400;
    public int OverlapTokens { get; set; } = 60;
    public int MinTokens { get; set; } = 50;

    public int TargetChars => TargetTokens * CharsPerToken;
    public int OverlapChars => OverlapTokens * CharsPerToken;
    public int MinChars => MinTokens * CharsPerToken;
}
