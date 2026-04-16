using System.Diagnostics;
using QuestMultiStream.Core.Models;

namespace QuestMultiStream.Core.Services;

public sealed class QuestCastSessionManager : IDisposable
{
    private readonly object _sync = new();
    private readonly RepositoryPaths _paths;
    private readonly Dictionary<string, QuestCastSession> _sessions = new(StringComparer.OrdinalIgnoreCase);

    public QuestCastSessionManager(RepositoryPaths? paths = null)
    {
        _paths = paths ?? RepositoryPaths.Discover();
    }

    public event EventHandler<OperatorLogEntry>? LogRaised;

    public DependencySnapshot GetDependencySnapshot()
        => ToolingLocator.Detect(_paths);

    public async Task<IReadOnlyList<QuestDevice>> GetDevicesAsync(CancellationToken cancellationToken = default)
    {
        var dependencies = GetDependencySnapshot();
        if (!dependencies.HasAdb || dependencies.AdbPath is null)
        {
            return Array.Empty<QuestDevice>();
        }

        var adbService = new QuestAdbService(dependencies.AdbPath);
        return await adbService.GetDevicesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ScrcpyCaptureTarget>> GetCaptureTargetsAsync(
        QuestDevice device,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);

        var dependencies = GetDependencySnapshot();
        if (!device.IsAvailable || !dependencies.HasScrcpy || dependencies.ScrcpyPath is null)
        {
            return [ScrcpyCaptureTarget.DefaultDisplay];
        }

        try
        {
            var probeService = new ScrcpyProbeService(dependencies.ScrcpyPath);
            return await probeService.GetCaptureTargetsAsync(device.Serial, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log("warning", $"Could not enumerate capture sources for {device.DisplayName}.", ex.Message);
            return [ScrcpyCaptureTarget.DefaultDisplay];
        }
    }

    public async Task<QuestProximityMode> GetProximityModeAsync(
        QuestDevice device,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);

        if (!device.IsAvailable)
        {
            return QuestProximityMode.Unknown;
        }

        try
        {
            var adb = CreateAdbService();
            return await adb.GetProximityModeAsync(device.Serial, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log("warning", $"Could not read proximity state for {device.DisplayName}.", ex.Message);
            return QuestProximityMode.Unknown;
        }
    }

    public IReadOnlyList<QuestCastSession> GetSessions()
    {
        lock (_sync)
        {
            return _sessions.Values
                .OrderBy(session => session.Device.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    public void RefreshSessions()
    {
        foreach (var session in GetSessions())
        {
            session.RefreshWindowHandle();
            if (!session.IsActive)
            {
                continue;
            }

            try
            {
                if (Process.GetProcessById(session.ProcessId).HasExited)
                {
                    session.MarkExited();
                }
            }
            catch (ArgumentException)
            {
                session.MarkExited();
            }
            catch (InvalidOperationException)
            {
                session.MarkExited();
            }
        }
    }

    public async Task<QuestCastSession> StartSessionAsync(
        QuestDevice device,
        ScrcpyLaunchProfile launchProfile,
        CancellationToken cancellationToken = default)
    {
        var existing = TryGetSession(device.Serial);
        if (existing is not null && existing.IsActive)
        {
            Log("info", $"Cast already active for {device.DisplayName}.", device.Serial);
            return existing;
        }

        if (existing is not null)
        {
            RemoveSession(device.Serial, existing);
        }

        var dependencies = GetDependencySnapshot();
        if (!dependencies.HasScrcpy || dependencies.ScrcpyPath is null)
        {
            throw new InvalidOperationException(dependencies.Guidance);
        }

        await TryWakeBeforeStartAsync(device, cancellationToken).ConfigureAwait(false);

        var windowTitle = BuildWindowTitle(device);
        var startInfo = new ProcessStartInfo
        {
            FileName = dependencies.ScrcpyPath,
            WorkingDirectory = Path.GetDirectoryName(dependencies.ScrcpyPath) ?? _paths.AppBaseDirectory,
            UseShellExecute = true,
            CreateNoWindow = false,
            WindowStyle = ProcessWindowStyle.Hidden
        };

        foreach (var argument in ScrcpyArgumentBuilder.Build(device, windowTitle, launchProfile))
        {
            startInfo.ArgumentList.Add(argument);
        }

        var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };

        if (!process.Start())
        {
            process.Dispose();
            throw new InvalidOperationException($"Failed to start scrcpy for {device.DisplayName}.");
        }

        var session = new QuestCastSession(device, windowTitle, launchProfile, process);

        process.Exited += (_, _) =>
        {
            session.MarkExited();
            Log("info", $"{device.DisplayName} cast ended.", session.LastMessage);
        };

        lock (_sync)
        {
            _sessions[device.Serial] = session;
        }

        Log("info", $"Started cast for {device.DisplayName}.", device.Serial);

        var windowReady = false;
        for (var attempt = 0; attempt < 40; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!session.IsActive)
            {
                break;
            }

            if (session.RefreshWindowHandle())
            {
                session.MarkRunning();
                windowReady = true;
                break;
            }

            await Task.Delay(150, cancellationToken).ConfigureAwait(false);
        }

        if (!windowReady)
        {
            session.Stop();
            _ = await session.WaitForExitAsync(TimeSpan.FromSeconds(3), cancellationToken).ConfigureAwait(false);
            RemoveSession(device.Serial, session);
            throw new InvalidOperationException($"scrcpy did not open a cast window for {device.DisplayName}.");
        }

        return session;
    }

    public Task StopSessionAsync(string serial, CancellationToken cancellationToken = default)
        => StopSessionInternalAsync(serial, waitForExit: false, cancellationToken);

    public async Task<QuestCastSession> RestartSessionAsync(
        QuestDevice device,
        ScrcpyLaunchProfile launchProfile,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(launchProfile);

        await StopSessionInternalAsync(device.Serial, waitForExit: true, cancellationToken).ConfigureAwait(false);
        return await StartSessionAsync(device, launchProfile, cancellationToken).ConfigureAwait(false);
    }

    public async Task StopAllAsync(CancellationToken cancellationToken = default)
    {
        foreach (var session in GetSessions().Where(session => session.IsActive))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await StopSessionAsync(session.Device.Serial, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<string?> EnableWirelessAsync(string serial, CancellationToken cancellationToken = default)
    {
        var adb = CreateAdbService();
        var endpoint = await adb.EnableWirelessAsync(serial, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(endpoint))
        {
            Log("info", $"Enabled Wi-Fi ADB for {serial}.", endpoint);
        }

        return endpoint;
    }

    public async Task WakeAsync(string serial, CancellationToken cancellationToken = default)
    {
        var adb = CreateAdbService();
        await adb.WakeAsync(serial, cancellationToken).ConfigureAwait(false);
        Log("info", $"Sent wake command to {serial}.");
    }

    public async Task SetProximityEnabledAsync(string serial, bool enabled, CancellationToken cancellationToken = default)
    {
        var adb = CreateAdbService();
        if (enabled)
        {
            await adb.EnableProximityAsync(serial, cancellationToken).ConfigureAwait(false);
            Log("info", $"Re-enabled proximity automation for {serial}.");
        }
        else
        {
            await adb.DisableProximityAsync(serial, cancellationToken).ConfigureAwait(false);
            Log("info", $"Disabled proximity automation for {serial}.");
        }
    }

    public async Task SetProximityModeAsync(string serial, QuestProximityMode mode, CancellationToken cancellationToken = default)
    {
        var adb = CreateAdbService();
        switch (mode)
        {
            case QuestProximityMode.KeepAwake:
                await adb.DisableProximityAsync(serial, cancellationToken).ConfigureAwait(false);
                Log("info", $"Enabled keep-awake proximity override for {serial}.");
                break;
            default:
                await adb.EnableProximityAsync(serial, cancellationToken).ConfigureAwait(false);
                Log("info", $"Restored normal proximity behavior for {serial}.");
                break;
        }
    }

    public string ArrangeSessions(WindowLayoutBounds area, WindowLayoutMode mode)
    {
        RefreshSessions();

        var active = GetSessions()
            .Where(session => session.IsActive)
            .Where(session => session.RefreshWindowHandle())
            .ToArray();

        if (active.Length == 0)
        {
            return "No active cast windows are ready to arrange.";
        }

        var slots = BuildLayoutSlots(area, active.Length, mode);
        var moved = 0;

        for (var index = 0; index < active.Length; index++)
        {
            if (active[index].TryMove(slots[index]))
            {
                moved++;
            }
        }

        var summary = $"Arranged {moved} cast window(s) in {mode} mode.";
        Log("info", summary);
        return summary;
    }

    public void Dispose()
    {
        foreach (var session in GetSessions())
        {
            session.Stop();
            session.Dispose();
        }
    }

    private QuestAdbService CreateAdbService()
    {
        var dependencies = GetDependencySnapshot();
        if (!dependencies.HasAdb || dependencies.AdbPath is null)
        {
            throw new InvalidOperationException(dependencies.Guidance);
        }

        return new QuestAdbService(dependencies.AdbPath);
    }

    private async Task TryWakeBeforeStartAsync(QuestDevice device, CancellationToken cancellationToken)
    {
        try
        {
            await WakeAsync(device.Serial, cancellationToken).ConfigureAwait(false);
            await Task.Delay(300, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log("warning", $"Wake command failed for {device.DisplayName}; continuing cast launch.", ex.Message);
        }
    }

    private QuestCastSession? TryGetSession(string serial)
    {
        lock (_sync)
        {
            return _sessions.TryGetValue(serial, out var session) ? session : null;
        }
    }

    private async Task StopSessionInternalAsync(
        string serial,
        bool waitForExit,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var session = TryGetSession(serial);
        if (session is null)
        {
            return;
        }

        session.Stop();
        Log("info", $"Stopping cast for {session.Device.DisplayName}.", serial);

        if (waitForExit)
        {
            _ = await session.WaitForExitAsync(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
            RemoveSession(serial, session);
        }
    }

    private void RemoveSession(string serial, QuestCastSession session)
    {
        lock (_sync)
        {
            if (_sessions.TryGetValue(serial, out var current) && ReferenceEquals(current, session))
            {
                _sessions.Remove(serial);
            }
        }

        session.Dispose();
    }

    private static string BuildWindowTitle(QuestDevice device)
        => $"Quest Multi Stream · {device.DisplayName} · {device.Serial}";

    private void Log(string level, string message, string? detail = null)
        => LogRaised?.Invoke(this, new OperatorLogEntry(DateTimeOffset.Now, level, message, detail));

    private static IReadOnlyList<WindowLayoutBounds> BuildLayoutSlots(
        WindowLayoutBounds area,
        int count,
        WindowLayoutMode mode)
    {
        const int spacing = 12;

        var columns = mode switch
        {
            WindowLayoutMode.Row => count,
            WindowLayoutMode.Column => 1,
            _ => (int)Math.Ceiling(Math.Sqrt(count))
        };

        var rows = mode switch
        {
            WindowLayoutMode.Row => 1,
            WindowLayoutMode.Column => count,
            _ => (int)Math.Ceiling(count / (double)columns)
        };

        var width = Math.Max(240, (area.Width - (spacing * (columns - 1))) / columns);
        var height = Math.Max(180, (area.Height - (spacing * (rows - 1))) / rows);
        var slots = new List<WindowLayoutBounds>(count);

        for (var index = 0; index < count; index++)
        {
            var row = index / columns;
            var column = index % columns;
            slots.Add(new WindowLayoutBounds(
                area.X + (column * (width + spacing)),
                area.Y + (row * (height + spacing)),
                width,
                height));
        }

        return slots;
    }
}
