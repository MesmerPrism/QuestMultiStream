<#
.SYNOPSIS
    Creates a clean Desktop and Start Menu launcher for Quest Multi Stream.
#>
[CmdletBinding()]
param(
    [string]$DesktopPath = [Environment]::GetFolderPath('Desktop'),
    [string]$StartMenuPath = [Environment]::GetFolderPath('Programs'),
    [string]$ShortcutName = 'Quest Multi Stream.lnk',
    [switch]$RefreshPublishedBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$launcherScriptPath = Join-Path $repoRoot 'tools\app\Start-Desktop-App.ps1'
$iconPath = Join-Path $repoRoot 'src\QuestMultiStream.App\Assets\quest-multi-stream.ico'
$powershellHost = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'

if (-not (Test-Path $launcherScriptPath)) {
    throw "Launcher script not found at $launcherScriptPath."
}

if (-not (Test-Path $iconPath)) {
    throw "Launcher icon not found at $iconPath."
}

if (-not (Test-Path $powershellHost)) {
    throw "PowerShell host was not found at $powershellHost."
}

$arguments = "-NoLogo -NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File `"$launcherScriptPath`""
if ($RefreshPublishedBuild) {
    $arguments += ' -Refresh'
}

$obsoleteLaunchers = @(
    'Launch Quest Multi Stream.cmd',
    'Quest Multi Stream Launcher.lnk',
    'Quest Multi Stream.lnk'
)

function Remove-ObsoleteLaunchers {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RootPath
    )

    if (-not (Test-Path $RootPath)) {
        return
    }

    foreach ($name in $obsoleteLaunchers) {
        $candidate = Join-Path $RootPath $name
        if (Test-Path $candidate) {
            Remove-Item $candidate -Force
        }
    }
}

function New-LauncherShortcut {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Shell,
        [Parameter(Mandatory = $true)]
        [string]$ShortcutPath
    )

    New-Item -ItemType Directory -Force -Path (Split-Path $ShortcutPath -Parent) | Out-Null

    $shortcut = $Shell.CreateShortcut($ShortcutPath)
    $shortcut.TargetPath = $powershellHost
    $shortcut.Arguments = $arguments
    $shortcut.WorkingDirectory = $repoRoot
    $shortcut.IconLocation = "$iconPath,0"
    $shortcut.Description = 'Launch Quest Multi Stream via the published desktop path with the bundled app icon'
    $shortcut.Save()

    return $Shell.CreateShortcut($ShortcutPath)
}

Remove-ObsoleteLaunchers -RootPath $DesktopPath
Remove-ObsoleteLaunchers -RootPath $StartMenuPath

$shell = New-Object -ComObject WScript.Shell
$desktopShortcutPath = Join-Path $DesktopPath $ShortcutName
$startMenuShortcutPath = Join-Path $StartMenuPath $ShortcutName
$desktopShortcut = New-LauncherShortcut -Shell $shell -ShortcutPath $desktopShortcutPath
$startMenuShortcut = New-LauncherShortcut -Shell $shell -ShortcutPath $startMenuShortcutPath

[PSCustomObject]@{
    DesktopShortcut = $desktopShortcutPath
    StartMenuShortcut = $startMenuShortcutPath
    TargetPath = $desktopShortcut.TargetPath
    Arguments = $desktopShortcut.Arguments
    WorkingDirectory = $desktopShortcut.WorkingDirectory
    IconLocation = $desktopShortcut.IconLocation
}
