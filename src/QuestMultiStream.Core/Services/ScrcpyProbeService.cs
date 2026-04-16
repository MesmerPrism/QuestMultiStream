using System.Diagnostics;
using QuestMultiStream.Core.Models;
using QuestMultiStream.Core.Parsing;

namespace QuestMultiStream.Core.Services;

public sealed class ScrcpyProbeService
{
    private readonly string _scrcpyPath;

    public ScrcpyProbeService(string scrcpyPath)
    {
        _scrcpyPath = scrcpyPath ?? throw new ArgumentNullException(nameof(scrcpyPath));
    }

    public async Task<IReadOnlyList<ScrcpyCaptureTarget>> GetCaptureTargetsAsync(string serial, CancellationToken cancellationToken = default)
    {
        var displays = await RunAsync([$"--serial={serial}", "--list-displays"], cancellationToken).ConfigureAwait(false);
        var cameras = await RunAsync([$"--serial={serial}", "--list-cameras"], cancellationToken).ConfigureAwait(false);
        return ScrcpyCaptureTargetCatalog.Build(
            ScrcpyCaptureTargetParser.ParseDisplays(CombineOutput(displays)),
            ScrcpyCaptureTargetParser.ParseCameras(CombineOutput(cameras)));
    }

    private async Task<ProcessCommandResult> RunAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _scrcpyPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        var standardOutputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        return new ProcessCommandResult(
            process.ExitCode,
            await standardOutputTask.ConfigureAwait(false),
            await standardErrorTask.ConfigureAwait(false));
    }
    private static string CombineOutput(ProcessCommandResult result)
        => $"{result.StandardOutput}{Environment.NewLine}{result.StandardError}";
}
