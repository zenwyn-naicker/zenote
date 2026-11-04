namespace Zenote.Core.Abstractions;

/// Loads the models ahead of the first question, so the first answer isn't slow.
public interface IModelWarmup
{
    Task WarmUpAsync(CancellationToken ct);
}
