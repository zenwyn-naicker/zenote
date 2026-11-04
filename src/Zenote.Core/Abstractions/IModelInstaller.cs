namespace Zenote.Core.Abstractions;

public interface IModelInstaller
{
    /// <param name="progress">Percent complete, from 0 to 100.</param>
    Task InstallAsync(string model, IProgress<double> progress, CancellationToken ct);
}
