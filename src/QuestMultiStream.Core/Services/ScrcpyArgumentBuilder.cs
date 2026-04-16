using QuestMultiStream.Core.Models;

namespace QuestMultiStream.Core.Services;

public static class ScrcpyArgumentBuilder
{
    public static IReadOnlyList<string> Build(
        QuestDevice device,
        string windowTitle,
        ScrcpyLaunchProfile launchProfile)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(windowTitle);
        ArgumentNullException.ThrowIfNull(launchProfile);

        var arguments = new List<string>
        {
            $"--serial={device.Serial}",
            $"--window-title={windowTitle}",
            $"--max-size={launchProfile.MaxSize}",
            $"--video-bit-rate={launchProfile.VideoBitRateMbps}M",
            $"--max-fps={launchProfile.MaxFps}",
            "--disable-screensaver"
        };

        if (launchProfile.VideoSource == ScrcpyCaptureTargetKind.Camera)
        {
            arguments.Add("--video-source=camera");

            if (!string.IsNullOrWhiteSpace(launchProfile.CameraId))
            {
                arguments.Add($"--camera-id={launchProfile.CameraId}");
            }
        }
        else if (launchProfile.DisplayId is int displayId)
        {
            arguments.Add($"--display-id={displayId}");
        }

        if (!string.IsNullOrWhiteSpace(launchProfile.Crop))
        {
            arguments.Add($"--crop={launchProfile.Crop}");
        }

        if (!string.IsNullOrWhiteSpace(launchProfile.Angle))
        {
            arguments.Add($"--angle={launchProfile.Angle}");
        }

        if (!launchProfile.EnableAudio)
        {
            arguments.Add("--no-audio");
        }

        var controlEnabled = launchProfile.EnableControl && launchProfile.VideoSource != ScrcpyCaptureTargetKind.Camera;
        if (!controlEnabled)
        {
            arguments.Add("--no-control");
        }

        if (launchProfile.AlwaysOnTop)
        {
            arguments.Add("--always-on-top");
        }

        if (launchProfile.InitialWindowBounds is { } initialWindowBounds)
        {
            arguments.Add($"--window-x={initialWindowBounds.X}");
            arguments.Add($"--window-y={initialWindowBounds.Y}");
            arguments.Add($"--window-width={initialWindowBounds.Width}");
            arguments.Add($"--window-height={initialWindowBounds.Height}");
        }

        if (launchProfile.Borderless)
        {
            arguments.Add("--window-borderless");
        }

        if (launchProfile.StayAwake && controlEnabled)
        {
            arguments.Add("--stay-awake");
        }

        return arguments;
    }
}
