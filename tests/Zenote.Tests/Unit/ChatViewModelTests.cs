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
    private readonly IConversationRepository _conversations = Substitute.For<IConversationRepository>();

    public ChatViewModelTests() =>
        _conversations.CreateAsync(Notebook, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => new Conversation(Guid.NewGuid(), Notebook, call.ArgAt<string>(1), DateTimeOffset.UtcNow));

    private ChatViewModel CreateViewModel(FakeChatClient chat)
    {
        var chunk = new ScoredChunk(new Chunk(1, Guid.NewGuid(), "a.pdf", 1, "text"), 0.8);
        _retriever.RetrieveAsync(Notebook, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new RetrievalResult([chunk], true));

        return new ChatViewModel(Notebook, new RagAnswerService(_retriever, chat), _conversations);
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
    public async Task Citations_become_chips_that_open_the_source()
    {
        Citation? opened = null;
        var viewModel = CreateViewModel(new FakeChatClient("Twenty days [1]."));
        viewModel.OpenCitation = citation => { opened = citation; return Task.CompletedTask; };
        viewModel.Input = "How much leave?";

        await viewModel.SendCommand.ExecuteAsync(null);

        var chip = viewModel.Messages[1].Chips.Single();
        chip.Label.ShouldBe("a.pdf · p. 1");
        await chip.OpenCommand.ExecuteAsync(null);
        opened!.Page.ShouldBe(1);
    }

    [Fact]
    public async Task An_answer_with_no_citations_is_marked_unverified()
    {
        var viewModel = CreateViewModel(new FakeChatClient("Probably twenty days."));
        viewModel.Input = "How much leave?";

        await viewModel.SendCommand.ExecuteAsync(null);

        viewModel.Messages[1].IsUnverified.ShouldBeTrue();
        viewModel.Messages[1].HasChips.ShouldBeFalse();
    }

    [Fact]
    public void Two_citations_to_one_page_share_a_chip()
    {
        var document = Guid.NewGuid();
        var message = MessageViewModel.Pending();

        message.Complete(
            new Answer("Text [1][2].", false, [new Citation(1, document, "a.pdf", 3, "x"), new Citation(2, document, "a.pdf", 3, "y")]),
            _ => Task.CompletedTask);

        message.Chips.Count.ShouldBe(1);
    }

    [Fact]
    public async Task The_first_question_starts_a_titled_conversation()
    {
        var viewModel = CreateViewModel(new FakeChatClient("Answer [1]."));
        viewModel.Input = "How much leave do I get?";

        await viewModel.SendCommand.ExecuteAsync(null);

        viewModel.Conversations.Single().Title.ShouldBe("How much leave do I get?");
        viewModel.SelectedConversation.ShouldBe(viewModel.Conversations[0]);
        await _conversations.Received(2).AppendMessageAsync(Arg.Any<Message>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Long_first_questions_are_shortened_for_the_title()
    {
        var viewModel = CreateViewModel(new FakeChatClient("Answer [1]."));
        viewModel.Input = new string('a', 100);

        await viewModel.SendCommand.ExecuteAsync(null);

        viewModel.Conversations.Single().Title.Length.ShouldBe(61);
    }

    [Fact]
    public async Task Later_questions_stay_in_the_same_conversation()
    {
        var viewModel = CreateViewModel(new FakeChatClient("Answer [1]."));
        viewModel.Input = "First";
        await viewModel.SendCommand.ExecuteAsync(null);
        viewModel.Input = "Second";

        await viewModel.SendCommand.ExecuteAsync(null);

        viewModel.Conversations.Count.ShouldBe(1);
        viewModel.Messages.Count.ShouldBe(4);
    }

    [Fact]
    public async Task New_chat_clears_the_screen_and_starts_fresh_on_the_next_question()
    {
        var chat = new FakeChatClient("Answer [1].");
        var viewModel = CreateViewModel(chat);
        viewModel.Input = "First";
        await viewModel.SendCommand.ExecuteAsync(null);

        await viewModel.NewChatCommand.ExecuteAsync(null);
        viewModel.Messages.ShouldBeEmpty();

        viewModel.Input = "Unrelated";
        await viewModel.SendCommand.ExecuteAsync(null);

        viewModel.Conversations.Count.ShouldBe(2);
        chat.ReceivedMessages.ShouldNotContain(m => m.Text == "First");
    }

    [Fact]
    public async Task Failed_replies_are_not_saved()
    {
        var answers = Substitute.For<IRagAnswerService>();
        answers.AskAsync(Notebook, Arg.Any<string>(), Arg.Any<IReadOnlyList<ChatTurn>>(), Arg.Any<CancellationToken>())
            .Returns(_ => Throwing());
        var viewModel = new ChatViewModel(Notebook, answers, _conversations) { Input = "Question" };

        await viewModel.SendCommand.ExecuteAsync(null);

        await _conversations.Received(1).AppendMessageAsync(
            Arg.Is<Message>(m => m.Role == MessageRole.User), Arg.Any<CancellationToken>());
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
        var viewModel = new ChatViewModel(Notebook, answers, _conversations) { Input = "Question" };

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
