using QuestMultiStream.App.ViewModels;
using QuestMultiStream.Core.Models;
using QuestMultiStream.Core.Services;
using System.Runtime.InteropServices;

namespace QuestMultiStream.App;

internal sealed class CastControlWindowManager
{
    private const int MinimumVisibleWidth = 720;
    private const int MinimumVisibleHeight = 460;
    private const int SmXVirtualScreen = 76;
    private const int SmYVirtualScreen = 77;
    private const int SmCxVirtualScreen = 78;
    private const int SmCyVirtualScreen = 79;
    private readonly Func<string, Task> _stopSessionAsync;
    private readonly Func<string, WindowLayoutBounds, Task> _resizeSessionAsync;
    private readonly Dictionary<string, CastControlWindow> _windows = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, WindowLayoutBounds> _rememberedBounds = new(StringComparer.OrdinalIgnoreCase);

    public CastControlWindowManager(
        Func<string, Task> stopSessionAsync,
        Func<string, WindowLayoutBounds, Task> resizeSessionAsync)
    {
        _stopSessionAsync = stopSessionAsync ?? throw new ArgumentNullException(nameof(stopSessionAsync));
        _resizeSessionAsync = resizeSessionAsync ?? throw new ArgumentNullException(nameof(resizeSessionAsync));
    }

    public void Sync(
        IReadOnlyList<QuestCastSession> sessions,
        IReadOnlyList<DeviceRowViewModel> devices)
    {
        var rowsBySerial = devices.ToDictionary(device => device.Serial, StringComparer.OrdinalIgnoreCase);
        var activeSessions = sessions
            .Where(session => session.IsActive)
            .Where(session => session.RefreshWindowHandle())
            .Where(session => rowsBySerial.ContainsKey(session.Device.Serial))
            .ToArray();

        var activeSerials = activeSessions
            .Select(session => session.Device.Serial)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var session in activeSessions)
        {
            var serial = session.Device.Serial;
            var row = rowsBySerial[serial];

            if (_windows.TryGetValue(serial, out var window))
            {
                window.Attach(row, session);
                continue;
            }

            try
            {
                window = new CastControlWindow(row, session);
                window.CloseRequested += OnWindowCloseRequested;
                window.ResizeRequested += OnWindowResizeRequested;
                _windows[serial] = window;

                if (_rememberedBounds.TryGetValue(serial, out var rememberedBounds))
                {
                    window.ApplyBounds(NormalizeBounds(rememberedBounds));
                }

                window.Show();
            }
            catch
            {
                session.TryRestoreWindow();
                session.TrySetTopmost(row.IsCastWindowPinned);
            }
        }

        foreach (var serial in _windows.Keys.Except(activeSerials, StringComparer.OrdinalIgnoreCase).ToArray())
        {
            CloseWindow(serial);
        }
    }

    public string Arrange(WindowLayoutBounds area, WindowLayoutMode mode)
    {
        var windows = _windows
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => pair.Value)
            .ToArray();

        if (windows.Length == 0)
        {
            return "No active cast windows are ready to arrange.";
        }

        var slots = BuildLayoutSlots(area, windows.Length, mode);
        var moved = 0;

        for (var index = 0; index < windows.Length; index++)
        {
            if (windows[index].TryMove(slots[index]))
            {
                moved++;
            }
        }

        return $"Arranged {moved} cast window(s) in {mode} mode.";
    }

    public void CloseAll()
    {
        foreach (var serial in _windows.Keys.ToArray())
        {
            CloseWindow(serial);
        }

        _windows.Clear();
    }

    private async void OnWindowCloseRequested(object? sender, string serial)
    {
        try
        {
            await _stopSessionAsync(serial).ConfigureAwait(false);
        }
        catch
        {
        }
    }

    private async void OnWindowResizeRequested(object? sender, WindowLayoutBounds bounds)
    {
        if (sender is not CastControlWindow window)
        {
            return;
        }

        try
        {
            var normalizedBounds = NormalizeBounds(bounds);
            _rememberedBounds[window.Serial] = normalizedBounds;
            await _resizeSessionAsync(window.Serial, normalizedBounds).ConfigureAwait(false);
        }
        catch
        {
        }
    }

    private void CloseWindow(string serial)
    {
        if (!_windows.TryGetValue(serial, out var window))
        {
            return;
        }

        if (window.TryGetWindowBounds(out var bounds))
        {
            _rememberedBounds[serial] = NormalizeBounds(bounds);
        }

        window.CloseRequested -= OnWindowCloseRequested;
        window.ResizeRequested -= OnWindowResizeRequested;
        window.CloseFromManager();
        _windows.Remove(serial);
    }

    private static WindowLayoutBounds NormalizeBounds(WindowLayoutBounds bounds)
    {
        var virtualScreen = GetVirtualScreenBounds();
        var width = Math.Clamp(bounds.Width, MinimumVisibleWidth, Math.Max(MinimumVisibleWidth, virtualScreen.Width));
        var height = Math.Clamp(bounds.Height, MinimumVisibleHeight, Math.Max(MinimumVisibleHeight, virtualScreen.Height));
        var maxX = (virtualScreen.X + virtualScreen.Width) - width;
        var maxY = (virtualScreen.Y + virtualScreen.Height) - height;
        var x = Math.Clamp(bounds.X, virtualScreen.X, maxX);
        var y = Math.Clamp(bounds.Y, virtualScreen.Y, maxY);
        return new WindowLayoutBounds(x, y, width, height);
    }

    private static WindowLayoutBounds GetVirtualScreenBounds()
    {
        var x = GetSystemMetrics(SmXVirtualScreen);
        var y = GetSystemMetrics(SmYVirtualScreen);
        var width = Math.Max(1, GetSystemMetrics(SmCxVirtualScreen));
        var height = Math.Max(1, GetSystemMetrics(SmCyVirtualScreen));
        return new WindowLayoutBounds(x, y, width, height);
    }

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

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

        var width = Math.Max(320, (area.Width - (spacing * (columns - 1))) / columns);
        var height = Math.Max(240, (area.Height - (spacing * (rows - 1))) / rows);
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
