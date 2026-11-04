namespace Zenote.App.Services;

public interface IFilePicker
{
    Task<IReadOnlyList<string>> PickPdfsAsync();
}
