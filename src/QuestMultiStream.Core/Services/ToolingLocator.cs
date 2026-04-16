using QuestMultiStream.Core.Models;

namespace QuestMultiStream.Core.Services;

public static class ToolingLocator
{
    public static DependencySnapshot Detect(RepositoryPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var scrcpyPath = TryLocateScrcpy(paths);
        var adbPath = TryLocateAdb(paths, scrcpyPath);
        var guidance = BuildGuidance(paths, scrcpyPath, adbPath);

        return new DependencySnapshot(paths.RepoRoot, scrcpyPath, adbPath, guidance);
    }

    private static string BuildGuidance(RepositoryPaths paths, string? scrcpyPath, string? adbPath)
    {
        if (string.IsNullOrWhiteSpace(scrcpyPath))
        {
            var installScript = paths.RepoRoot is null
                ? "tools/setup/Install-Scrcpy.ps1"
                : Path.Combine(paths.RepoRoot, "tools", "setup", "Install-Scrcpy.ps1");
            return $"scrcpy.exe was not found. Run {installScript} or install Genymobile.scrcpy via WinGet.";
        }

        if (string.IsNullOrWhiteSpace(adbPath))
        {
            return "adb.exe was not found. Install Android platform-tools or use the adb bundled with scrcpy.";
        }

        return "scrcpy and adb are available.";
    }

    private static string? TryLocateScrcpy(RepositoryPaths paths)
    {
        var candidates = new List<string>();

        AddCandidate(candidates, Environment.GetEnvironmentVariable("QUEST_MULTI_STREAM_SCRCPY"));
        AddCandidate(candidates, Path.Combine(paths.AppBaseDirectory, "scrcpy.exe"));
        AddCandidate(candidates, Path.Combine(paths.AppBaseDirectory, "scrcpy", "scrcpy.exe"));

        if (paths.RepoRoot is not null)
        {
            AddCandidate(candidates, FindNewestExecutable(Path.Combine(paths.RepoRoot, "tools", "scrcpy"), "scrcpy.exe"));
        }

        AddCandidate(candidates, FindOnPath("scrcpy.exe"));
        return candidates.FirstOrDefault(File.Exists);
    }

    private static string? TryLocateAdb(RepositoryPaths paths, string? scrcpyPath)
    {
        var candidates = new List<string>();

        AddCandidate(candidates, Environment.GetEnvironmentVariable("QUEST_MULTI_STREAM_ADB"));

        if (!string.IsNullOrWhiteSpace(scrcpyPath))
        {
            var directory = Path.GetDirectoryName(scrcpyPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                AddCandidate(candidates, Path.Combine(directory, "adb.exe"));
            }
        }

        AddCandidate(candidates, Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Android",
            "Sdk",
            "platform-tools",
            "adb.exe"));

        foreach (var envVar in new[] { "ANDROID_SDK_ROOT", "ANDROID_HOME" })
        {
            var value = Environment.GetEnvironmentVariable(envVar);
            if (!string.IsNullOrWhiteSpace(value))
            {
                AddCandidate(candidates, Path.Combine(value, "platform-tools", "adb.exe"));
            }
        }

        AddCandidate(candidates, FindOnPath("adb.exe"));
        return candidates.FirstOrDefault(File.Exists);
    }

    private static string? FindNewestExecutable(string root, string fileName)
    {
        if (!Directory.Exists(root))
        {
            return null;
        }

        return Directory.EnumerateFiles(root, fileName, SearchOption.AllDirectories)
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .Select(file => file.FullName)
            .FirstOrDefault();
    }

    private static string? FindOnPath(string fileName)
    {
        var pathEntries = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var entry in pathEntries)
        {
            var candidate = Path.Combine(entry, fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static void AddCandidate(ICollection<string> candidates, string? candidate)
    {
        if (!string.IsNullOrWhiteSpace(candidate))
        {
            candidates.Add(candidate);
        }
    }
}
