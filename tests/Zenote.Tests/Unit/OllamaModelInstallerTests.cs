using NSubstitute;
using OllamaSharp;
using OllamaSharp.Models;
using Shouldly;
using Zenote.Infrastructure.Ai;

namespace Zenote.Tests.Unit;

public class OllamaModelInstallerTests
{
    private sealed class Recorder : IProgress<double>
    {
        public List<double> Reports { get; } = [];

        public void Report(double value) => Reports.Add(value);
    }

    private static async IAsyncEnumerable<PullModelResponse?> Stream(params PullModelResponse[] updates)
    {
        foreach (var update in updates)
        {
            await Task.Yield();
            yield return update;
        }
    }

    private static PullModelResponse Piece(string digest, long completed, long total) =>
        new() { Digest = digest, Completed = completed, Total = total, Status = "pulling" };

    private static async Task<List<double>> InstallAsync(params PullModelResponse[] updates)
    {
        var client = Substitute.For<IOllamaApiClient>();
        client.PullModelAsync(Arg.Any<PullModelRequest>(), Arg.Any<CancellationToken>()).Returns(Stream(updates));
        var recorder = new Recorder();

        await new OllamaModelInstaller(client).InstallAsync("llama3.2", recorder, TestContext.Current.CancellationToken);

        return recorder.Reports;
    }

    [Fact]
    public async Task Progress_never_drops_when_a_new_piece_starts()
    {
        var reports = await InstallAsync(
            Piece("big", 500, 1000),
            Piece("big", 1000, 1000),
            Piece("small", 0, 100),
            Piece("small", 100, 100));

        reports.ShouldBe(reports.Order());
        reports[^1].ShouldBe(100);
    }

    [Fact]
    public async Task Status_updates_without_a_size_are_ignored()
    {
        var reports = await InstallAsync(
            Piece("big", 1000, 1000),
            new PullModelResponse { Status = "verifying sha256 digest" },
            new PullModelResponse { Status = "writing manifest" });

        reports.ShouldBe([100.0]);
    }
}
