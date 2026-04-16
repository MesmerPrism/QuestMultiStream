namespace QuestMultiStream.Core.Models;

public sealed record ScrcpyCaptureTarget(
    ScrcpyCaptureTargetKind Kind,
    string Id,
    string Label,
    string Detail)
{
    public static ScrcpyCaptureTarget DefaultDisplay { get; } =
        new(ScrcpyCaptureTargetKind.Display, "0", "Display 0", "default stereo mirror")
        {
            LaunchDisplayId = 0,
            SortOrder = 0
        };

    public int? Width { get; init; }

    public int? Height { get; init; }

    public int? LaunchDisplayId { get; init; }

    public string? LaunchCameraId { get; init; }

    public string? Crop { get; init; }

    public string? Angle { get; init; }

    public int SortOrder { get; init; } = 100;

    public bool IsExperimental { get; init; }

    public string SelectionText => string.IsNullOrWhiteSpace(Detail)
        ? Label
        : $"{Label} · {Detail}";

    public string SourceText => Kind switch
    {
        ScrcpyCaptureTargetKind.Display when
            LaunchDisplayId is int displayId &&
            !string.Equals(Label, $"Display {displayId}", StringComparison.Ordinal)
                => $"{Label} · Display {displayId}",
        ScrcpyCaptureTargetKind.Camera when
            !string.IsNullOrWhiteSpace(LaunchCameraId) &&
            !string.Equals(Label, $"Camera {LaunchCameraId}", StringComparison.Ordinal)
                => $"{Label} · Camera {LaunchCameraId}",
        ScrcpyCaptureTargetKind.Display => $"Display {LaunchDisplayId?.ToString() ?? Id}",
        _ => $"Camera {LaunchCameraId ?? Id}"
    };
}
