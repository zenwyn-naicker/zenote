using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Options;
using Zenote.Core.Abstractions;
using Zenote.Core.Options;
using Zenote.Infrastructure.Storage;

namespace Zenote.App.ViewModels;

public sealed partial class SettingsViewModel(
    IOptions<ModelOptions> options,
    ModelPickerViewModel modelPicker,
    IAiHealthCheck health,
    AppPaths paths) : ObservableObject
{
    public ModelPickerViewModel ModelPicker { get; } = modelPicker;

    public string SearchModel { get; } = options.Value.EmbeddingModel;

    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConnectionText))]
    public partial bool IsConnected { get; set; }

    public string ConnectionText => IsConnected ? "Connected" : "Not running. Open Ollama from Applications.";

    [RelayCommand]
    private async Task OpenAsync()
    {
        IsOpen = true;
        IsConnected = (await health.CheckAsync(CancellationToken.None)).Reachable;

        if (IsConnected)
        {
            await ModelPicker.LoadAsync();
        }
    }

    [RelayCommand]
    private void Close() => IsOpen = false;

    [RelayCommand]
    private void ShowDataFolder() => Process.Start(new ProcessStartInfo("open") { ArgumentList = { paths.Root } });
}
