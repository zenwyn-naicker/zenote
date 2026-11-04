using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Options;
using Zenote.App.Services;
using Zenote.Core.Abstractions;
using Zenote.Core.Options;
using Zenote.Infrastructure.Storage;

namespace Zenote.App.ViewModels;

public sealed partial class SettingsViewModel(
    UserSettings settings,
    IOptions<ModelOptions> options,
    ModelPickerViewModel modelPicker,
    IAiHealthCheck health,
    AppPaths paths) : ObservableObject
{
    public ModelPickerViewModel ModelPicker { get; } = modelPicker;

    public string AboutText { get; } = $"Zenote 1.0  ·  Search model: {options.Value.EmbeddingModel}";

    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConnectionText))]
    public partial bool IsConnected { get; set; }

    [ObservableProperty]
    public partial bool IsEditingAddress { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConnectionText))]
    public partial string OllamaUrl { get; set; } = options.Value.OllamaUrl;

    [ObservableProperty]
    public partial string? Message { get; set; }

    [ObservableProperty]
    public partial bool IsSaved { get; set; }

    public string ConnectionText => IsConnected
        ? $"Connected at {OllamaUrl}"
        : "Not running. Open Ollama from Applications.";

    [RelayCommand]
    private async Task OpenAsync()
    {
        Message = null;
        IsSaved = false;
        IsEditingAddress = false;
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
    private void ToggleAddress() => IsEditingAddress = !IsEditingAddress;

    [RelayCommand]
    private void ShowDataFolder() => Process.Start(new ProcessStartInfo("open") { ArgumentList = { paths.Root } });

    [RelayCommand]
    private void Save()
    {
        if (!Uri.TryCreate(OllamaUrl.Trim(), UriKind.Absolute, out var url) || url.Scheme is not ("http" or "https"))
        {
            Message = "Enter a full address, like http://localhost:11434.";
            return;
        }

        settings.SaveOllamaUrl(OllamaUrl.Trim());
        IsSaved = true;
        Message = "Saved. Restart Zenote to use this address.";
    }
}
