namespace Zenote.Core.Abstractions;

public sealed record AiHealth(bool Reachable, IReadOnlyList<string> MissingModels);

public interface IAiHealthCheck
{
    Task<AiHealth> CheckAsync(CancellationToken ct);
}
