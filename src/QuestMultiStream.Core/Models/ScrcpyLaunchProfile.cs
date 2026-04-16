namespace QuestMultiStream.Core.Models;

public sealed class ScrcpyLaunchProfile
{
    public string CaptureTargetId { get; init; } = "0";

    public ScrcpyCaptureTargetKind VideoSource { get; init; } = ScrcpyCaptureTargetKind.Display;

    public int? DisplayId { get; init; }

    public string? CameraId { get; init; }

    public string? Crop { get; init; }

    public string? Angle { get; init; }

    public string CaptureTargetLabel { get; init; } = "Display 0 · default stereo mirror";

    public int MaxSize { get; init; } = 1344;

    public int MaxFps { get; init; } = 30;

    public int VideoBitRateMbps { get; init; } = 20;

    public bool EnableAudio { get; init; }

    public bool EnableControl { get; init; }

    public bool AlwaysOnTop { get; init; }

    public bool Borderless { get; init; }

    public WindowLayoutBounds? InitialWindowBounds { get; init; }

    public bool StayAwake { get; init; } = true;
}
