using Shouldly;
using Zenote.App.ViewModels;
using Zenote.Core.Domain;
using Zenote.Tests.Support;

namespace Zenote.Tests.Integration;

[Trait("Category", "Integration")]
public sealed class ConversationRepositoryTests : IDisposable
{
    private readonly TestWorkspace _workspace = new();
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    public void Dispose() => _workspace.Dispose();

    private async Task<(Notebook Notebook, Conversation Conversation)> NewConversationAsync()
    {
        var notebook = await _workspace.Notebooks.CreateAsync("Work", _ct);
        return (notebook, await _workspace.Conversations.CreateAsync(notebook.Id, "Leave", _ct));
    }

    private static Message Reply(Conversation conversation, string text, params Citation[] citations) =>
        new(Guid.NewGuid(), conversation.Id, MessageRole.Assistant, text, false, citations, DateTimeOffset.UtcNow);

    [Fact]
    public async Task Messages_come_back_in_order_with_their_citations()
    {
        var (_, conversation) = await NewConversationAsync();
        var citation = new Citation(1, Guid.NewGuid(), "a.pdf", 7, "The passage.");

        await _workspace.Conversations.AppendMessageAsync(
            new Message(Guid.NewGuid(), conversation.Id, MessageRole.User, "Question", false, [], DateTimeOffset.UtcNow), _ct);
        await _workspace.Conversations.AppendMessageAsync(Reply(conversation, "Answer [1].", citation), _ct);

        var messages = await _workspace.Conversations.GetMessagesAsync(conversation.Id, _ct);

        messages.Select(m => m.Content).ShouldBe(["Question", "Answer [1]."]);
        messages[1].Citations.Single().ShouldBe(citation);
    }

    [Fact]
    public async Task Conversations_list_most_recently_used_first()
    {
        var (notebook, older) = await NewConversationAsync();
        await _workspace.Conversations.CreateAsync(notebook.Id, "Newer", _ct);
        await _workspace.Conversations.AppendMessageAsync(Reply(older, "Bumped"), _ct);

        var conversations = await _workspace.Conversations.ListAsync(notebook.Id, _ct);

        conversations.Select(c => c.Title).ShouldBe(["Leave", "Newer"]);
    }

    [Fact]
    public async Task Conversations_stay_in_their_notebook()
    {
        await NewConversationAsync();
        var other = await _workspace.Notebooks.CreateAsync("Other", _ct);

        (await _workspace.Conversations.ListAsync(other.Id, _ct)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Deleting_a_conversation_removes_its_messages()
    {
        var (_, conversation) = await NewConversationAsync();
        await _workspace.Conversations.AppendMessageAsync(Reply(conversation, "Answer"), _ct);

        await _workspace.Conversations.DeleteAsync(conversation.Id, _ct);

        _workspace.Count("messages").ShouldBe(0);
    }

    [Fact]
    public async Task Deleting_a_notebook_removes_its_conversations()
    {
        var (notebook, conversation) = await NewConversationAsync();
        await _workspace.Conversations.AppendMessageAsync(Reply(conversation, "Answer"), _ct);

        await _workspace.Library.DeleteNotebookAsync(notebook.Id, _ct);

        _workspace.Count("conversations").ShouldBe(0);
        _workspace.Count("messages").ShouldBe(0);
    }

    [Fact]
    public async Task Saved_answers_reload_with_their_chips()
    {
        var citation = new Citation(1, Guid.NewGuid(), "a.pdf", 7, "The passage.");
        var message = Reply(new Conversation(Guid.NewGuid(), Guid.NewGuid(), "t", DateTimeOffset.UtcNow), "Answer [1].", citation);

        var viewModel = MessageViewModel.FromSaved(message, _ => Task.CompletedTask);

        viewModel.Chips.Single().Label.ShouldBe("a.pdf · p. 7");
        viewModel.IsUnverified.ShouldBeFalse();
    }
}
