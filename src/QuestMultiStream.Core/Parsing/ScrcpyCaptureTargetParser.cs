using System.Text.RegularExpressions;
using QuestMultiStream.Core.Models;

namespace QuestMultiStream.Core.Parsing;

public static partial class ScrcpyCaptureTargetParser
{
    public static IReadOnlyList<ScrcpyCaptureTarget> ParseDisplays(string text)
    {
        var targets = new List<ScrcpyCaptureTarget>();

        foreach (Match match in DisplayPattern().Matches(text))
        {
            var id = match.Groups["id"].Value;
            var size = match.Groups["size"].Value.Trim();
            var detailParts = new List<string> { size };
            if (id == "0")
            {
                detailParts.Add("default");
            }

            if (TryParseSize(size, out var width, out var height))
            {
                if (width <= 32 || height <= 32)
                {
                    detailParts.Add("tiny surface");
                }
                else if (width < 1400 || height < 900)
                {
                    detailParts.Add("small surface");
                }
            }

            var detail = string.Join(" · ", detailParts);
            _ = int.TryParse(id, out var displayId);
            targets.Add(new ScrcpyCaptureTarget(
                ScrcpyCaptureTargetKind.Display,
                id,
                $"Display {id}",
                detail)
            {
                Width = width > 0 ? width : null,
                Height = height > 0 ? height : null,
                LaunchDisplayId = displayId > 0 || id == "0"
                    ? displayId
                    : null
            });
        }

        return targets;
    }

    public static IReadOnlyList<ScrcpyCaptureTarget> ParseCameras(string text)
    {
        var targets = new List<ScrcpyCaptureTarget>();

        foreach (Match match in CameraPattern().Matches(text))
        {
            var id = match.Groups["id"].Value;
            var detail = match.Groups["detail"].Value.Trim();
            targets.Add(new ScrcpyCaptureTarget(
                ScrcpyCaptureTargetKind.Camera,
                id,
                $"Camera {id}",
                detail)
            {
                LaunchCameraId = id
            });
        }

        return targets;
    }

    [GeneratedRegex(@"^\s*--display-id=(?<id>\d+)\s+\((?<size>[^)]+)\)\s*$", RegexOptions.Multiline | RegexOptions.Compiled)]
    private static partial Regex DisplayPattern();

    [GeneratedRegex(@"^\s*--camera-id=(?<id>[^\s]+)\s+\((?<detail>[^)]+)\)\s*$", RegexOptions.Multiline | RegexOptions.Compiled)]
    private static partial Regex CameraPattern();

    private static bool TryParseSize(string size, out int width, out int height)
    {
        var parts = size.Split('x', StringSplitOptions.TrimEntries);
        if (parts.Length == 2 &&
            int.TryParse(parts[0], out width) &&
            int.TryParse(parts[1], out height))
        {
            return true;
        }

        width = 0;
        height = 0;
        return false;
    }
}
