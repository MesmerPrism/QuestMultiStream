using QuestMultiStream.Core.Models;
using QuestMultiStream.Core.Parsing;

namespace QuestMultiStream.Core.Tests;

public sealed class ScrcpyCaptureTargetParserTests
{
    [Fact]
    public void ParseDisplays_ReturnsDisplayTargets()
    {
        const string output = """
            scrcpy 3.3.4 <https://github.com/Genymobile/scrcpy>
            List of displays:
                --display-id=0    (3664x1920)
                --display-id=5    (1082x80)
                --display-id=21   (3664x1920)
            """;

        var targets = ScrcpyCaptureTargetParser.ParseDisplays(output);

        Assert.Collection(
            targets,
            target =>
            {
                Assert.Equal(ScrcpyCaptureTargetKind.Display, target.Kind);
                Assert.Equal("0", target.Id);
                Assert.Equal("Display 0", target.Label);
                Assert.Equal("3664x1920 · default", target.Detail);
                Assert.Equal(3664, target.Width);
                Assert.Equal(1920, target.Height);
                Assert.Equal(0, target.LaunchDisplayId);
            },
            target =>
            {
                Assert.Equal(ScrcpyCaptureTargetKind.Display, target.Kind);
                Assert.Equal("5", target.Id);
                Assert.Equal("1082x80 · small surface", target.Detail);
                Assert.Equal(1082, target.Width);
                Assert.Equal(80, target.Height);
                Assert.Equal(5, target.LaunchDisplayId);
            },
            target =>
            {
                Assert.Equal(ScrcpyCaptureTargetKind.Display, target.Kind);
                Assert.Equal("21", target.Id);
                Assert.Equal("3664x1920", target.Detail);
                Assert.Equal(3664, target.Width);
                Assert.Equal(1920, target.Height);
                Assert.Equal(21, target.LaunchDisplayId);
            });
    }

    [Fact]
    public void ParseCameras_ReturnsCameraTargets()
    {
        const string output = """
            scrcpy 3.3.4 <https://github.com/Genymobile/scrcpy>
            List of cameras:
                --camera-id=1     (front, 1600x1200, fps=[15, 30])
                --camera-id=50    (back, 1280x1280, fps=[15, 30, 60])
                --camera-id=51    (back, 1280x1280, fps=[15, 30, 60])
            """;

        var targets = ScrcpyCaptureTargetParser.ParseCameras(output);

        Assert.Collection(
            targets,
            target =>
            {
                Assert.Equal(ScrcpyCaptureTargetKind.Camera, target.Kind);
                Assert.Equal("1", target.Id);
                Assert.Equal("Camera 1", target.Label);
                Assert.Equal("front, 1600x1200, fps=[15, 30]", target.Detail);
                Assert.Equal("1", target.LaunchCameraId);
            },
            target =>
            {
                Assert.Equal(ScrcpyCaptureTargetKind.Camera, target.Kind);
                Assert.Equal("50", target.Id);
                Assert.Equal("back, 1280x1280, fps=[15, 30, 60]", target.Detail);
                Assert.Equal("50", target.LaunchCameraId);
            },
            target =>
            {
                Assert.Equal(ScrcpyCaptureTargetKind.Camera, target.Kind);
                Assert.Equal("51", target.Id);
                Assert.Equal("back, 1280x1280, fps=[15, 30, 60]", target.Detail);
                Assert.Equal("51", target.LaunchCameraId);
            });
    }
}
