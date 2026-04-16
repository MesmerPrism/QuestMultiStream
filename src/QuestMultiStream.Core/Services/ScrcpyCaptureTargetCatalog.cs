using QuestMultiStream.Core.Models;

namespace QuestMultiStream.Core.Services;

public static class ScrcpyCaptureTargetCatalog
{
    public static IReadOnlyList<ScrcpyCaptureTarget> Build(
        IReadOnlyList<ScrcpyCaptureTarget>? displays,
        IReadOnlyList<ScrcpyCaptureTarget>? cameras)
    {
        var normalizedDisplays = NormalizeDisplays(displays);
        var normalizedCameras = NormalizeCameras(cameras);
        var orderedTargets = new List<ScrcpyCaptureTarget>(normalizedDisplays.Count + normalizedCameras.Count);

        orderedTargets.AddRange(normalizedDisplays);
        orderedTargets.AddRange(normalizedCameras);

        return orderedTargets
            .GroupBy(target => (target.Kind, target.Id))
            .Select(group => group.First())
            .OrderBy(target => target.SortOrder)
            .ThenBy(target => target.Kind)
            .ThenBy(target => ParseNumericId(target.Kind == ScrcpyCaptureTargetKind.Camera
                ? target.LaunchCameraId ?? target.Id
                : target.LaunchDisplayId?.ToString() ?? target.Id))
            .ThenBy(target => target.Id, StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<ScrcpyCaptureTarget> NormalizeDisplays(IReadOnlyList<ScrcpyCaptureTarget>? displays)
    {
        var materialized = displays?
            .Where(target => target.Kind == ScrcpyCaptureTargetKind.Display)
            .ToList() ?? [];

        if (!materialized.Any(target => ResolveDisplayId(target) == 0))
        {
            materialized.Insert(0, ScrcpyCaptureTarget.DefaultDisplay);
        }

        return materialized
            .Select(DecorateDisplay)
            .ToArray();
    }

    private static IReadOnlyList<ScrcpyCaptureTarget> NormalizeCameras(IReadOnlyList<ScrcpyCaptureTarget>? cameras)
        => cameras?
            .Where(target => target.Kind == ScrcpyCaptureTargetKind.Camera)
            .Select(DecorateCamera)
            .ToArray() ?? [];

    private static ScrcpyCaptureTarget DecorateDisplay(ScrcpyCaptureTarget display)
    {
        var displayId = ResolveDisplayId(display);
        var isSmallSurface = IsSmallSurface(display);
        var isLikelyBlank = displayId == 3;
        var detail = BuildDetail(
            display.Detail,
            displayId == 0 ? "full stereo mirror" : null,
            isSmallSurface ? "panel surface" : null,
            isLikelyBlank ? "experimental / often blank" : null);

        return display with
        {
            LaunchDisplayId = displayId,
            Detail = detail,
            SortOrder = displayId switch
            {
                0 => 0,
                3 => 90,
                _ when isSmallSurface => 80,
                _ => 40
            },
            IsExperimental = display.IsExperimental || isSmallSurface || isLikelyBlank
        };
    }

    private static ScrcpyCaptureTarget DecorateCamera(ScrcpyCaptureTarget camera)
    {
        var cameraId = string.IsNullOrWhiteSpace(camera.LaunchCameraId)
            ? camera.Id
            : camera.LaunchCameraId;
        var detail = BuildDetail(camera.Detail, "headset camera");

        return camera with
        {
            LaunchCameraId = cameraId,
            Detail = detail,
            SortOrder = 20
        };
    }

    private static int ResolveDisplayId(ScrcpyCaptureTarget display)
        => display.LaunchDisplayId ??
           (int.TryParse(display.Id, out var parsedDisplayId) ? parsedDisplayId : int.MaxValue);

    private static bool IsSmallSurface(ScrcpyCaptureTarget display)
        => display.Width is int width &&
           display.Height is int height &&
           (width <= 32 || height <= 32 || width < 1400 || height < 900);

    private static string BuildDetail(params string?[] parts)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var orderedParts = new List<string>();

        foreach (var part in parts)
        {
            if (string.IsNullOrWhiteSpace(part))
            {
                continue;
            }

            foreach (var token in part.Split("·", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                if (seen.Add(token))
                {
                    orderedParts.Add(token);
                }
            }
        }

        return string.Join(" · ", orderedParts);
    }

    private static int ParseNumericId(string id)
        => int.TryParse(id, out var parsed)
            ? parsed
            : int.MaxValue;
}
