using System.Diagnostics;
using System.IO.Compression;

namespace QuestMultiStream.PreviewInstaller;

internal sealed record PreviewReleaseInstallResult(
    bool UpdatedExistingInstall,
    string InstallDirectory,
    string ExecutablePath);

internal sealed class PreviewReleaseInstaller
{
    private const string ExecutableFileName = "QuestMultiStream.App.exe";
    private const string ShortcutName = "Quest Multi Stream.lnk";

    public async Task<PreviewReleaseInstallResult> InstallOrUpdateAsync(
        string archivePath,
        string installDirectory,
        IProgress<InstallerProgressUpdate>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(installDirectory);

        var installRoot = new DirectoryInfo(installDirectory);
        var updatedExistingInstall = installRoot.Exists;
        var stagingRoot = Path.Combine(Path.GetTempPath(), "QuestMultiStream-Install-" + Guid.NewGuid().ToString("N"));
        var stagingInstallDirectory = Path.Combine(stagingRoot, "QuestMultiStream");

        try
        {
            progress?.Report(new InstallerProgressUpdate(
                "Preparing install payload",
                "Expanding the downloaded release archive into a temporary staging directory.",
                48));

            Directory.CreateDirectory(stagingInstallDirectory);
            ZipFile.ExtractToDirectory(archivePath, stagingInstallDirectory, overwriteFiles: true);

            var stagedExePath = Path.Combine(stagingInstallDirectory, ExecutableFileName);
            if (!File.Exists(stagedExePath))
            {
                throw new InvalidOperationException($"The downloaded release archive did not contain {ExecutableFileName}.");
            }

            progress?.Report(new InstallerProgressUpdate(
                "Replacing installed files",
                "Stopping any running Quest Multi Stream processes from the current install and replacing the install directory.",
                72));

            StopRunningInstallProcesses(installRoot.FullName);

            if (installRoot.Exists)
            {
                installRoot.Delete(recursive: true);
            }

            Directory.CreateDirectory(installRoot.Parent!.FullName);
            Directory.Move(stagingInstallDirectory, installRoot.FullName);

            var installedExePath = Path.Combine(installRoot.FullName, ExecutableFileName);
            if (!File.Exists(installedExePath))
            {
                throw new InvalidOperationException($"The installed preview does not contain {ExecutableFileName} after extraction.");
            }

            progress?.Report(new InstallerProgressUpdate(
                "Creating shortcuts",
                "Writing Start Menu and Desktop shortcuts for the installed preview.",
                90));
            CreateShortcuts(installedExePath);

            await Task.CompletedTask.ConfigureAwait(false);
            return new PreviewReleaseInstallResult(updatedExistingInstall, installRoot.FullName, installedExePath);
        }
        finally
        {
            try
            {
                if (Directory.Exists(stagingRoot))
                {
                    Directory.Delete(stagingRoot, recursive: true);
                }
            }
            catch
            {
            }
        }
    }

    public static bool TryLaunchInstalledApp(string executablePath, out string detail)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            detail = "The preview was installed, but the launcher could not find the installed EXE afterward. Launch Quest Multi Stream from the Start menu.";
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = executablePath,
                WorkingDirectory = Path.GetDirectoryName(executablePath),
                UseShellExecute = true
            });

            detail = "The helper then launched the installed app automatically.";
            return true;
        }
        catch (Exception exception)
        {
            detail = $"The preview was installed, but Windows did not accept the automatic launch request ({exception.Message}). Launch Quest Multi Stream from the Start menu.";
            return false;
        }
    }

    private static void StopRunningInstallProcesses(string installDirectory)
    {
        foreach (var process in Process.GetProcessesByName("QuestMultiStream.App")) {
            try
            {
                var processPath = process.MainModule?.FileName;
                if (string.IsNullOrWhiteSpace(processPath) ||
                    !processPath.StartsWith(installDirectory, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!process.CloseMainWindow())
                {
                    process.Kill(entireProcessTree: true);
                    continue;
                }

                if (!process.WaitForExit(3000))
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    private static void CreateShortcuts(string executablePath)
    {
        var desktopShortcutPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            ShortcutName);
        var startMenuShortcutPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Programs),
            ShortcutName);

        CreateShortcut(executablePath, desktopShortcutPath);
        CreateShortcut(executablePath, startMenuShortcutPath);
    }

    private static void CreateShortcut(string executablePath, string shortcutPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(shortcutPath)!);
        var shellType = Type.GetTypeFromProgID("WScript.Shell", throwOnError: true)!;
        dynamic shell = Activator.CreateInstance(shellType)!;

        try
        {
            dynamic shortcut = shell.CreateShortcut(shortcutPath);
            try
            {
                shortcut.TargetPath = executablePath;
                shortcut.WorkingDirectory = Path.GetDirectoryName(executablePath);
                shortcut.IconLocation = executablePath + ",0";
                shortcut.Save();
            }
            finally
            {
                System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shortcut);
            }
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
        }
    }
}
