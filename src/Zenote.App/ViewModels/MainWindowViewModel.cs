using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Zenote.Core.Abstractions;

namespace Zenote.App.ViewModels;

public sealed partial class MainWindowViewModel(ISystemCheck systemCheck) : ObservableObject
{
    public ObservableCollection<CheckItemViewModel> Checks { get; } = [];

    [RelayCommand]
    private async Task RefreshAsync(CancellationToken ct)
    {
        var results = await systemCheck.RunAsync(ct);

        Checks.Clear();
        foreach (var result in results)
        {
            Checks.Add(new CheckItemViewModel(result));
        }
    }
}

public sealed class CheckItemViewModel(CheckResult result)
{
    public string Label { get; } = $"{(result.Ok ? "✓" : "✕")} {result.Name}";
    public string? Detail { get; } = result.Detail;
    public bool Ok { get; } = result.Ok;
    public bool Failed { get; } = !result.Ok;
}
