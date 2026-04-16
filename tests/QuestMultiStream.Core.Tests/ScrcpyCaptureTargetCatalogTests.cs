using QuestMultiStream.Core.Models;
using QuestMultiStream.Core.Services;

namespace QuestMultiStream.Core.Tests;

public sealed class ScrcpyCaptureTargetCatalogTests
{
    [Fact]
    public void Build_PrioritizesReliableSourcesAndDemotesWeakDisplays()
    {
        var displays = new[]
        {
            new ScrcpyCaptureTarget(ScrcpyCaptureTargetKind.Display, "0", "Display 0", "3664x1920 · default")
            {
                Width = 3664,
                Height = 1920,
                LaunchDisplayId = 0
            },
            new ScrcpyCaptureTarget(ScrcpyCaptureTargetKind.Display, "25", "Display 25", "500x800 · small surface")
            {
                Width = 500,
                Height = 800,
                LaunchDisplayId = 25
            },
            new ScrcpyCaptureTarget(ScrcpyCaptureTargetKind.Display, "3", "Display 3", "3664x1920")
            {
                Width = 3664,
                Height = 1920,
                LaunchDisplayId = 3
            }
        };
        var cameras = new[]
        {
            new ScrcpyCaptureTarget(ScrcpyCaptureTargetKind.Camera, "1", "Camera 1", "front")
            {
                LaunchCameraId = "1"
            },
            new ScrcpyCaptureTarget(ScrcpyCaptureTargetKind.Camera, "50", "Camera 50", "back")
            {
                LaunchCameraId = "50"
            },
            new ScrcpyCaptureTarget(ScrcpyCaptureTargetKind.Camera, "51", "Camera 51", "back")
            {
                LaunchCameraId = "51"
            }
        };

        var targets = ScrcpyCaptureTargetCatalog.Build(displays, cameras);

        Assert.Collection(
            targets,
            target => Assert.Equal("0", target.Id),
            target => Assert.Equal("1", target.Id),
            target => Assert.Equal("50", target.Id),
            target => Assert.Equal("51", target.Id),
            target => Assert.Equal("25", target.Id),
            target =>
            {
                Assert.Equal("3", target.Id);
                Assert.Contains("experimental / often blank", target.Detail);
            });
    }
}
