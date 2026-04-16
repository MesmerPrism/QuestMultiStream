using System.Windows.Media;
using QuestMultiStream.Core.Models;
using QuestMultiStream.Core.Services;

namespace QuestMultiStream.App.ViewModels;

public sealed class SessionCardViewModel
{
    public SessionCardViewModel(QuestCastSession session)
    {
        Title = session.Device.DisplayName;
        Detail = session.Device.Serial;
        SourceText = session.LaunchProfile.CaptureTargetLabel;
        StateText = session.State.ToString();
        LastMessage = session.LastMessage;
        StartedAtText = session.StartedAt.ToString("HH:mm:ss");
        ProcessText = $"PID {session.ProcessId}";
        StatusBrush = session.State switch
        {
            QuestCastSessionState.Running => UiPalette.Success,
            QuestCastSessionState.Starting => UiPalette.Accent,
            QuestCastSessionState.Failed => UiPalette.Failure,
            _ => UiPalette.Muted
        };
    }

    public string Title { get; }

    public string Detail { get; }

    public string SourceText { get; }

    public string StateText { get; }

    public string LastMessage { get; }

    public string StartedAtText { get; }

    public string ProcessText { get; }

    public Brush StatusBrush { get; }
}
