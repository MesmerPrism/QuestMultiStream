namespace QuestMultiStream.Core.Models;

public sealed record DependencySnapshot(
    string? RepoRoot,
    string? ScrcpyPath,
    string? AdbPath,
    string Guidance)
{
    public bool HasScrcpy => !string.IsNullOrWhiteSpace(ScrcpyPath);

    public bool HasAdb => !string.IsNullOrWhiteSpace(AdbPath);

    public bool IsReady => HasScrcpy && HasAdb;
}
