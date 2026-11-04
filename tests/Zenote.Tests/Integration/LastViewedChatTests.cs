using NSubstitute;
using Shouldly;
using Zenote.App.ViewModels;
using Zenote.Core.Abstractions;
using Zenote.Core.Answering;
using Zenote.Core.Domain;
using Zenote.Tests.Support;

namespace Zenote.Tests.Integration;

[Trait("Category", "Integration")]
public sealed class LastViewedChatTests : IDisposable
{
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;
    private readonly TestWorkspace _workspace = new();

    public void Dispose() => _workspace.Dispose();

    private ChatViewModel OpenChat(Guid notebookId, params string[] reply) => new(
        notebookId,
        new RagAnswerService(RetrieverWithOneChunk(notebookId), new FakeChatClient(reply)),
        _workspace.Conversations)
    { SourceCount = 1 };

    private static IRetriever RetrieverWithOneChunk(Guid notebookId)
    {
        var retriever = Substitute.For<IRetriever>();
        var chunk = new ScoredChunk(new Chunk(1, Guid.NewGuid(), "a.pdf", 1, "text"), 0.8);
        retriever.RetrieveAsync(notebookId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new RetrievalResult([chunk], true));
        return retriever;
    }

    private async Task<(Notebook Notebook, Conversation Older, Conversation Newer)> TwoChatsAsync()
    {
        var notebook = await _workspace.Notebooks.CreateAsync("Notes", _ct);
        var older = await _workspace.Conversations.CreateAsync(notebook.Id, "Older", _ct);
        await Task.Delay(5, _ct);
        var newer = await _workspace.Conversations.CreateAsync(notebook.Id, "Newer", _ct);
        return (notebook, older, newer);
    }

    private static ConversationItemViewModel Find(ChatViewModel chat, Conversation conversation) =>
        chat.Conversations.Single(c => c.Conversation.Id == conversation.Id);

    [Fact]
    public async Task A_new_notebook_opens_on_the_newest_chat()
    {
        var (notebook, _, newer) = await TwoChatsAsync();

        var chat = OpenChat(notebook.Id);
        await chat.LoadAsync();

        chat.SelectedConversation!.Conversation.Id.ShouldBe(newer.Id);
    }

    [Fact]
    public async Task Coming_back_opens_the_chat_you_were_viewing()
    {
        var (notebook, older, _) = await TwoChatsAsync();
        var first = OpenChat(notebook.Id);
        await first.LoadAsync();

        await first.ShowAsync(Find(first, older));

        var second = OpenChat(notebook.Id);
        await second.LoadAsync();
        second.SelectedConversation!.Conversation.Id.ShouldBe(older.Id);
    }

    [Fact]
    public async Task Coming_back_opens_a_chat_you_just_started()
    {
        var (notebook, older, _) = await TwoChatsAsync();
        var first = OpenChat(notebook.Id, "Answer [1].");
        await first.LoadAsync();
        await first.ShowAsync(Find(first, older));

        await first.NewChatCommand.ExecuteAsync(null);
        first.Input = "A new question";
        await first.SendCommand.ExecuteAsync(null);

        var second = OpenChat(notebook.Id);
        await second.LoadAsync();
        second.SelectedConversation!.Title.ShouldBe("A new question");
    }

    [Fact]
    public async Task Deleting_the_chat_you_were_viewing_falls_back_to_the_newest()
    {
        var (notebook, older, newer) = await TwoChatsAsync();
        await _workspace.Conversations.SetLastViewedAsync(notebook.Id, older.Id, _ct);

        await _workspace.Conversations.DeleteAsync(older.Id, _ct);

        (await _workspace.Conversations.GetLastViewedAsync(notebook.Id, _ct)).ShouldBeNull();
        var chat = OpenChat(notebook.Id);
        await chat.LoadAsync();
        chat.SelectedConversation!.Conversation.Id.ShouldBe(newer.Id);
    }

    [Fact]
    public async Task Each_notebook_remembers_its_own_chat()
    {
        var (notebook, older, _) = await TwoChatsAsync();
        var (other, _, otherNewer) = await TwoChatsAsync();

        await _workspace.Conversations.SetLastViewedAsync(notebook.Id, older.Id, _ct);
        await _workspace.Conversations.SetLastViewedAsync(other.Id, otherNewer.Id, _ct);

        (await _workspace.Conversations.GetLastViewedAsync(notebook.Id, _ct)).ShouldBe(older.Id);
        (await _workspace.Conversations.GetLastViewedAsync(other.Id, _ct)).ShouldBe(otherNewer.Id);
    }

    [Fact]
    public async Task A_notebook_with_a_remembered_chat_can_still_be_deleted()
    {
        var (notebook, older, _) = await TwoChatsAsync();
        await _workspace.Conversations.SetLastViewedAsync(notebook.Id, older.Id, _ct);

        await _workspace.Notebooks.DeleteAsync(notebook.Id, _ct);

        (await _workspace.Notebooks.ListAsync(_ct)).ShouldBeEmpty();
    }
}
