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
    private readonly IAppearance _appearance = Substitute.For<IAppearance>();

    private SettingsViewModel CreateViewModel()
    {
        var settings = new UserSettings(_path);
        var picker = new ModelPickerViewModel(_models, settings, Substitute.For<IModelWarmup>());
        return new SettingsViewModel(Options.Create(new ModelOptions()), picker, _health, new AppPaths("/tmp/zenote"), settings, _appearance);
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

    [Theory]
    [InlineData("{ this is not json")]
    [InlineData("[1, 2, 3]")]
    public void A_damaged_settings_file_is_detected_and_replaced_on_save(string damaged)
    {
        File.WriteAllText(_path, damaged);
        var settings = new UserSettings(_path);

        settings.IsReadable.ShouldBeFalse();

        settings.SaveChatModel("llama3.2");

        settings.IsReadable.ShouldBeTrue();
    }

    [Fact]
    public void Having_no_settings_file_is_fine()
    {
        new UserSettings(_path).IsReadable.ShouldBeTrue();
    }

    [Fact]
    public void Appearance_follows_the_system_by_default()
    {
        CreateViewModel().Appearance.ShouldBe("System");
    }

    [Fact]
    public void Choosing_an_appearance_applies_it_and_remembers_it()
    {
        var viewModel = CreateViewModel();

        viewModel.Appearance = "Dark";

        _appearance.Received(1).Apply("Dark");
        CreateViewModel().Appearance.ShouldBe("Dark");
    }

    [Fact]
    public void Saving_a_model_keeps_the_appearance()
    {
        var settings = new UserSettings(_path);

        settings.SaveAppearance("Dark");
        settings.SaveChatModel("llama3.2");

        settings.Appearance.ShouldBe("Dark");
    }

    [Fact]
    public void Shows_the_search_model()
    {
        CreateViewModel().SearchModel.ShouldBe("nomic-embed-text");
    }
}
