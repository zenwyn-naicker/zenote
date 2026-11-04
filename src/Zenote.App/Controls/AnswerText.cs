using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Zenote.App.ViewModels;

namespace Zenote.App.Controls;

/// Shows an answer, turning its [n] markers into small numbered buttons once it has finished streaming.
public sealed partial class AnswerText : SelectableTextBlock
{
    public static readonly StyledProperty<MessageViewModel?> MessageProperty =
        AvaloniaProperty.Register<AnswerText, MessageViewModel?>(nameof(Message));

    protected override Type StyleKeyOverride => typeof(SelectableTextBlock);

    public MessageViewModel? Message
    {
        get => GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property != MessageProperty)
        {
            return;
        }

        if (change.OldValue is MessageViewModel previous)
        {
            previous.PropertyChanged -= OnMessageChanged;
        }

        if (change.NewValue is MessageViewModel current)
        {
            current.PropertyChanged += OnMessageChanged;
        }

        Rebuild();
    }

    private void OnMessageChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => Rebuild();

    private void Rebuild()
    {
        if (Message is not { } message)
        {
            return;
        }

        Inlines?.Clear();

        if (message.IsPending)
        {
            Text = message.Text;
            return;
        }

        Text = null;
        Inlines ??= [];

        var position = 0;
        foreach (Match marker in Marker().Matches(message.Text))
        {
            if (message.FindChip(int.Parse(marker.Groups[1].Value)) is not { } chip)
            {
                continue;
            }

            Inlines.Add(new Run(message.Text[position..marker.Index].TrimEnd()));
            Inlines.Add(new InlineUIContainer(CreateButton(marker.Groups[1].Value, chip)) { BaselineAlignment = BaselineAlignment.Center });
            position = marker.Index + marker.Length;
        }

        Inlines.Add(new Run(message.Text[position..]));
    }

    private static Button CreateButton(string number, CitationChipViewModel chip)
    {
        var button = new Button { Content = number, Command = chip.OpenCommand };
        button.Classes.Add("citation");
        ToolTip.SetTip(button, chip.Label);
        return button;
    }

    [GeneratedRegex(@"\[(\d+)\]")]
    private static partial Regex Marker();
}
