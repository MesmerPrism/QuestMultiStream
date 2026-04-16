param(
    [string]$OutputDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$artifactRoot = Join-Path $repoRoot 'artifacts\visual-tests'
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $OutputDirectory = Join-Path $artifactRoot $stamp
}

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

Add-Type -AssemblyName System.Drawing

if ('QuestMultiStreamVisualCaptureNative' -as [type]) {
    # Reuse the loaded type when the script is invoked repeatedly in one session.
}
else {
    Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Runtime.InteropServices;
using System.Collections.Generic;

public static class QuestMultiStreamVisualCaptureNative
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
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindowVisible(IntPtr windowHandle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr windowHandle, StringBuilder title, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr windowHandle, StringBuilder className, int maxCount);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(IntPtr windowHandle, out RECT rect);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr windowHandle, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(IntPtr windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool BringWindowToTop(IntPtr windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ShowWindow(IntPtr windowHandle, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsIconic(IntPtr windowHandle);

    public static IntPtr[] GetTopLevelWindows()
    {
        var windows = new List<IntPtr>();
        EnumWindows((handle, lParam) =>
        {
            windows.Add(handle);
            return true;
        }, IntPtr.Zero);
        return windows.ToArray();
    }

    public static string GetTitle(IntPtr handle)
    {
        var builder = new StringBuilder(512);
        GetWindowText(handle, builder, builder.Capacity);
        return builder.ToString();
    }

    public static string GetClass(IntPtr handle)
    {
        var builder = new StringBuilder(256);
        GetClassName(handle, builder, builder.Capacity);
        return builder.ToString();
    }

    public static uint GetProcessId(IntPtr handle)
    {
        uint processId;
        GetWindowThreadProcessId(handle, out processId);
        return processId;
    }
}
'@
}

function Convert-ToSlug {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Value
    )

    $slug = ($Value -replace '[^A-Za-z0-9]+', '-').Trim('-')
    if ([string]::IsNullOrWhiteSpace($slug)) {
        return 'window'
    }

    return $slug.ToLowerInvariant()
}

function Get-WindowTargets {
    $processesById = @{}
    Get-Process -Name 'QuestMultiStream.App', 'scrcpy' -ErrorAction SilentlyContinue | ForEach-Object {
        $processesById[$_.Id] = $_
    }

    $targets = foreach ($handle in [QuestMultiStreamVisualCaptureNative]::GetTopLevelWindows()) {
        if ($handle -eq [IntPtr]::Zero) {
            continue
        }

        if (-not [QuestMultiStreamVisualCaptureNative]::IsWindowVisible($handle)) {
            continue
        }

        $title = [QuestMultiStreamVisualCaptureNative]::GetTitle($handle)
        $className = [QuestMultiStreamVisualCaptureNative]::GetClass($handle)
        $processId = [int][QuestMultiStreamVisualCaptureNative]::GetProcessId($handle)
        $process = $processesById[$processId]

        if ($null -eq $process) {
            continue
        }

        $rect = New-Object QuestMultiStreamVisualCaptureNative+RECT
        if (-not [QuestMultiStreamVisualCaptureNative]::GetWindowRect($handle, [ref]$rect)) {
            continue
        }

        $width = $rect.Right - $rect.Left
        $height = $rect.Bottom - $rect.Top
        if ($width -lt 80 -or $height -lt 80) {
            continue
        }

        $role = switch ($process.ProcessName) {
            'QuestMultiStream.App' {
                if ($title -eq 'Quest Multi Stream') { 'app-main' } else { 'app-overlay' }
            }
            'scrcpy' { 'scrcpy-window' }
            default { 'window' }
        }

        [pscustomobject]@{
            Handle = $handle
            ProcessId = $processId
            ProcessName = $process.ProcessName
            Title = if ([string]::IsNullOrWhiteSpace($title)) { $className } else { $title }
            ClassName = $className
            Role = $role
            X = $rect.Left
            Y = $rect.Top
            Width = $width
            Height = $height
        }
    }

    return @(
        $targets |
            Sort-Object @{ Expression = { $_.Role } }, @{ Expression = { $_.ProcessId } }, @{ Expression = { $_.Title } }
    )
}

function Focus-Window {
    param(
        [Parameter(Mandatory = $true)]
        [IntPtr]$Handle
    )

    if ([QuestMultiStreamVisualCaptureNative]::IsIconic($Handle)) {
        [QuestMultiStreamVisualCaptureNative]::ShowWindow($Handle, 9) | Out-Null
    }
    else {
        [QuestMultiStreamVisualCaptureNative]::ShowWindow($Handle, 5) | Out-Null
    }

    [QuestMultiStreamVisualCaptureNative]::BringWindowToTop($Handle) | Out-Null
    [QuestMultiStreamVisualCaptureNative]::SetForegroundWindow($Handle) | Out-Null
    Start-Sleep -Milliseconds 250
}

function Save-WindowScreenshot {
    param(
        [Parameter(Mandatory = $true)]
        [pscustomobject]$Window,
        [Parameter(Mandatory = $true)]
        [string]$FilePath
    )

    $bitmap = New-Object System.Drawing.Bitmap $Window.Width, $Window.Height
    try {
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.CopyFromScreen($Window.X, $Window.Y, 0, 0, $bitmap.Size)
        }
        finally {
            $graphics.Dispose()
        }

        $bitmap.Save($FilePath, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $bitmap.Dispose()
    }
}

$targets = @(Get-WindowTargets)
if ($targets.Count -eq 0) {
    Write-Warning 'No visible Quest Multi Stream or scrcpy windows found.'
    return
}

$captured = [System.Collections.Generic.List[object]]::new()
$index = 1
foreach ($target in $targets) {
    Focus-Window -Handle $target.Handle

    $fileName = '{0:D2}-{1}-{2}.png' -f $index, $target.Role, (Convert-ToSlug -Value $target.Title)
    $filePath = Join-Path $OutputDirectory $fileName
    Save-WindowScreenshot -Window $target -FilePath $filePath

    $captured.Add([pscustomobject]@{
        File = $filePath
        Role = $target.Role
        Title = $target.Title
        ProcessName = $target.ProcessName
        ProcessId = $target.ProcessId
        ClassName = $target.ClassName
        Handle = ('0x{0:X}' -f $target.Handle.ToInt64())
        Bounds = [pscustomobject]@{
            X = $target.X
            Y = $target.Y
            Width = $target.Width
            Height = $target.Height
        }
    }) | Out-Null

    $index++
}

$manifestPath = Join-Path $OutputDirectory 'manifest.json'
$manifest = [pscustomobject]@{
    CapturedAt = (Get-Date).ToString('o')
    OutputDirectory = $OutputDirectory
    WindowCount = $captured.Count
    Windows = $captured
}
$manifest | ConvertTo-Json -Depth 6 | Set-Content -Path $manifestPath -Encoding utf8

[pscustomobject]@{
    OutputDirectory = $OutputDirectory
    ManifestPath = $manifestPath
    WindowCount = $captured.Count
    Windows = $captured
}
