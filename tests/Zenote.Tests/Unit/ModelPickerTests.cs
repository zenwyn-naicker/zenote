using System.Text.Json;
using Microsoft.Extensions.AI;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using OllamaSharp;
using OllamaSharp.Models;
using Shouldly;
using Zenote.App.Services;
using Zenote.App.ViewModels;
using Zenote.Core.Abstractions;
using Zenote.Infrastructure.Ai;
using Zenote.Tests.Support;

namespace Zenote.Tests.Unit;

public sealed class ModelPickerTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"zenote-picker-{Guid.NewGuid():N}.json");
    private readonly IChatModels _models = Substitute.For<IChatModels>();
    private readonly IModelWarmup _warmup = Substitute.For<IModelWarmup>();

    public ModelPickerTests()
    {
        _models.Current.Returns("llama3.2");
        _models.ListAsync(Arg.Any<CancellationToken>()).Returns(["gemma4:e4b", "llama3.2"]);
    }

    public void Dispose() => File.Delete(_path);

    private ModelPickerViewModel CreateViewModel() => new(_models, new UserSettings(_path), _warmup);

    [Fact]
    public async Task Loading_shows_the_models_without_changing_anything()
    {
        var viewModel = CreateViewModel();

        await viewModel.LoadAsync();

        viewModel.Models.ShouldBe(["gemma4:e4b", "llama3.2"]);
        viewModel.Selected.ShouldBe("llama3.2");
        viewModel.HasChoice.ShouldBeTrue();
        _models.DidNotReceive().Select(Arg.Any<string>());
        File.Exists(_path).ShouldBeFalse();
    }

    [Fact]
    public async Task Choosing_a_model_switches_remembers_and_warms_it_up()
    {
        var viewModel = CreateViewModel();
        await viewModel.LoadAsync();

        viewModel.Selected = "gemma4:e4b";

        _models.Received(1).Select("gemma4:e4b");
        await _warmup.Received(1).WarmUpAsync(Arg.Any<CancellationToken>());
        using var saved = JsonDocument.Parse(File.ReadAllText(_path));
        saved.RootElement.GetProperty("Models").GetProperty("ChatModel").GetString().ShouldBe("gemma4:e4b");
    }

    [Fact]
    public async Task One_installed_model_needs_no_dropdown()
    {
        _models.ListAsync(Arg.Any<CancellationToken>()).Returns(["llama3.2"]);
        var viewModel = CreateViewModel();

        await viewModel.LoadAsync();

        viewModel.HasChoice.ShouldBeFalse();
    }

    [Fact]
    public async Task Ollama_being_down_leaves_the_dropdown_hidden()
    {
        _models.ListAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new HttpRequestException());
        var viewModel = CreateViewModel();

        await viewModel.LoadAsync();

        viewModel.HasChoice.ShouldBeFalse();
    }

    [Fact]
    public async Task Embedding_and_cloud_models_are_not_offered()
    {
        var client = Substitute.For<IOllamaApiClient>();
        client.ListLocalModelsAsync(Arg.Any<CancellationToken>()).Returns(
        [
            new Model { Name = "nomic-embed-text:latest" },
            new Model { Name = "llama3.2:latest" },
            new Model { Name = "qwen3.5:4b" },
            new Model { Name = "gpt-oss:120b-cloud" },
        ]);
        var chat = new SwitchableChatClient(_ => new FakeChatClient(), "llama3.2");

        var models = await new OllamaChatModels(client, chat).ListAsync(TestContext.Current.CancellationToken);

        models.ShouldBe(["llama3.2", "qwen3.5:4b"]);
    }

    [Fact]
    public async Task Switching_sends_later_questions_to_the_new_model()
    {
        var first = new FakeChatClient("one");
        var second = new FakeChatClient("two");
        var chat = new SwitchableChatClient(name => name == "a" ? first : second, "a");

        chat.Switch("b");
        await foreach (var _ in chat.GetStreamingResponseAsync([], cancellationToken: TestContext.Current.CancellationToken))
        {
        }

        first.Calls.ShouldBe(0);
        second.Calls.ShouldBe(1);
        chat.Model.ShouldBe("b");
    }
}
