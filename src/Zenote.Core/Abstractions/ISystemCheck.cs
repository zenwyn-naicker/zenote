namespace Zenote.Core.Abstractions;

public sealed record CheckResult(string Name, bool Ok, string? Detail = null);

/// Confirms everything Zenote depends on works on this machine.
public interface ISystemCheck
{
    Task<IReadOnlyList<CheckResult>> RunAsync(CancellationToken ct);
}
