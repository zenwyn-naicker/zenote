using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Zenote.Core.Abstractions;
using Zenote.Core.Domain;

namespace Zenote.App.ViewModels;

public sealed partial class ChatViewModel : ObservableObject
{
    // Updating the UI per token would flood it, so streamed text is flushed in small batches.
    private static readonly TimeSpan FlushInterval = TimeSpan.FromMilliseconds(40);

    private const int MaxTitleLength = 60;

    private readonly Guid _notebookId;
    private readonly IRagAnswerService _answers;
    private readonly IConversationRepository _conversations;

    private CancellationTokenSource? _answering;
    private bool _switching;

    public ChatViewModel(Guid notebookId, IRagAnswerService answers, IConversationRepository conversations)
    {
        _notebookId = notebookId;
        _answers = answers;
        _conversations = conversations;
        Messages.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasNoMessages));
        Conversations.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasConversations));
    }

    public Func<Citation, Task>? OpenCitation { get; set; }

    public ObservableCollection<MessageViewModel> Messages { get; } = [];

    public ObservableCollection<ConversationItemViewModel> Conversations { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentChatTitle))]
    public partial ConversationItemViewModel? SelectedConversation { get; set; }

    [ObservableProperty]
    public partial string NotebookTitle { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SourcesLabel), nameof(HasSources), nameof(HasNoSources), nameof(InputHint))]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    public partial int SourceCount { get; set; }

    public string InputHint => HasSources ? "Start typing…" : "Add a source to get started";

    public string SourcesLabel => SourceCount == 1 ? "1 source" : $"{SourceCount} sources";

    public bool HasSources => SourceCount > 0;

    public bool HasNoSources => SourceCount == 0;

    public bool HasConversations => Conversations.Count > 0;

    public string CurrentChatTitle => SelectedConversation?.Title ?? "New chat";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    public partial string Input { get; set; } = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand), nameof(StopCommand), nameof(NewChatCommand))]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    public partial bool IsAnswering { get; set; }

    public bool IsIdle => !IsAnswering;

    public bool HasNoMessages => Messages.Count == 0;

    private bool CanSend => HasSources && !IsAnswering && !string.IsNullOrWhiteSpace(Input);

    public async Task LoadAsync()
    {
        var saved = await _conversations.ListAsync(_notebookId, CancellationToken.None);

        Conversations.Clear();
        foreach (var conversation in saved)
        {
            Conversations.Add(new ConversationItemViewModel(conversation, this));
        }

        if (Conversations.Count > 0)
        {
            var lastViewed = await _conversations.GetLastViewedAsync(_notebookId, CancellationToken.None);
            await ShowAsync(Conversations.FirstOrDefault(c => c.Conversation.Id == lastViewed) ?? Conversations[0]);
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

        foreach (var message in await _conversations.GetMessagesAsync(item.Conversation.Id, CancellationToken.None))
        {
            Messages.Add(MessageViewModel.FromSaved(message, OpenAsync));
        }

        await _conversations.SetLastViewedAsync(_notebookId, item.Conversation.Id, CancellationToken.None);
    }

    internal async Task RenameAsync(ConversationItemViewModel item, string title)
    {
        await _conversations.RenameAsync(item.Conversation.Id, title, CancellationToken.None);
        item.Title = title;
        OnPropertyChanged(nameof(CurrentChatTitle));
    }

    internal async Task DeleteAsync(ConversationItemViewModel item)
    {
        await _conversations.DeleteAsync(item.Conversation.Id, CancellationToken.None);

        // Clear the selection first, so the list isn't removing and deselecting at the same moment.
        if (SelectedConversation == item)
        {
            await ShowAsync(null);
        }

        Conversations.Remove(item);
    }

    partial void OnSelectedConversationChanged(ConversationItemViewModel? value)
    {
        if (!_switching)
        {
            _ = ShowAsync(value);
        }
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
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
        catch (HttpRequestException ex)
        {
            reply.Fail(ex.StatusCode == HttpStatusCode.NotFound || ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase)
                ? "That chat model isn't installed. Choose another in Settings."
                : "Couldn't reach Ollama. Open it, then try again.");
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
        var conversation = await _conversations.CreateAsync(_notebookId, title, CancellationToken.None);
        await _conversations.SetLastViewedAsync(_notebookId, conversation.Id, CancellationToken.None);
        var item = new ConversationItemViewModel(conversation, this);

        Conversations.Insert(0, item);

        _switching = true;
        SelectedConversation = item;
        _switching = false;

        return conversation;
    }

    private Task SaveAsync(Conversation conversation, MessageRole role, string text, bool notFound, IReadOnlyList<Citation> citations) =>
        _conversations.AppendMessageAsync(
            new Message(Guid.NewGuid(), conversation.Id, role, text, notFound, citations, DateTimeOffset.UtcNow),
            CancellationToken.None);

    private async Task StreamAnswerAsync(string question, List<ChatTurn> history, MessageViewModel reply, CancellationToken ct)
    {
        var buffer = new StringBuilder();
        var sinceFlush = Stopwatch.StartNew();

        try
        {
            await foreach (var answerEvent in _answers.AskAsync(_notebookId, question, history, ct))
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
    private readonly Dictionary<int, CitationChipViewModel> _chipsByMarker = [];

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
    [NotifyPropertyChangedFor(nameof(IsThinking), nameof(CanCopy))]
    public partial bool IsPending { get; set; }

    [ObservableProperty]
    public partial bool IsNotFound { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCopy))]
    public partial bool IsFailed { get; set; }

    [ObservableProperty]
    public partial bool IsUnverified { get; set; }

    [ObservableProperty]
    public partial bool IsStopped { get; set; }

    [ObservableProperty]
    public partial bool IsCopied { get; set; }

    public bool CanCopy => !IsPending && !IsFailed && Text.Length > 0;

    /// What the copy button puts on the clipboard. Citation numbers mean nothing outside Zenote.
    public string CopyText => CitationMarker().Replace(Text, "");

    public ObservableCollection<CitationChipViewModel> Chips { get; } = [];

    public IReadOnlyList<Citation> Citations { get; private set; } = [];

    public CitationChipViewModel? FindChip(int marker) => _chipsByMarker.GetValueOrDefault(marker);

    public bool HasChips => Chips.Count > 0;

    public string ChipsHeading => IsNotFound ? "Closest matches" : IsUnverified ? "Pages it was given" : "Sources";

    public string UnverifiedText => HasChips
        ? "The model didn't cite specific lines, so check this against the pages below."
        : "Unverified: this answer has no citations, so check it against your sources.";

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
        Citations = answer.Citations;

        foreach (var citation in answer.Citations)
        {
            var chip = new CitationChipViewModel(citation, open);
            _chipsByMarker[citation.Marker] = chip;

            // Several markers can point to one page, which only needs one chip.
            if (!Chips.Any(c => c.Citation.DocumentId == citation.DocumentId && c.Citation.Page == citation.Page))
            {
                Chips.Add(chip);
            }
        }

        Text = answer.Text;
        IsNotFound = answer.NotFound;
        IsUnverified = !answer.NotFound && answer.Citations.All(citation => citation.Marker == 0);
        IsPending = false;

        OnPropertyChanged(nameof(HasChips));
        OnPropertyChanged(nameof(ChipsHeading));
        OnPropertyChanged(nameof(UnverifiedText));
    }

    public void Stop()
    {
        IsPending = false;

        if (Text.Length == 0)
        {
            Text = "Stopped.";
            IsFailed = true;
        }
        else
        {
            IsStopped = true;
        }
    }

    public void Fail(string message)
    {
        Text = message;
        IsPending = false;
        IsFailed = true;
    }

    [GeneratedRegex(@"\s*\[\d+\]")]
    private static partial Regex CitationMarker();
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
