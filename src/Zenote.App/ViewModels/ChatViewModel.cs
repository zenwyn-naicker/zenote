using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Zenote.Core.Abstractions;
using Zenote.Core.Domain;

namespace Zenote.App.ViewModels;

public sealed partial class ChatViewModel(
    Guid notebookId,
    IRagAnswerService answers,
    IConversationRepository conversations) : ObservableObject
{
    // Updating the UI per token would flood it, so streamed text is flushed in small batches.
    private static readonly TimeSpan FlushInterval = TimeSpan.FromMilliseconds(40);

    private const int MaxTitleLength = 60;

    private CancellationTokenSource? _answering;
    private bool _switching;

    public Func<Citation, Task>? OpenCitation { get; set; }

    public ObservableCollection<MessageViewModel> Messages { get; } = [];

    public ObservableCollection<ConversationItemViewModel> Conversations { get; } = [];

    [ObservableProperty]
    public partial ConversationItemViewModel? SelectedConversation { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    public partial string Input { get; set; } = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand), nameof(StopCommand))]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    public partial bool IsAnswering { get; set; }

    public bool IsIdle => !IsAnswering;

    private bool CanSend => !IsAnswering && !string.IsNullOrWhiteSpace(Input);

    public async Task LoadAsync()
    {
        var saved = await conversations.ListAsync(notebookId, CancellationToken.None);

        Conversations.Clear();
        foreach (var conversation in saved)
        {
            Conversations.Add(new ConversationItemViewModel(conversation, this));
        }

        if (Conversations.Count > 0)
        {
            await ShowAsync(Conversations[0]);
        }
    }

    public async Task ShowAsync(ConversationItemViewModel? item)
    {
        _switching = true;
        SelectedConversation = item;
        _switching = false;

        Messages.Clear();

        if (item is null)
        {
            return;
        }

        foreach (var message in await conversations.GetMessagesAsync(item.Conversation.Id, CancellationToken.None))
        {
            Messages.Add(MessageViewModel.FromSaved(message, OpenAsync));
        }
    }

    internal async Task RenameAsync(ConversationItemViewModel item, string title)
    {
        await conversations.RenameAsync(item.Conversation.Id, title, CancellationToken.None);
        item.Title = title;
    }

    internal async Task DeleteAsync(ConversationItemViewModel item)
    {
        await conversations.DeleteAsync(item.Conversation.Id, CancellationToken.None);
        Conversations.Remove(item);

        if (SelectedConversation == item)
        {
            await ShowAsync(null);
        }
    }

    partial void OnSelectedConversationChanged(ConversationItemViewModel? value)
    {
        if (!_switching)
        {
            _ = ShowAsync(value);
        }
    }

    [RelayCommand]
    private Task NewChatAsync() => ShowAsync(null);

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        var question = Input.Trim();
        var history = Messages.Where(m => !m.IsFailed).Select(m => m.ToTurn()).ToList();
        var conversation = await EnsureConversationAsync(question);

        Input = "";
        Messages.Add(MessageViewModel.FromUser(question));
        await SaveAsync(conversation, MessageRole.User, question, notFound: false, []);

        var reply = MessageViewModel.Pending();
        Messages.Add(reply);

        IsAnswering = true;
        _answering = new CancellationTokenSource();

        try
        {
            await StreamAnswerAsync(question, history, reply, _answering.Token);
        }
        catch (OperationCanceledException)
        {
            reply.Stop();
        }
        catch (HttpRequestException)
        {
            reply.Fail("Couldn't reach Ollama. Open it, then try again.");
        }
        finally
        {
            _answering.Dispose();
            _answering = null;
            IsAnswering = false;
        }

        if (!reply.IsFailed)
        {
            await SaveAsync(conversation, MessageRole.Assistant, reply.Text, reply.IsNotFound, reply.Citations);
        }
    }

    [RelayCommand(CanExecute = nameof(IsAnswering))]
    private void Stop() => _answering?.Cancel();

    private Task OpenAsync(Citation citation) => OpenCitation?.Invoke(citation) ?? Task.CompletedTask;

    private async Task<Conversation> EnsureConversationAsync(string firstQuestion)
    {
        if (SelectedConversation is { } current)
        {
            return current.Conversation;
        }

        var title = firstQuestion.Length <= MaxTitleLength ? firstQuestion : $"{firstQuestion[..MaxTitleLength].TrimEnd()}…";
        var conversation = await conversations.CreateAsync(notebookId, title, CancellationToken.None);
        var item = new ConversationItemViewModel(conversation, this);

        Conversations.Insert(0, item);

        _switching = true;
        SelectedConversation = item;
        _switching = false;

        return conversation;
    }

    private Task SaveAsync(Conversation conversation, MessageRole role, string text, bool notFound, IReadOnlyList<Citation> citations) =>
        conversations.AppendMessageAsync(
            new Message(Guid.NewGuid(), conversation.Id, role, text, notFound, citations, DateTimeOffset.UtcNow),
            CancellationToken.None);

    private async Task StreamAnswerAsync(string question, List<ChatTurn> history, MessageViewModel reply, CancellationToken ct)
    {
        var buffer = new StringBuilder();
        var sinceFlush = Stopwatch.StartNew();

        try
        {
            await foreach (var answerEvent in answers.AskAsync(notebookId, question, history, ct))
            {
                switch (answerEvent)
                {
                    case TokenReceived token:
                        buffer.Append(token.Text);

                        if (sinceFlush.Elapsed >= FlushInterval)
                        {
                            reply.Append(buffer.ToString());
                            buffer.Clear();
                            sinceFlush.Restart();
                        }

                        break;

                    case AnswerCompleted completed:
                        buffer.Clear();
                        reply.Complete(completed.Answer, OpenAsync);
                        break;
                }
            }
        }
        finally
        {
            // Stopping early still shows everything that arrived.
            reply.Append(buffer.ToString());
        }
    }
}

public sealed partial class MessageViewModel : ObservableObject
{
    private MessageViewModel(MessageRole role, string text, bool isPending)
    {
        Role = role;
        Text = text;
        IsPending = isPending;
    }

    public MessageRole Role { get; }

    public bool IsUser => Role == MessageRole.User;

    public bool IsAssistant => Role == MessageRole.Assistant;

    [ObservableProperty]
    public partial string Text { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsThinking))]
    public partial bool IsPending { get; set; }

    [ObservableProperty]
    public partial bool IsNotFound { get; set; }

    [ObservableProperty]
    public partial bool IsFailed { get; set; }

    [ObservableProperty]
    public partial bool IsUnverified { get; set; }

    public ObservableCollection<CitationChipViewModel> Chips { get; } = [];

    public IReadOnlyList<Citation> Citations { get; private set; } = [];

    public bool HasChips => Chips.Count > 0;

    public string ChipsHeading => IsNotFound ? "Closest matches" : "Sources";

    public bool IsThinking => IsPending && Text.Length == 0;

    public static MessageViewModel FromUser(string text) => new(MessageRole.User, text, isPending: false);

    public static MessageViewModel FromSaved(Message message, Func<Citation, Task> open)
    {
        var viewModel = new MessageViewModel(message.Role, message.Content, isPending: false);

        if (message.Role == MessageRole.Assistant)
        {
            viewModel.Complete(new Answer(message.Content, message.NotFound, message.Citations), open);
        }

        return viewModel;
    }

    public static MessageViewModel Pending() => new(MessageRole.Assistant, "", isPending: true);

    public ChatTurn ToTurn() => new(Role, Text);

    public void Append(string text)
    {
        Text += text;
        OnPropertyChanged(nameof(IsThinking));
    }

    public void Complete(Answer answer, Func<Citation, Task> open)
    {
        Text = answer.Text;
        IsNotFound = answer.NotFound;
        IsUnverified = !answer.NotFound && answer.Citations.Count == 0;
        IsPending = false;
        Citations = answer.Citations;

        // Several markers can point to one page, which only needs one chip.
        foreach (var citation in answer.Citations.DistinctBy(c => (c.DocumentId, c.Page)))
        {
            Chips.Add(new CitationChipViewModel(citation, open));
        }

        OnPropertyChanged(nameof(HasChips));
        OnPropertyChanged(nameof(ChipsHeading));
    }

    public void Stop()
    {
        IsPending = false;

        if (Text.Length == 0)
        {
            Text = "Stopped.";
            IsFailed = true;
        }
    }

    public void Fail(string message)
    {
        Text = message;
        IsPending = false;
        IsFailed = true;
    }
}

public sealed partial class CitationChipViewModel(Citation citation, Func<Citation, Task> open) : ObservableObject
{
    public Citation Citation { get; } = citation;

    public string Label { get; } = $"{citation.FileName} · p. {citation.Page}";

    [RelayCommand]
    private Task OpenAsync() => open(Citation);
}

public sealed partial class ConversationItemViewModel(Conversation conversation, ChatViewModel parent) : ObservableObject
{
    public Conversation Conversation { get; } = conversation;

    [ObservableProperty]
    public partial string Title { get; set; } = conversation.Title;

    [ObservableProperty]
    public partial bool IsRenaming { get; set; }

    [ObservableProperty]
    public partial string EditTitle { get; set; } = conversation.Title;

    [RelayCommand]
    private void StartRename()
    {
        EditTitle = Title;
        IsRenaming = true;
    }

    [RelayCommand]
    private async Task SaveRenameAsync()
    {
        IsRenaming = false;

        if (!string.IsNullOrWhiteSpace(EditTitle))
        {
            await parent.RenameAsync(this, EditTitle.Trim());
        }
    }

    [RelayCommand]
    private Task DeleteAsync() => parent.DeleteAsync(this);
}
