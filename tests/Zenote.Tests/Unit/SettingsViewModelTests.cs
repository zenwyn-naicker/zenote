using System.Text.Json;
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
        var settings = new UserSettings(_path);
        var picker = new ModelPickerViewModel(_models, settings, Substitute.For<IModelWarmup>());
        return new SettingsViewModel(settings, Options.Create(new ModelOptions()), picker, _health, new AppPaths("/tmp/zenote"));
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
    public void Starts_with_the_current_address()
    {
        CreateViewModel().OllamaUrl.ShouldBe("http://localhost:11434");
    }

    [Fact]
    public void Saves_in_the_shape_the_app_reads_back()
    {
        var viewModel = CreateViewModel();
        viewModel.OllamaUrl = " http://localhost:9999 ";

        viewModel.SaveCommand.Execute(null);

        using var saved = JsonDocument.Parse(File.ReadAllText(_path));
        saved.RootElement.GetProperty("Models").GetProperty("OllamaUrl").GetString().ShouldBe("http://localhost:9999");
        viewModel.IsSaved.ShouldBeTrue();
        viewModel.Message!.ShouldContain("Restart");
    }

    [Fact]
    public void Saving_one_setting_keeps_the_other()
    {
        var settings = new UserSettings(_path);

        settings.SaveChatModel("gemma4:e4b");
        settings.SaveOllamaUrl("http://localhost:9999");

        using var saved = JsonDocument.Parse(File.ReadAllText(_path));
        saved.RootElement.GetProperty("Models").GetProperty("ChatModel").GetString().ShouldBe("gemma4:e4b");
    }

    [Theory]
    [InlineData("localhost")]
    [InlineData("ftp://localhost:11434")]
    [InlineData("")]
    public void Rejects_an_address_that_is_not_a_web_url(string url)
    {
        var viewModel = CreateViewModel();
        viewModel.OllamaUrl = url;

        viewModel.SaveCommand.Execute(null);

        File.Exists(_path).ShouldBeFalse();
        viewModel.IsSaved.ShouldBeFalse();
    }
}
