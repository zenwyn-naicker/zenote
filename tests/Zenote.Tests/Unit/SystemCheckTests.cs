using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Zenote.Core.Abstractions;
using Zenote.Core.Options;
using Zenote.Infrastructure;

namespace Zenote.Tests.Unit;

public class SystemCheckTests
{
    private static Task<IReadOnlyList<CheckResult>> RunWith(AiHealth health)
    {
        var aiHealth = Substitute.For<IAiHealthCheck>();
        aiHealth.CheckAsync(Arg.Any<CancellationToken>()).Returns(health);

        var renderer = Substitute.For<IPageRenderer>();
        renderer.RenderPngAsync(default!, default, default, default).ReturnsForAnyArgs([1]);

        return new SystemCheck(aiHealth, renderer, Options.Create(new ModelOptions())).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Suggests_pulling_a_model_that_is_missing()
    {
        var results = await RunWith(new AiHealth(true, ["llama3.2"]));

        results.Single(r => r.Name == "llama3.2").Detail.ShouldBe("Run: ollama pull llama3.2");
    }

    [Fact]
    public async Task Does_not_suggest_pulling_models_when_Ollama_is_down()
    {
        var results = await RunWith(new AiHealth(false, ["llama3.2", "nomic-embed-text"]));

        results.Single(r => r.Name == "Ollama").Ok.ShouldBeFalse();
        results.Single(r => r.Name == "llama3.2").Detail.ShouldBeNull();
    }
}
