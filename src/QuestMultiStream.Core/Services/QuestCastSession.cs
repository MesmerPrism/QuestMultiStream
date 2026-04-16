using System.Diagnostics;
using QuestMultiStream.Core.Models;

namespace QuestMultiStream.Core.Services;

public sealed class QuestCastSession : IDisposable
{
    private readonly Process _process;
    private bool _stopRequested;

    public QuestCastSession(
        QuestDevice device,
        string windowTitle,
        ScrcpyLaunchProfile launchProfile,
        Process process)
    {
        Device = device;
        WindowTitle = windowTitle;
        LaunchProfile = launchProfile;
        _process = process;
        StartedAt = DateTimeOffset.Now;
        State = QuestCastSessionState.Starting;
        LastMessage = "Launching scrcpy.";
    }

    public QuestDevice Device { get; }

    public string WindowTitle { get; }

    public ScrcpyLaunchProfile LaunchProfile { get; }

    public DateTimeOffset StartedAt { get; }

    public QuestCastSessionState State { get; private set; }

    public string LastMessage { get; private set; }

    public int? ExitCode { get; private set; }

    public IntPtr WindowHandle { get; private set; }

    public int ProcessId => _process.Id;

    public bool IsActive => State is QuestCastSessionState.Starting or QuestCastSessionState.Running;

    public bool HasWindow => WindowHandle != IntPtr.Zero;

    public void MarkRunning()
    {
        if (State == QuestCastSessionState.Starting)
        {
            State = QuestCastSessionState.Running;
            LastMessage = "Cast window is live.";
        }
    }

    public void RecordOutput(string line)
    {
        if (!string.IsNullOrWhiteSpace(line))
        {
            LastMessage = line.Trim();
        }
    }

    public void RequestStop()
    {
        _stopRequested = true;
        LastMessage = "Stopping scrcpy.";
    }

    public void MarkExited()
    {
        try
        {
            if (!_process.HasExited)
            {
                return;
            }

            ExitCode = _process.ExitCode;
        }
        catch (InvalidOperationException)
        {
            ExitCode = null;
        }

        State = _stopRequested
            ? QuestCastSessionState.Stopped
            : ExitCode == 0
                ? QuestCastSessionState.Exited
                : QuestCastSessionState.Failed;
        LastMessage = ExitCode is null
            ? "scrcpy exited."
            : $"scrcpy exited with code {ExitCode}.";
    }

    public bool RefreshWindowHandle()
    {
        if (State is QuestCastSessionState.Stopped or QuestCastSessionState.Exited or QuestCastSessionState.Failed)
        {
            return WindowHandle != IntPtr.Zero && NativeMethods.IsWindow(WindowHandle);
        }

        var titledWindow = NativeMethods.FindWindow(null, WindowTitle);
        if (titledWindow != IntPtr.Zero && NativeMethods.IsWindowVisible(titledWindow))
        {
            WindowHandle = titledWindow;
            return true;
        }

        if (WindowHandle != IntPtr.Zero &&
            NativeMethods.IsWindow(WindowHandle) &&
            NativeMethods.IsWindowVisible(WindowHandle))
        {
            return true;
        }

        try
        {
            _process.Refresh();
            if (_process.MainWindowHandle != IntPtr.Zero &&
                NativeMethods.IsWindowVisible(_process.MainWindowHandle))
            {
                WindowHandle = _process.MainWindowHandle;
                return true;
            }
        }
        catch (InvalidOperationException)
        {
            return WindowHandle != IntPtr.Zero;
        }

        return false;
    }

    public bool TryMove(WindowLayoutBounds bounds)
    {
        if (!RefreshWindowHandle() || WindowHandle == IntPtr.Zero)
        {
            return false;
        }

        return NativeMethods.MoveWindow(
            WindowHandle,
            bounds.X,
            bounds.Y,
            bounds.Width,
            bounds.Height,
            true);
    }

    public bool TryGetBounds(out WindowLayoutBounds bounds)
    {
        bounds = new WindowLayoutBounds(0, 0, 0, 0);

        if (!RefreshWindowHandle() || WindowHandle == IntPtr.Zero)
        {
            return false;
        }

        if (!NativeMethods.GetWindowRect(WindowHandle, out var rect))
        {
            return false;
        }

        bounds = new WindowLayoutBounds(
            rect.Left,
            rect.Top,
            Math.Max(1, rect.Right - rect.Left),
            Math.Max(1, rect.Bottom - rect.Top));
        return true;
    }

    public bool TryGetClientBounds(out WindowLayoutBounds bounds)
    {
        bounds = new WindowLayoutBounds(0, 0, 0, 0);

        if (!RefreshWindowHandle() || WindowHandle == IntPtr.Zero)
        {
            return false;
        }

        if (!NativeMethods.GetClientRect(WindowHandle, out var clientRect))
        {
            return false;
        }

        var topLeft = new NativeMethods.Point { X = clientRect.Left, Y = clientRect.Top };
        var bottomRight = new NativeMethods.Point { X = clientRect.Right, Y = clientRect.Bottom };

        if (!NativeMethods.ClientToScreen(WindowHandle, ref topLeft) ||
            !NativeMethods.ClientToScreen(WindowHandle, ref bottomRight))
        {
            return false;
        }

        bounds = new WindowLayoutBounds(
            topLeft.X,
            topLeft.Y,
            Math.Max(1, bottomRight.X - topLeft.X),
            Math.Max(1, bottomRight.Y - topLeft.Y));
        return true;
    }

    public bool TrySetTopmost(bool isTopmost)
    {
        if (!RefreshWindowHandle() || WindowHandle == IntPtr.Zero)
        {
            return false;
        }

        return NativeMethods.SetWindowPos(
            WindowHandle,
            isTopmost ? NativeMethods.HwndTopmost : NativeMethods.HwndNotTopmost,
            0,
            0,
            0,
            0,
            NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate);
    }

    public bool TryMinimizeWindow()
        => RefreshWindowHandle() &&
           WindowHandle != IntPtr.Zero &&
           NativeMethods.ShowWindow(WindowHandle, NativeMethods.SwShowMinimized);

    public bool TryRestoreWindow()
        => RefreshWindowHandle() &&
           WindowHandle != IntPtr.Zero &&
           NativeMethods.ShowWindow(WindowHandle, NativeMethods.SwRestore);

    public bool IsWindowMinimized()
        => RefreshWindowHandle() &&
           WindowHandle != IntPtr.Zero &&
           NativeMethods.IsIconic(WindowHandle);

    public bool IsWindowVisible()
        => RefreshWindowHandle() &&
           WindowHandle != IntPtr.Zero &&
           NativeMethods.IsWindowVisible(WindowHandle);

    public async Task<bool> WaitForExitAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linkedCancellation.CancelAfter(timeout);

        try
        {
            await _process.WaitForExitAsync(linkedCancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (!_process.HasExited)
                {
                    return false;
                }
            }
            catch (InvalidOperationException)
            {
            }
        }
        catch (InvalidOperationException)
        {
        }

        MarkExited();
        return true;
    }

    public void Stop()
    {
        RequestStop();

        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }

    public void Dispose()
    {
        _process.Dispose();
    }
}
