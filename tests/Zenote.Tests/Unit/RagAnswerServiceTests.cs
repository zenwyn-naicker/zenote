using Microsoft.Extensions.AI;
using NSubstitute;
using Shouldly;
using Zenote.Core.Abstractions;
using Zenote.Core.Answering;
using Zenote.Core.Domain;
using Zenote.Tests.Support;

namespace Zenote.Tests.Unit;

public class RagAnswerServiceTests
{
    private static readonly Guid Notebook = Guid.NewGuid();

    private readonly IRetriever _retriever = Substitute.For<IRetriever>();
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    private static ScoredChunk Chunk(int id, string text) =>
        new(new Chunk(id, Guid.NewGuid(), "handbook.pdf", id, text), 0.8);

    private void GivenRetrieval(bool relevant, params ScoredChunk[] chunks) =>
        _retriever.RetrieveAsync(Notebook, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new RetrievalResult(chunks, relevant));

    private async Task<List<AnswerEvent>> AskAsync(IChatClient chat, string question = "How much leave?", IReadOnlyList<ChatTurn>? history = null)
    {
        var events = new List<AnswerEvent>();
        await foreach (var answerEvent in new RagAnswerService(_retriever, chat).AskAsync(Notebook, question, history ?? [], _ct))
        {
            events.Add(answerEvent);
        }

        return events;
    }

    private static Answer Final(List<AnswerEvent> events) => ((AnswerCompleted)events[^1]).Answer;

    private static string Streamed(List<AnswerEvent> events) => string.Concat(events.OfType<TokenReceived>().Select(t => t.Text));

    [Fact]
    public async Task Streams_the_answer_then_completes_with_its_sources()
    {
        GivenRetrieval(true, Chunk(1, "Twenty days of leave."));

        var events = await AskAsync(new FakeChatClient("You get ", "twenty ", "days [1]."));

        Streamed(events).ShouldBe("You get twenty days [1].");
        Final(events).Text.ShouldBe("You get twenty days [1].");
        Final(events).NotFound.ShouldBeFalse();
        Final(events).Citations.Single().Page.ShouldBe(1);
    }

    [Fact]
    public async Task Weak_retrieval_skips_the_model_and_offers_closest_matches()
    {
        GivenRetrieval(false, Chunk(1, "a"), Chunk(2, "b"), Chunk(3, "c"), Chunk(4, "d"));
        var chat = new FakeChatClient("Should never be used");

        var events = await AskAsync(chat);

        chat.Calls.ShouldBe(0);
        Final(events).NotFound.ShouldBeTrue();
        Final(events).Text.ShouldBe(RagAnswerService.NotFoundText);
        Final(events).Citations.Count.ShouldBe(3);
    }

    [Theory]
    [InlineData("NOT_FOUND")]
    [InlineData("NOT", "_FOUND")]
    [InlineData("N", "OT_", "FOUND", ".")]
    [InlineData(" NOT_FOUND\n")]
    public async Task The_not_found_signal_never_reaches_the_screen(params string[] tokens)
    {
        GivenRetrieval(true, Chunk(1, "text"));

        var events = await AskAsync(new FakeChatClient(tokens));

        Streamed(events).ShouldBeEmpty();
        Final(events).NotFound.ShouldBeTrue();
    }

    [Fact]
    public async Task Text_that_only_starts_like_the_signal_still_streams()
    {
        GivenRetrieval(true, Chunk(1, "text"));

        var events = await AskAsync(new FakeChatClient("No", "t all staff qualify [1]."));

        Streamed(events).ShouldBe("Not all staff qualify [1].");
        Final(events).NotFound.ShouldBeFalse();
    }

    [Fact]
    public async Task Invented_source_numbers_are_removed()
    {
        GivenRetrieval(true, Chunk(1, "text"));

        var events = await AskAsync(new FakeChatClient("Twenty days [1] and more [7]."));

        Final(events).Text.ShouldBe("Twenty days [1] and more.");
        Final(events).Citations.Single().Marker.ShouldBe(1);
    }

    [Fact]
    public async Task An_empty_reply_counts_as_not_found()
    {
        GivenRetrieval(true, Chunk(1, "text"));

        Final(await AskAsync(new FakeChatClient())).NotFound.ShouldBeTrue();
    }

    [Fact]
    public async Task Sends_numbered_sources_with_file_and_page_at_low_temperature()
    {
        GivenRetrieval(true, Chunk(1, "First passage."), Chunk(2, "Second passage."));
        var chat = new FakeChatClient("Answer [1].");

        await AskAsync(chat, "How much leave?");

        var prompt = chat.ReceivedMessages[^1].Text;
        prompt.ShouldContain("[1] handbook.pdf, page 1\nFirst passage.");
        prompt.ShouldContain("[2] handbook.pdf, page 2\nSecond passage.");
        prompt.ShouldEndWith("Question: How much leave?");
        chat.ReceivedOptions!.Temperature.ShouldBe(0.1f);
    }

    [Fact]
    public async Task Follow_ups_search_with_the_previous_question_too()
    {
        GivenRetrieval(true, Chunk(1, "text"));
        ChatTurn[] history =
        [
            new(MessageRole.User, "How much annual leave?"),
            new(MessageRole.Assistant, "Twenty days [1]."),
        ];

        await AskAsync(new FakeChatClient("Yes [1]."), "Can I carry it over?", history);

        await _retriever.Received(1).RetrieveAsync(Notebook, "How much annual leave? Can I carry it over?", Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Only_recent_history_goes_in_the_prompt()
    {
        var history = Enumerable.Range(1, 20).Select(i => new ChatTurn(i % 2 == 1 ? MessageRole.User : MessageRole.Assistant, $"turn {i}")).ToList();

        var messages = PromptBuilder.Build("Question?", history, []);

        messages.Count.ShouldBe(10);
        messages[1].Text.ShouldBe("turn 13");
    }
}
