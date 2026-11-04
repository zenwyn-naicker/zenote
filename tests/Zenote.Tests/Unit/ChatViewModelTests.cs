using NSubstitute;
using Shouldly;
using Zenote.App.ViewModels;
using Zenote.Core.Abstractions;
using Zenote.Core.Answering;
using Zenote.Core.Domain;
using Zenote.Tests.Support;

namespace Zenote.Tests.Unit;

public class ChatViewModelTests
{
    private static readonly Guid Notebook = Guid.NewGuid();

    private readonly IRetriever _retriever = Substitute.For<IRetriever>();

    private ChatViewModel CreateViewModel(FakeChatClient chat)
    {
        var chunk = new ScoredChunk(new Chunk(1, Guid.NewGuid(), "a.pdf", 1, "text"), 0.8);
        _retriever.RetrieveAsync(Notebook, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new RetrievalResult([chunk], true));

        return new ChatViewModel(Notebook, new RagAnswerService(_retriever, chat));
    }

    [Fact]
    public async Task Sending_adds_the_question_and_the_finished_answer()
    {
        var viewModel = CreateViewModel(new FakeChatClient("Twenty ", "days [1]."));
        viewModel.Input = "How much leave?";

        await viewModel.SendCommand.ExecuteAsync(null);

        viewModel.Messages.Select(m => m.Text).ShouldBe(["How much leave?", "Twenty days [1]."]);
        viewModel.Input.ShouldBeEmpty();
        viewModel.IsAnswering.ShouldBeFalse();
        viewModel.Messages[1].IsPending.ShouldBeFalse();
    }

    [Fact]
    public void Blank_questions_cannot_be_sent()
    {
        var viewModel = CreateViewModel(new FakeChatClient());
        viewModel.Input = "   ";

        viewModel.SendCommand.CanExecute(null).ShouldBeFalse();
    }

    [Fact]
    public async Task Stop_keeps_what_has_arrived_so_far()
    {
        var chat = new FakeChatClient("Partial answer", "never arrives") { HangAfterFirstToken = true };
        var viewModel = CreateViewModel(chat);
        viewModel.Input = "Question";

        var sending = viewModel.SendCommand.ExecuteAsync(null);
        await Task.Delay(100, TestContext.Current.CancellationToken);
        viewModel.StopCommand.Execute(null);
        await sending;

        viewModel.IsAnswering.ShouldBeFalse();
        viewModel.Messages[1].IsPending.ShouldBeFalse();
        viewModel.Messages[1].Text.ShouldBe("Partial answer");
    }

    [Fact]
    public async Task A_stopped_answer_with_no_text_says_so()
    {
        var viewModel = CreateViewModel(new FakeChatClient("NOT", "_FOUND") { HangAfterFirstToken = true });
        viewModel.Input = "Question";

        var sending = viewModel.SendCommand.ExecuteAsync(null);
        await Task.Delay(100, TestContext.Current.CancellationToken);
        viewModel.StopCommand.Execute(null);
        await sending;

        viewModel.Messages[1].Text.ShouldBe("Stopped.");
    }

    [Fact]
    public async Task Ollama_being_down_shows_a_helpful_message()
    {
        var answers = Substitute.For<IRagAnswerService>();
        answers.AskAsync(Notebook, Arg.Any<string>(), Arg.Any<IReadOnlyList<ChatTurn>>(), Arg.Any<CancellationToken>())
            .Returns(_ => Throwing());
        var viewModel = new ChatViewModel(Notebook, answers) { Input = "Question" };

        await viewModel.SendCommand.ExecuteAsync(null);

        viewModel.Messages[1].IsFailed.ShouldBeTrue();
        viewModel.Messages[1].Text.ShouldContain("Ollama");
    }

    [Fact]
    public async Task Failed_replies_are_left_out_of_later_history()
    {
        var chat = new FakeChatClient("Answer [1].");
        var viewModel = CreateViewModel(chat);
        viewModel.Messages.Add(MessageViewModel.FromUser("Earlier"));
        var failed = MessageViewModel.Pending();
        failed.Fail("Couldn't reach Ollama.");
        viewModel.Messages.Add(failed);
        viewModel.Input = "Next";

        await viewModel.SendCommand.ExecuteAsync(null);

        chat.ReceivedMessages.ShouldNotContain(m => m.Text.Contains("Couldn't reach"));
    }

    private static async IAsyncEnumerable<AnswerEvent> Throwing()
    {
        await Task.Yield();
        throw new HttpRequestException();
#pragma warning disable CS0162
        yield break;
#pragma warning restore CS0162
    }
}
