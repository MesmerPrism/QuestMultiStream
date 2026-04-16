using QuestMultiStream.Core.Models;
using QuestMultiStream.Core.Services;

namespace QuestMultiStream.Core.Tests;

public sealed class ScrcpyArgumentBuilderTests
{
    [Fact]
    public void Build_UsesProfileToggles()
    {
        var device = new QuestDevice(
            "serial-1",
            "Quest 3",
            "Quest 3",
            "eureka",
            "eureka",
            "device",
            QuestDeviceConnectionKind.Usb,
            true,
            null);
        var profile = new ScrcpyLaunchProfile
        {
            DisplayId = 5,
            MaxSize = 1600,
            MaxFps = 72,
            VideoBitRateMbps = 32,
            EnableAudio = true,
            EnableControl = true,
            AlwaysOnTop = true,
            Borderless = true,
            StayAwake = true
        };

        var arguments = ScrcpyArgumentBuilder.Build(device, "Window Title", profile);

        Assert.Contains("--serial=serial-1", arguments);
        Assert.Contains("--window-title=Window Title", arguments);
        Assert.Contains("--display-id=5", arguments);
        Assert.Contains("--max-size=1600", arguments);
        Assert.Contains("--max-fps=72", arguments);
        Assert.Contains("--video-bit-rate=32M", arguments);
        Assert.Contains("--always-on-top", arguments);
        Assert.Contains("--window-borderless", arguments);
        Assert.DoesNotContain("--no-audio", arguments);
        Assert.DoesNotContain("--no-control", arguments);
    }

    [Fact]
    public void Build_SkipsStayAwakeWhenControlIsDisabled()
    {
        var device = new QuestDevice(
            "serial-2",
            "Quest 3S",
            "Quest 3S",
            "panther",
            "panther",
            "device",
            QuestDeviceConnectionKind.Usb,
            true,
            null);
        var profile = new ScrcpyLaunchProfile
        {
            EnableControl = false,
            StayAwake = true
        };

        var arguments = ScrcpyArgumentBuilder.Build(device, "Window Title", profile);

        Assert.Contains("--no-control", arguments);
        Assert.DoesNotContain("--stay-awake", arguments);
    }

    [Fact]
    public void Build_UsesCameraModeForCameraTargets()
    {
        var device = new QuestDevice(
            "serial-3",
            "Quest 3",
            "Quest 3",
            "eureka",
            "eureka",
            "device",
            QuestDeviceConnectionKind.Usb,
            true,
            null);
        var profile = new ScrcpyLaunchProfile
        {
            VideoSource = ScrcpyCaptureTargetKind.Camera,
            CameraId = "51",
            EnableControl = true,
            StayAwake = true
        };

        var arguments = ScrcpyArgumentBuilder.Build(device, "Window Title", profile);

        Assert.Contains("--video-source=camera", arguments);
        Assert.Contains("--camera-id=51", arguments);
        Assert.Contains("--no-control", arguments);
        Assert.DoesNotContain("--display-id=51", arguments);
        Assert.DoesNotContain("--stay-awake", arguments);
    }

    [Fact]
    public void Build_EmitsCropAndAngleWhenConfigured()
    {
        var device = new QuestDevice(
            "serial-4",
            "Quest 3S",
            "Quest 3S",
            "panther",
            "panther",
            "device",
            QuestDeviceConnectionKind.Usb,
            true,
            null);
        var profile = new ScrcpyLaunchProfile
        {
            CaptureTargetId = "display-0-custom",
            DisplayId = 0,
            Crop = "1832:1920:0:0",
            Angle = "2.5"
        };

        var arguments = ScrcpyArgumentBuilder.Build(device, "Window Title", profile);

        Assert.Contains("--display-id=0", arguments);
        Assert.Contains("--crop=1832:1920:0:0", arguments);
        Assert.Contains("--angle=2.5", arguments);
    }
}
