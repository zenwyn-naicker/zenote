using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Zenote.Core.Abstractions;
using Zenote.Core.Domain;

namespace Zenote.App.ViewModels;

public sealed partial class ChatViewModel(Guid notebookId, IRagAnswerService answers) : ObservableObject
{
    // Updating the UI per token would flood it, so streamed text is flushed in small batches.
    private static readonly TimeSpan FlushInterval = TimeSpan.FromMilliseconds(40);

    private CancellationTokenSource? _answering;

    public ObservableCollection<MessageViewModel> Messages { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    public partial string Input { get; set; } = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand), nameof(StopCommand))]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    public partial bool IsAnswering { get; set; }

    public bool IsIdle => !IsAnswering;

    private bool CanSend => !IsAnswering && !string.IsNullOrWhiteSpace(Input);

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        var question = Input.Trim();
        var history = Messages.Where(m => !m.IsFailed).Select(m => m.ToTurn()).ToList();

        Input = "";
        Messages.Add(MessageViewModel.FromUser(question));

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
    }

    [RelayCommand(CanExecute = nameof(IsAnswering))]
    private void Stop() => _answering?.Cancel();

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
                        reply.Complete(completed.Answer);
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

    public bool IsThinking => IsPending && Text.Length == 0;

    public static MessageViewModel FromUser(string text) => new(MessageRole.User, text, isPending: false);

    public static MessageViewModel Pending() => new(MessageRole.Assistant, "", isPending: true);

    public ChatTurn ToTurn() => new(Role, Text);

    public void Append(string text)
    {
        Text += text;
        OnPropertyChanged(nameof(IsThinking));
    }

    public void Complete(Answer answer)
    {
        Text = answer.Text;
        IsNotFound = answer.NotFound;
        IsPending = false;
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
