using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Zenote.App.Services;
using Zenote.App.ViewModels;
using Zenote.Core.Abstractions;
using Zenote.Core.Options;
using Zenote.Infrastructure.Storage;

namespace Zenote.Tests.Unit;

public sealed class SettingsViewModelTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"zenote-settings-{Guid.NewGuid():N}.json");

    public void Dispose() => File.Delete(_path);

    private readonly IAiHealthCheck _health = Substitute.For<IAiHealthCheck>();
    private readonly IChatModels _models = Substitute.For<IChatModels>();

    private SettingsViewModel CreateViewModel()
    {
        var picker = new ModelPickerViewModel(_models, new UserSettings(_path), Substitute.For<IModelWarmup>());
        return new SettingsViewModel(Options.Create(new ModelOptions()), picker, _health, new AppPaths("/tmp/zenote"));
    }

    [Fact]
    public async Task Opening_shows_the_connection_and_installed_models()
    {
        _health.CheckAsync(Arg.Any<CancellationToken>()).Returns(new AiHealth(true, []));
        _models.Current.Returns("llama3.2");
        _models.ListAsync(Arg.Any<CancellationToken>()).Returns(["llama3.2", "gemma4:e4b"]);
        var viewModel = CreateViewModel();

        await viewModel.OpenCommand.ExecuteAsync(null);

        viewModel.IsOpen.ShouldBeTrue();
        viewModel.IsConnected.ShouldBeTrue();
        viewModel.ConnectionText.ShouldStartWith("Connected");
        viewModel.ModelPicker.Models.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Opening_says_when_Ollama_is_not_running()
    {
        _health.CheckAsync(Arg.Any<CancellationToken>()).Returns(new AiHealth(false, ["llama3.2"]));
        var viewModel = CreateViewModel();

        await viewModel.OpenCommand.ExecuteAsync(null);

        viewModel.IsConnected.ShouldBeFalse();
        viewModel.ConnectionText.ShouldContain("Not running");
    }

    [Fact]
    public void Shows_the_search_model()
    {
        CreateViewModel().SearchModel.ShouldBe("nomic-embed-text");
    }
}
