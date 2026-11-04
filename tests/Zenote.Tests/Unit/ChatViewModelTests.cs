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

        return new ChatViewModel(Notebook, new RagAnswerService(_retriever, chat), _conversations) { SourceCount = 1 };
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
    public async Task An_uncited_answer_is_flagged_and_lists_the_pages_it_was_given()
    {
        var viewModel = CreateViewModel(new FakeChatClient("Probably twenty days."));
        viewModel.Input = "How much leave?";

        await viewModel.SendCommand.ExecuteAsync(null);

        var answer = viewModel.Messages[1];
        answer.IsUnverified.ShouldBeTrue();
        answer.ChipsHeading.ShouldBe("Pages it was given");
        answer.UnverifiedText.ShouldContain("pages below");
        answer.Chips.Single().Label.ShouldBe("a.pdf · p. 1");
    }

    [Fact]
    public void Copying_an_answer_leaves_out_citation_numbers()
    {
        var message = MessageViewModel.Pending();

        message.Complete(new Answer("Twenty days [1]. Five carry over [2][3].", false, []), _ => Task.CompletedTask);

        message.CopyText.ShouldBe("Twenty days. Five carry over.");
    }

    [Fact]
    public void Only_finished_answers_can_be_copied()
    {
        var pending = MessageViewModel.Pending();
        pending.CanCopy.ShouldBeFalse();

        pending.Complete(new Answer("Text.", false, []), _ => Task.CompletedTask);
        pending.CanCopy.ShouldBeTrue();

        var failed = MessageViewModel.Pending();
        failed.Fail("Couldn't reach Ollama.");
        failed.CanCopy.ShouldBeFalse();
    }

    [Fact]
    public async Task New_chat_is_unavailable_while_answering()
    {
        var viewModel = CreateViewModel(new FakeChatClient("Partial") { HangAfterFirstToken = true });
        viewModel.Input = "Question";

        var sending = viewModel.SendCommand.ExecuteAsync(null);
        await Task.Delay(100, TestContext.Current.CancellationToken);

        viewModel.NewChatCommand.CanExecute(null).ShouldBeFalse();

        viewModel.StopCommand.Execute(null);
        await sending;
        viewModel.NewChatCommand.CanExecute(null).ShouldBeTrue();
    }

    [Fact]
    public async Task A_missing_model_gets_its_own_message()
    {
        var answers = Substitute.For<IRagAnswerService>();
        answers.AskAsync(Notebook, Arg.Any<string>(), Arg.Any<IReadOnlyList<ChatTurn>>(), Arg.Any<CancellationToken>())
            .Returns(_ => Throwing(new HttpRequestException("model 'x' not found")));
        var viewModel = new ChatViewModel(Notebook, answers, _conversations) { SourceCount = 1, Input = "Question" };

        await viewModel.SendCommand.ExecuteAsync(null);

        viewModel.Messages[1].Text.ShouldContain("isn't installed");
    }

    [Fact]
    public void A_cited_answer_is_not_flagged()
    {
        var message = MessageViewModel.Pending();

        message.Complete(new Answer("Fact [1].", false, [new Citation(1, Guid.NewGuid(), "a.pdf", 3, "x")]), _ => Task.CompletedTask);

        message.IsUnverified.ShouldBeFalse();
        message.ChipsHeading.ShouldBe("Sources");
    }

    [Fact]
    public void An_older_saved_answer_with_no_pages_keeps_the_plain_warning()
    {
        var message = MessageViewModel.Pending();

        message.Complete(new Answer("Fact.", false, []), _ => Task.CompletedTask);

        message.IsUnverified.ShouldBeTrue();
        message.UnverifiedText.ShouldStartWith("Unverified");
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
        var viewModel = new ChatViewModel(Notebook, answers, _conversations) { SourceCount = 1, Input = "Question" };

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
        viewModel.Messages[1].IsStopped.ShouldBeTrue();
        viewModel.Messages[1].IsUnverified.ShouldBeFalse();
    }

    [Fact]
    public async Task A_client_that_ends_quietly_on_cancel_still_counts_as_stopped()
    {
        var chat = new FakeChatClient("Partial answer", "more") { EndQuietlyOnCancel = true, HangAfterFirstToken = true };
        var viewModel = CreateViewModel(chat);
        viewModel.Input = "Question";

        var sending = viewModel.SendCommand.ExecuteAsync(null);
        await Task.Delay(100, TestContext.Current.CancellationToken);
        viewModel.StopCommand.Execute(null);
        await sending;

        viewModel.Messages[1].IsStopped.ShouldBeTrue();
    }

    [Fact]
    public void Questions_need_at_least_one_source()
    {
        var viewModel = CreateViewModel(new FakeChatClient());
        viewModel.SourceCount = 0;
        viewModel.Input = "Question";

        viewModel.SendCommand.CanExecute(null).ShouldBeFalse();
        viewModel.InputHint.ShouldBe("Add a source to get started");
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
        var viewModel = new ChatViewModel(Notebook, answers, _conversations) { SourceCount = 1, Input = "Question" };

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

    private static async IAsyncEnumerable<AnswerEvent> Throwing(Exception? exception = null)
    {
        await Task.Yield();
        throw exception ?? new HttpRequestException();
#pragma warning disable CS0162
        yield break;
#pragma warning restore CS0162
    }
}
