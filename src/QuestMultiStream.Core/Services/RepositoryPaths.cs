namespace QuestMultiStream.Core.Services;

public sealed class RepositoryPaths
{
    private RepositoryPaths(string appBaseDirectory, string? repoRoot)
    {
        AppBaseDirectory = appBaseDirectory;
        RepoRoot = repoRoot;
    }

    public string AppBaseDirectory { get; }

    public string? RepoRoot { get; }

    public string ToolsRoot => RepoRoot is not null
        ? Path.Combine(RepoRoot, "tools")
        : Path.Combine(AppBaseDirectory, "tools");

    public static RepositoryPaths Discover(string? startDirectory = null)
    {
        var appBaseDirectory = Path.GetFullPath(startDirectory ?? AppContext.BaseDirectory);
        var current = new DirectoryInfo(appBaseDirectory);

        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "QuestMultiStream.sln")) ||
                File.Exists(Path.Combine(current.FullName, "QuestMultiStream.slnx")) ||
                File.Exists(Path.Combine(current.FullName, "global.json")))
            {
                return new RepositoryPaths(appBaseDirectory, current.FullName);
            }

            current = current.Parent;
        }

        return new RepositoryPaths(appBaseDirectory, null);
    }
}
