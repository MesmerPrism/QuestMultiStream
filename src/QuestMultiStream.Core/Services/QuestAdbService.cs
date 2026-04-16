using System.Diagnostics;
using System.Text.RegularExpressions;
using QuestMultiStream.Core.Models;
using QuestMultiStream.Core.Parsing;

namespace QuestMultiStream.Core.Services;

public sealed partial class QuestAdbService
{
    private readonly string _adbPath;

    public QuestAdbService(string adbPath)
    {
        _adbPath = adbPath ?? throw new ArgumentNullException(nameof(adbPath));
    }

    public async Task<IReadOnlyList<QuestDevice>> GetDevicesAsync(CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(new[] { "devices", "-l" }, cancellationToken).ConfigureAwait(false);
        return result.ExitCode == 0
            ? AdbDeviceParser.Parse(result.StandardOutput)
            : Array.Empty<QuestDevice>();
    }

    public async Task<string?> EnableWirelessAsync(string serial, CancellationToken cancellationToken = default)
    {
        await RunAsync(new[] { "-s", serial, "tcpip", "5555" }, cancellationToken).ConfigureAwait(false);
        var ipAddress = await GetIpAddressAsync(serial, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(ipAddress))
        {
            return null;
        }

        var endpoint = $"{ipAddress}:5555";
        await RunAsync(new[] { "connect", endpoint }, cancellationToken).ConfigureAwait(false);
        return endpoint;
    }

    public Task WakeAsync(string serial, CancellationToken cancellationToken = default)
        => RunShellAsync(serial, ["input", "keyevent", "KEYCODE_WAKEUP"], cancellationToken);

    public Task DisableProximityAsync(string serial, CancellationToken cancellationToken = default)
        => RunShellAsync(serial, ["am", "broadcast", "-a", "com.oculus.vrpowermanager.prox_close"], cancellationToken);

    public Task EnableProximityAsync(string serial, CancellationToken cancellationToken = default)
        => RunShellAsync(serial, ["am", "broadcast", "-a", "com.oculus.vrpowermanager.automation_disable"], cancellationToken);

    public async Task<QuestProximityMode> GetProximityModeAsync(string serial, CancellationToken cancellationToken = default)
    {
        var result = await RunShellAsync(serial, ["dumpsys", "activity", "broadcasts"], cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            return QuestProximityMode.Unknown;
        }

        return QuestProximityStateParser.Parse($"{result.StandardOutput}{Environment.NewLine}{result.StandardError}");
    }

    private async Task<string?> GetIpAddressAsync(string serial, CancellationToken cancellationToken)
    {
        var result = await RunShellAsync(serial, ["ip", "route"], cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            return null;
        }

        var match = IpAddressPattern().Match(result.StandardOutput);
        return match.Success ? match.Groups["ip"].Value : null;
    }

    private Task<ProcessCommandResult> RunShellAsync(
        string serial,
        IReadOnlyList<string> shellArguments,
        CancellationToken cancellationToken)
    {
        var arguments = new List<string> { "-s", serial, "shell" };
        arguments.AddRange(shellArguments);
        return RunAsync(arguments, cancellationToken);
    }

    private async Task<ProcessCommandResult> RunAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _adbPath,
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

    [GeneratedRegex(@"src\s+(?<ip>\d{1,3}(?:\.\d{1,3}){3})", RegexOptions.Compiled)]
    private static partial Regex IpAddressPattern();
}
