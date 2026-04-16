using System.Windows.Forms;

namespace QuestMultiStream.PreviewInstaller;

internal readonly record struct InstallerProgressUpdate(string Status, string Detail, int PercentComplete);

internal readonly record struct InstallerCompletionResult(
    string ArchivePath,
    string Summary,
    string Detail);

internal static class Program
{
    private const string ReleaseZipUri = "https://github.com/Zivilkannibale/QuestMultiStream/releases/latest/download/QuestMultiStream-win-x64.zip";
    private const string ReleasePageUri = "https://github.com/Zivilkannibale/QuestMultiStream/releases";
    private const string DownloadDirectoryName = "QuestMultiStreamPreviewSetup";
    private const string ReleaseZipFileName = "QuestMultiStream-win-x64.zip";
    private const string InstallDirectoryName = "QuestMultiStream";

    [STAThread]
    private static int Main()
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        try
        {
            using var installerForm = new InstallerStatusForm(InstallPreviewAsync, ReleasePageUri);
            Application.Run(installerForm);
            return 0;
        }
        catch (Exception exception)
        {
            ShowError(exception);
            return 1;
        }
    }

    private static async Task<InstallerCompletionResult> InstallPreviewAsync(
        IProgress<InstallerProgressUpdate> progress,
        CancellationToken cancellationToken)
    {
        progress.Report(new InstallerProgressUpdate(
            "Preparing guided setup",
            "Creating a temporary staging folder for the latest public Quest Multi Stream preview.",
            5));

        var downloadDirectory = Path.Combine(Path.GetTempPath(), DownloadDirectoryName);
        Directory.CreateDirectory(downloadDirectory);

        var archivePath = Path.Combine(downloadDirectory, ReleaseZipFileName);

        using var httpClient = new HttpClient();

        progress.Report(new InstallerProgressUpdate(
            "Downloading release archive",
            "Fetching the latest portable Windows release with the bundled scrcpy runtime from GitHub Releases.",
            25));
        await DownloadFileAsync(httpClient, ReleaseZipUri, archivePath, cancellationToken).ConfigureAwait(false);

        var installDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs",
            InstallDirectoryName);

        var installer = new PreviewReleaseInstaller();
        var installResult = await installer
            .InstallOrUpdateAsync(archivePath, installDirectory, progress, cancellationToken)
            .ConfigureAwait(false);

        _ = PreviewReleaseInstaller.TryLaunchInstalledApp(installResult.ExecutablePath, out var launchDetail);

        var completionSummary = BuildCompletionSummary(installResult);
        var completionDetail = BuildCompletionDetail(installResult, launchDetail);
        progress.Report(new InstallerProgressUpdate(
            completionSummary,
            completionDetail,
            100));

        return new InstallerCompletionResult(archivePath, completionSummary, completionDetail);
    }

    internal static string GetDownloadedArchivePath()
        => Path.Combine(Path.GetTempPath(), DownloadDirectoryName, ReleaseZipFileName);

    private static async Task DownloadFileAsync(HttpClient httpClient, string sourceUri, string destinationPath, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(sourceUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var output = File.Create(destinationPath);
        await response.Content.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
    }

    private static void ShowError(Exception exception)
    {
        var message =
            "Quest Multi Stream Preview Setup could not finish.\n\n" +
            $"{exception.Message}\n\n" +
            "Open the public release page if you need to download the portable zip manually.\n" +
            $"{ReleasePageUri}";

        MessageBox.Show(
            message,
            "Quest Multi Stream Preview Setup",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }

    private static string BuildCompletionSummary(PreviewReleaseInstallResult installResult)
    {
        return installResult.UpdatedExistingInstall
            ? "Quest Multi Stream was updated."
            : "Quest Multi Stream was installed.";
    }

    private static string BuildCompletionDetail(PreviewReleaseInstallResult installResult, string? launchDetail)
    {
        var installDetail = installResult.UpdatedExistingInstall
            ? $"The existing portable install at {installResult.InstallDirectory} was replaced with the latest public preview."
            : $"The portable preview was installed to {installResult.InstallDirectory}.";

        return string.IsNullOrWhiteSpace(launchDetail)
            ? installDetail
            : $"{installDetail} {launchDetail}";
    }
}
