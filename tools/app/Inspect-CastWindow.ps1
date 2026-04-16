param(
    [switch]$FixEmbeddedSize
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$source = @'
using System;
using System.Text;
using System.Runtime.InteropServices;
using System.Collections.Generic;

public static class CastWindowInspectorNative
{
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr parentHandle, EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr windowHandle, out RECT rect);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr windowHandle, StringBuilder className, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr windowHandle, StringBuilder title, int maxCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr windowHandle, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr windowHandle);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr windowHandle,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    public static IntPtr[] GetDescendants(IntPtr rootHandle)
    {
        var list = new List<IntPtr>();
        EnumChildWindows(rootHandle, (child, lParam) =>
        {
            list.Add(child);
            return true;
        }, IntPtr.Zero);
        return list.ToArray();
    }

    public static RECT GetRect(IntPtr handle)
    {
        RECT rect;
        GetWindowRect(handle, out rect);
        return rect;
    }

    public static string GetClass(IntPtr handle)
    {
        var builder = new StringBuilder(128);
        GetClassName(handle, builder, builder.Capacity);
        return builder.ToString();
    }

    public static string GetTitle(IntPtr handle)
    {
        var builder = new StringBuilder(256);
        GetWindowText(handle, builder, builder.Capacity);
        return builder.ToString();
    }

    public static uint GetProcessId(IntPtr handle)
    {
        uint processId;
        GetWindowThreadProcessId(handle, out processId);
        return processId;
    }

    public static bool IsVisible(IntPtr handle)
    {
        return IsWindowVisible(handle);
    }

    public static bool Resize(IntPtr handle, int width, int height)
    {
        return SetWindowPos(handle, IntPtr.Zero, 0, 0, width, height, 0x0004 | 0x0010 | 0x0040);
    }
}
'@

Add-Type -TypeDefinition $source

function Convert-Rect {
    param($Rect)

    [pscustomobject]@{
        X      = $Rect.Left
        Y      = $Rect.Top
        Width  = $Rect.Right - $Rect.Left
        Height = $Rect.Bottom - $Rect.Top
    }
}

$appProcesses = Get-Process QuestMultiStream.App -ErrorAction SilentlyContinue |
    Where-Object { $_.MainWindowHandle -ne 0 }

if (-not $appProcesses) {
    Write-Warning "No visible Quest Multi Stream app window found."
    return
}

foreach ($process in $appProcesses) {
    $rootHandle = [IntPtr]$process.MainWindowHandle
    $descendants = [CastWindowInspectorNative]::GetDescendants($rootHandle)

    $hostHandle = $descendants |
        Where-Object { [CastWindowInspectorNative]::GetClass($_) -eq "Static" } |
        Select-Object -First 1

    $scrcpyHandle = $descendants |
        Where-Object { [CastWindowInspectorNative]::GetClass($_) -eq "SDL_app" } |
        Select-Object -First 1

    $rootRect = Convert-Rect ([CastWindowInspectorNative]::GetRect($rootHandle))
    $hostRect = $null
    $scrcpyRect = $null
    $fixed = $false

    if ($hostHandle -ne $null -and $hostHandle -ne [IntPtr]::Zero) {
        $hostRect = Convert-Rect ([CastWindowInspectorNative]::GetRect($hostHandle))
    }

    if ($scrcpyHandle -ne $null -and $scrcpyHandle -ne [IntPtr]::Zero) {
        $scrcpyRect = Convert-Rect ([CastWindowInspectorNative]::GetRect($scrcpyHandle))
    }

    $sizeMismatch = $false
    if ($hostRect -and $scrcpyRect) {
        $sizeMismatch = $hostRect.Width -ne $scrcpyRect.Width -or $hostRect.Height -ne $scrcpyRect.Height
        if ($FixEmbeddedSize -and $sizeMismatch) {
            $fixed = [CastWindowInspectorNative]::Resize($scrcpyHandle, $hostRect.Width, $hostRect.Height)
            Start-Sleep -Milliseconds 150
            $scrcpyRect = Convert-Rect ([CastWindowInspectorNative]::GetRect($scrcpyHandle))
            $sizeMismatch = $hostRect.Width -ne $scrcpyRect.Width -or $hostRect.Height -ne $scrcpyRect.Height
        }
    }

    [pscustomobject]@{
        ProcessId           = $process.Id
        WindowTitle         = $process.MainWindowTitle
        RootHandle          = ('0x{0:X}' -f $rootHandle.ToInt64())
        RootSize            = if ($rootRect) { '{0}x{1}' -f $rootRect.Width, $rootRect.Height } else { $null }
        EmbeddedHostHandle  = if ($hostHandle) { '0x{0:X}' -f $hostHandle.ToInt64() } else { $null }
        EmbeddedHostSize    = if ($hostRect) { '{0}x{1}' -f $hostRect.Width, $hostRect.Height } else { $null }
        ScrcpyChildHandle   = if ($scrcpyHandle) { '0x{0:X}' -f $scrcpyHandle.ToInt64() } else { $null }
        ScrcpyChildSize     = if ($scrcpyRect) { '{0}x{1}' -f $scrcpyRect.Width, $scrcpyRect.Height } else { $null }
        ScrcpyChildProcess  = if ($scrcpyHandle) { [CastWindowInspectorNative]::GetProcessId($scrcpyHandle) } else { $null }
        ScrcpyChildVisible  = if ($scrcpyHandle) { [CastWindowInspectorNative]::IsVisible($scrcpyHandle) } else { $false }
        SizeMismatch        = $sizeMismatch
        FixedThisRun        = $fixed
    }
}
