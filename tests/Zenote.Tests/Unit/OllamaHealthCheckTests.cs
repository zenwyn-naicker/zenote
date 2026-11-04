using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using OllamaSharp;
using OllamaSharp.Models;
using Shouldly;
using Zenote.Core.Options;
using Zenote.Infrastructure.Ai;

namespace Zenote.Tests.Unit;

public class OllamaHealthCheckTests
{
    private readonly IOllamaApiClient _client = Substitute.For<IOllamaApiClient>();

    private OllamaHealthCheck CreateCheck() => new(_client, Options.Create(new ModelOptions()));

    private void GivenInstalled(params string[] names) =>
        _client.ListLocalModelsAsync(Arg.Any<CancellationToken>())
            .Returns(names.Select(name => new Model { Name = name }));

    [Fact]
    public async Task Models_listed_with_latest_tag_count_as_installed()
    {
        GivenInstalled("llama3.2:latest", "nomic-embed-text:latest");

        var health = await CreateCheck().CheckAsync(TestContext.Current.CancellationToken);

        health.Reachable.ShouldBeTrue();
        health.MissingModels.ShouldBeEmpty();
    }

    [Fact]
    public async Task Reports_models_that_are_not_installed()
    {
        GivenInstalled("llama3.2:latest");

        var health = await CreateCheck().CheckAsync(TestContext.Current.CancellationToken);

        health.MissingModels.ShouldBe(["nomic-embed-text"]);
    }

    [Fact]
    public async Task Reports_unreachable_when_Ollama_is_not_running()
    {
        _client.ListLocalModelsAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new HttpRequestException());

        var health = await CreateCheck().CheckAsync(TestContext.Current.CancellationToken);

        health.Reachable.ShouldBeFalse();
    }
}
