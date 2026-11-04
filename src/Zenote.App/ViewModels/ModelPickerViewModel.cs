using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Zenote.App.Services;
using Zenote.Core.Abstractions;

namespace Zenote.App.ViewModels;

public sealed partial class ModelPickerViewModel(IChatModels models, UserSettings settings, IModelWarmup warmup) : ObservableObject
{
    private bool _loading;

    public ObservableCollection<string> Models { get; } = [];

    [ObservableProperty]
    public partial string? Selected { get; set; }

    public bool HasChoice => Models.Count > 1;

    public async Task LoadAsync()
    {
        _loading = true;

        try
        {
            var installed = await models.ListAsync(CancellationToken.None);

            Models.Clear();
            foreach (var model in installed)
            {
                Models.Add(model);
            }

            Selected = models.Current;
        }
        catch (HttpRequestException)
        {
            // Ollama isn't running, so there's nothing to choose from. The banner says so.
        }
        finally
        {
            _loading = false;
            OnPropertyChanged(nameof(HasChoice));
        }
    }

    partial void OnSelectedChanged(string? value)
    {
        if (_loading || value is null || value == models.Current)
        {
            return;
        }

        models.Select(value);
        settings.SaveChatModel(value);
        _ = warmup.WarmUpAsync(CancellationToken.None);
    }
}
