using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;
using Zenote.App.Services;
using Zenote.App.ViewModels;
using Zenote.Core.Abstractions;
using Zenote.Core.Domain;
using Zenote.Infrastructure.Storage;
using Microsoft.Extensions.Options;
using Zenote.Core.Options;

namespace Zenote.Tests.Unit;

public class MainWindowViewModelTests
{
    private readonly ISystemCheck _checks = Substitute.For<ISystemCheck>();
    private readonly IAiHealthCheck _health = Substitute.For<IAiHealthCheck>();
    private readonly IModelInstaller _installer = Substitute.For<IModelInstaller>();
    private readonly IExternalLauncher _launcher = Substitute.For<IExternalLauncher>();

    private MainWindowViewModel CreateViewModel()
    {
        var picker = new ModelPickerViewModel(Substitute.For<IChatModels>(), new UserSettings(Path.GetTempFileName()), Substitute.For<IModelWarmup>());

        return new MainWindowViewModel(
            _checks,
            new NotebooksViewModel(Substitute.For<INotebookRepository>(), Substitute.For<IDocumentLibrary>()),
            _ => throw new NotSupportedException(),
            Substitute.For<IIndexingQueue>(),
            Substitute.For<IModelWarmup>(),
            new SettingsViewModel(Options.Create(new ModelOptions()), picker, _health, new AppPaths("/tmp/zenote")),
            _health,
            _installer,
            _launcher);
    }

    private void Given(bool reachable, params string[] missing)
    {
        _health.CheckAsync(Arg.Any<CancellationToken>()).Returns(new AiHealth(reachable, missing));
        _checks.RunAsync(Arg.Any<CancellationToken>()).Returns(
            [new CheckResult("Ollama", reachable, reachable ? null : "Not running."), .. missing.Select(m => new CheckResult(m, false, "Missing"))]);
    }

    [Fact]
    public async Task Healthy_setup_shows_no_banner()
    {
        Given(reachable: true);
        var viewModel = CreateViewModel();

        await viewModel.RefreshCommand.ExecuteAsync(null);

        viewModel.HasProblems.ShouldBeFalse();
    }

    [Fact]
    public async Task Missing_models_offer_a_download_in_plain_language()
    {
        Given(reachable: true, "llama3.2", "nomic-embed-text");
        var viewModel = CreateViewModel();

        await viewModel.RefreshCommand.ExecuteAsync(null);

        viewModel.CanDownloadModels.ShouldBeTrue();
        viewModel.ProblemText.ShouldContain("one-time download");
        viewModel.ProblemText.ShouldNotContain("ollama pull");
    }

    [Fact]
    public async Task Downloading_installs_each_missing_model_then_clears_the_banner()
    {
        Given(reachable: true, "llama3.2", "nomic-embed-text");
        var viewModel = CreateViewModel();
        await viewModel.RefreshCommand.ExecuteAsync(null);
        _installer.InstallAsync(Arg.Any<string>(), Arg.Any<IProgress<double>>(), Arg.Any<CancellationToken>())
            .Returns(_ => { Given(reachable: true); return Task.CompletedTask; });

        await viewModel.DownloadModelsCommand.ExecuteAsync(null);

        await _installer.Received(1).InstallAsync("llama3.2", Arg.Any<IProgress<double>>(), Arg.Any<CancellationToken>());
        await _installer.Received(1).InstallAsync("nomic-embed-text", Arg.Any<IProgress<double>>(), Arg.Any<CancellationToken>());
        viewModel.HasProblems.ShouldBeFalse();
    }

    [Fact]
    public async Task A_failed_download_says_what_to_do()
    {
        Given(reachable: true, "llama3.2");
        var viewModel = CreateViewModel();
        await viewModel.RefreshCommand.ExecuteAsync(null);
        _installer.InstallAsync(Arg.Any<string>(), Arg.Any<IProgress<double>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException());

        await viewModel.DownloadModelsCommand.ExecuteAsync(null);

        viewModel.ProblemText.ShouldContain("internet connection");
        viewModel.CanDownloadModels.ShouldBeTrue();
    }

    [Fact]
    public async Task Without_Ollama_installed_the_button_opens_its_download_page()
    {
        Given(reachable: false);
        _launcher.OpenApplication("Ollama").Returns(false);
        var viewModel = CreateViewModel();
        await viewModel.RefreshCommand.ExecuteAsync(null);

        viewModel.NeedsOllama.ShouldBeTrue();
        await viewModel.OpenOllamaCommand.ExecuteAsync(null);

        _launcher.Received(1).OpenWebsite("https://ollama.com/download");
    }
}
