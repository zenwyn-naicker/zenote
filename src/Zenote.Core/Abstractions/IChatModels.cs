namespace Zenote.Core.Abstractions;

public interface IChatModels
{
    string Current { get; }

    /// The installed models that can chat, leaving out embedding models.
    Task<IReadOnlyList<string>> ListAsync(CancellationToken ct);

    /// Takes effect from the next question.
    void Select(string model);
}
