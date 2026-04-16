using System.Windows.Media;
using QuestMultiStream.Core.Models;

namespace QuestMultiStream.App.ViewModels;

public sealed class LogEntryViewModel
{
    public LogEntryViewModel(OperatorLogEntry entry)
    {
        TimeText = entry.Timestamp.ToString("HH:mm:ss");
        Level = entry.Level.ToUpperInvariant();
        Message = entry.Message;
        Detail = entry.Detail;
        StatusBrush = entry.Level switch
        {
            "error" => UiPalette.Failure,
            "warning" => UiPalette.Warning,
            "debug" => UiPalette.Accent,
            _ => UiPalette.Muted
        };
    }

    public string TimeText { get; }

    public string Level { get; }

    public string Message { get; }

    public string? Detail { get; }

    public Brush StatusBrush { get; }
}
