[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [string]$Command = 'help',
    [ValidateSet('Auto', 'Published', 'BuiltExe', 'DotnetRun')]
    [string]$Mode = 'Auto',
    [ValidateSet('Debug', 'Release')]
    [string]$BuildConfiguration = 'Debug',
    [ValidateSet('Release')]
    [string]$PublishConfiguration = 'Release',
    [ValidateSet('win-x64')]
    [string]$RuntimeIdentifier = 'win-x64',
    [switch]$Refresh,
    [switch]$Wait,
    [switch]$FixEmbeddedSize,
    [switch]$RefreshPublishedBuild,
    [int]$KeepPublishedBuilds = 3,
    [int]$Tail = 40,
    [string]$OutputDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$projectPath = Join-Path $repoRoot 'src\QuestMultiStream.App\QuestMultiStream.App.csproj'
$publishRoot = Join-Path $repoRoot 'artifacts\publish\QuestMultiStream.App'
$logRoot = Join-Path $env:LOCALAPPDATA 'QuestMultiStream\logs'
$launcherLogPath = Join-Path $logRoot ('launcher-' + (Get-Date).ToString('yyyyMMdd') + '.log')

New-Item -ItemType Directory -Force -Path $logRoot | Out-Null

function Write-LauncherLog {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Level,
        [Parameter(Mandatory = $true)]
        [string]$Message
    )

    $timestamp = Get-Date -Format 'yyyy-MM-dd HH:mm:ss.fff'
    $line = "$timestamp [$Level] $Message$([Environment]::NewLine)"
    for ($attempt = 0; $attempt -lt 5; $attempt++) {
        try {
            [System.IO.File]::AppendAllText($launcherLogPath, $line, [System.Text.Encoding]::UTF8)
            return
        }
        catch [System.IO.IOException] {
            Start-Sleep -Milliseconds 150
        }
    }

    throw "Could not append to launcher log '$launcherLogPath' after repeated retries."
}

function Ensure-Win32Interop {
    if ('QuestMultiStreamLauncherNative' -as [type]) {
        return
    }

    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class QuestMultiStreamLauncherNative
{
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
}
'@
}

function Ensure-UiAutomationInterop {
    if ('System.Windows.Automation.AutomationElement' -as [type]) {
        return
    }

    Add-Type -AssemblyName UIAutomationClient
    Add-Type -AssemblyName UIAutomationTypes
}

function Get-RunningAppProcess {
    $process = Get-Process -Name 'QuestMultiStream.App' -ErrorAction SilentlyContinue |
        Sort-Object StartTime |
        Select-Object -First 1

    if ($null -eq $process) {
        return $null
    }

    try {
        $process.Refresh()
    }
    catch {
    }

    return $process
}

function Get-ProcessExecutablePath {
    param(
        [Parameter(Mandatory = $true)]
        [System.Diagnostics.Process]$Process
    )

    try {
        return $Process.Path
    }
    catch {
        return $null
    }
}

function Focus-AppProcess {
    param(
        [Parameter(Mandatory = $true)]
        [System.Diagnostics.Process]$Process
    )

    $windowHandle = $Process.MainWindowHandle
    if ($windowHandle -eq 0) {
        return $false
    }

    Ensure-Win32Interop
    if ([QuestMultiStreamLauncherNative]::IsIconic($windowHandle)) {
        [QuestMultiStreamLauncherNative]::ShowWindow($windowHandle, 9) | Out-Null
    }
    else {
        [QuestMultiStreamLauncherNative]::ShowWindow($windowHandle, 5) | Out-Null
    }

    [QuestMultiStreamLauncherNative]::BringWindowToTop($windowHandle) | Out-Null
    return [QuestMultiStreamLauncherNative]::SetForegroundWindow($windowHandle)
}

function Get-AppAutomationWindow {
    param(
        [Parameter(Mandatory = $true)]
        [System.Diagnostics.Process]$Process
    )

    Ensure-UiAutomationInterop

    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        try {
            $Process.Refresh()
        }
        catch {
        }

        if ($Process.MainWindowHandle -ne 0) {
            return [System.Windows.Automation.AutomationElement]::FromHandle($Process.MainWindowHandle)
        }

        Start-Sleep -Milliseconds 250
    }

    return $null
}

function Find-WindowButtons {
    param(
        [Parameter(Mandatory = $true)]
        [System.Windows.Automation.AutomationElement]$Window,
        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    $controlTypeCondition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Button)
    $nameCondition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty,
        $Name)
    $buttonCondition = New-Object System.Windows.Automation.AndCondition -ArgumentList @($controlTypeCondition, $nameCondition)

    return $Window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $buttonCondition)
}

function Invoke-WindowButton {
    param(
        [Parameter(Mandatory = $true)]
        [System.Windows.Automation.AutomationElement]$Button
    )

    $invokePattern = $Button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
    if ($null -eq $invokePattern) {
        throw "Button '$($Button.Current.Name)' does not expose InvokePattern."
    }

    $invokePattern.Invoke()
}

function Get-NewestInputWriteTimeUtc {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$RelativePaths
    )

    $newest = [DateTime]::MinValue
    foreach ($relativePath in $RelativePaths) {
        $fullPath = Join-Path $repoRoot $relativePath
        if (-not (Test-Path $fullPath)) {
            continue
        }

        $files = Get-ChildItem $fullPath -Recurse -File |
            Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' }

        foreach ($file in $files) {
            if ($file.LastWriteTimeUtc -gt $newest) {
                $newest = $file.LastWriteTimeUtc
            }
        }
    }

    return $newest
}

function Get-BuiltExePath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Configuration
    )

    return Join-Path $repoRoot "src\QuestMultiStream.App\bin\$Configuration\net10.0-windows\QuestMultiStream.App.exe"
}

function Find-OnPath {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$CommandNames
    )

    foreach ($commandName in $CommandNames) {
        try {
            $command = Get-Command $commandName -CommandType Application -ErrorAction Stop | Select-Object -First 1
            if ($command -and $command.Source) {
                return $command.Source
            }
        }
        catch {
        }
    }

    return $null
}

function Find-NewestExecutable {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Root,
        [Parameter(Mandatory = $true)]
        [string]$FileName
    )

    if (-not (Test-Path $Root)) {
        return $null
    }

    return Get-ChildItem -Path $Root -Recurse -Filter $FileName -File |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -ExpandProperty FullName -First 1
}

function Resolve-ScrcpyPath {
    $candidates = @(
        $env:QUEST_MULTI_STREAM_SCRCPY,
        (Find-NewestExecutable -Root (Join-Path $repoRoot 'tools\scrcpy') -FileName 'scrcpy.exe'),
        (Find-OnPath @('scrcpy.exe', 'scrcpy'))
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }

    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) {
            return $candidate
        }
    }

    return $null
}

function Resolve-AdbPath {
    $scrcpyPath = Resolve-ScrcpyPath
    $sdkRoots = @($env:ANDROID_SDK_ROOT, $env:ANDROID_HOME) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    $candidates = [System.Collections.Generic.List[string]]::new()

    if (-not [string]::IsNullOrWhiteSpace($env:QUEST_MULTI_STREAM_ADB)) {
        $null = $candidates.Add($env:QUEST_MULTI_STREAM_ADB)
    }

    if (-not [string]::IsNullOrWhiteSpace($scrcpyPath)) {
        $scrcpyDirectory = Split-Path $scrcpyPath
        $null = $candidates.Add((Join-Path $scrcpyDirectory 'adb.exe'))
    }

    $null = $candidates.Add((Join-Path $env:LOCALAPPDATA 'Android\Sdk\platform-tools\adb.exe'))
    foreach ($sdkRoot in $sdkRoots) {
        $null = $candidates.Add((Join-Path $sdkRoot 'platform-tools\adb.exe'))
    }

    $pathAdb = Find-OnPath @('adb.exe', 'adb')
    if (-not [string]::IsNullOrWhiteSpace($pathAdb)) {
        $null = $candidates.Add($pathAdb)
    }

    foreach ($candidate in $candidates) {
        if (-not [string]::IsNullOrWhiteSpace($candidate) -and (Test-Path $candidate)) {
            return $candidate
        }
    }

    return $null
}

function Get-LatestLogFile {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Prefix
    )

    if (-not (Test-Path $logRoot)) {
        return $null
    }

    return Get-ChildItem -Path $logRoot -Filter "$Prefix-*.log" -File |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -ExpandProperty FullName -First 1
}

function Invoke-NativeCapture {
    param(
        [Parameter(Mandatory = $true)]
        [string]$FilePath,
        [string[]]$Arguments = @()
    )

    try {
        $outputLines = & $FilePath @Arguments 2>&1
        $exitCode = $LASTEXITCODE
        $output = (($outputLines | Out-String).Trim())

        return [pscustomobject]@{
            Success = ($exitCode -eq 0)
            ExitCode = $exitCode
            Output = $output
        }
    }
    catch {
        return [pscustomobject]@{
            Success = $false
            ExitCode = $null
            Output = $_.Exception.Message
        }
    }
}

function Test-BuildIsStale {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Configuration
    )

    $exePath = Get-BuiltExePath -Configuration $Configuration
    if (-not (Test-Path $exePath)) {
        return $true
    }

    $builtAt = (Get-Item $exePath).LastWriteTimeUtc
    $inputAt = Get-NewestInputWriteTimeUtc @(
        'src\QuestMultiStream.App',
        'src\QuestMultiStream.Core'
    )

    return $inputAt -gt $builtAt
}

function Invoke-Build {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Configuration
    )

    if (-not $Refresh -and -not (Test-BuildIsStale -Configuration $Configuration)) {
        $exePath = Get-BuiltExePath -Configuration $Configuration
        Write-LauncherLog -Level 'INFO' -Message "Reusing existing $Configuration build at $exePath."
        return $exePath
    }

    Write-LauncherLog -Level 'INFO' -Message "Building QuestMultiStream.App ($Configuration)."
    & dotnet build $projectPath -c $Configuration | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build failed with exit code $LASTEXITCODE"
    }

    $builtExePath = Get-BuiltExePath -Configuration $Configuration
    if (-not (Test-Path $builtExePath)) {
        throw "Built executable not found at $builtExePath"
    }

    return $builtExePath
}

function Get-PublishedBuildDirectories {
    if (-not (Test-Path $publishRoot)) {
        return @()
    }

    return @(
        Get-ChildItem -LiteralPath $publishRoot -Directory |
            Sort-Object LastWriteTimeUtc -Descending
    )
}

function Get-LatestPublishedExePath {
    $directories = Get-PublishedBuildDirectories
    foreach ($directory in $directories) {
        $candidate = Join-Path $directory.FullName 'QuestMultiStream.App.exe'
        if (Test-Path $candidate) {
            return $candidate
        }
    }

    return $null
}

function Invoke-Publish {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Configuration,
        [Parameter(Mandatory = $true)]
        [string]$RuntimeIdentifier,
        [Parameter(Mandatory = $true)]
        [int]$KeepCount
    )

    New-Item -ItemType Directory -Force -Path $publishRoot | Out-Null
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $outputPath = Join-Path $publishRoot "$stamp-$Configuration-$RuntimeIdentifier"

    Write-LauncherLog -Level 'INFO' -Message "Publishing QuestMultiStream.App to $outputPath."
    & dotnet publish $projectPath `
        -c $Configuration `
        -r $RuntimeIdentifier `
        -p:PublishSingleFile=true `
        -p:SelfContained=false `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -o $outputPath | Out-Host

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE"
    }

    $exePath = Join-Path $outputPath 'QuestMultiStream.App.exe'
    if (-not (Test-Path $exePath)) {
        throw "Published executable not found at $exePath"
    }

    $directories = Get-PublishedBuildDirectories
    if ($KeepCount -gt 0 -and $directories.Count -gt $KeepCount) {
        $directories | Select-Object -Skip $KeepCount | ForEach-Object {
            try {
                Remove-Item -LiteralPath $_.FullName -Recurse -Force -ErrorAction Stop
                Write-LauncherLog -Level 'INFO' -Message "Pruned old publish directory $($_.FullName)."
            }
            catch {
                Write-LauncherLog -Level 'WARN' -Message "Could not prune publish directory $($_.FullName): $($_.Exception.Message)"
            }
        }
    }

    return $exePath
}

function Start-AppProcess {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ExecutablePath,
        [Parameter(Mandatory = $true)]
        [string]$WorkingDirectory
    )

    Write-LauncherLog -Level 'INFO' -Message "Starting executable $ExecutablePath."
    $process = Start-Process -FilePath $ExecutablePath -WorkingDirectory $WorkingDirectory -PassThru
    $process | Add-Member -NotePropertyName ExecutablePath -NotePropertyValue $ExecutablePath -Force
    if ($Wait) {
        Wait-Process -Id $process.Id
    }

    return $process
}

function Start-PublishedProcess {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Configuration,
        [Parameter(Mandatory = $true)]
        [string]$RuntimeIdentifier,
        [Parameter(Mandatory = $true)]
        [int]$KeepCount
    )

    $exePath = if ($Refresh) {
        Invoke-Publish -Configuration $Configuration -RuntimeIdentifier $RuntimeIdentifier -KeepCount $KeepCount
    }
    else {
        Get-LatestPublishedExePath
    }

    if ([string]::IsNullOrWhiteSpace($exePath) -or -not (Test-Path $exePath)) {
        if ($Refresh) {
            throw "No published executable was produced."
        }

        return $null
    }

    return Start-AppProcess -ExecutablePath $exePath -WorkingDirectory (Split-Path $exePath)
}

function Start-BuiltProcess {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Configuration
    )

    $exePath = Invoke-Build -Configuration $Configuration
    return Start-AppProcess -ExecutablePath $exePath -WorkingDirectory (Split-Path $exePath)
}

function Start-DotnetRunProcess {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Configuration
    )

    Write-LauncherLog -Level 'INFO' -Message "Falling back to dotnet run ($Configuration)."
    $dotnetExecutable = Find-OnPath @('dotnet.exe', 'dotnet')
    if ([string]::IsNullOrWhiteSpace($dotnetExecutable)) {
        $dotnetExecutable = 'dotnet'
    }

    $process = Start-Process -FilePath $dotnetExecutable `
        -ArgumentList @('run', '--project', $projectPath, '-c', $Configuration) `
        -WorkingDirectory $repoRoot `
        -PassThru
    $process | Add-Member -NotePropertyName ExecutablePath -NotePropertyValue $dotnetExecutable -Force

    if ($Wait) {
        Wait-Process -Id $process.Id
    }

    return $process
}

function Test-IsApplicationControlFailure {
    param(
        [Parameter(Mandatory = $true)]
        [System.Exception]$Exception
    )

    return $Exception.Message -match 'Application Control policy has blocked this file' -or
        $Exception.Message -match 'Code Integrity'
}

function Invoke-Run {
    $runningProcess = Get-RunningAppProcess
    if ($runningProcess) {
        $existingPath = Get-ProcessExecutablePath -Process $runningProcess
        Write-LauncherLog -Level 'INFO' -Message "Quest Multi Stream is already running as PID $($runningProcess.Id). Reusing the existing instance."
        if ($Refresh) {
            Write-LauncherLog -Level 'WARN' -Message 'Refresh was requested while the app was already running. Close the current instance before forcing a rebuild or republish.'
        }

        Focus-AppProcess -Process $runningProcess | Out-Null
        $runningProcess | Add-Member -NotePropertyName ExecutablePath -NotePropertyValue $existingPath -Force
        return $runningProcess
    }

    switch ($Mode) {
        'Published' {
            $process = Start-PublishedProcess -Configuration $PublishConfiguration -RuntimeIdentifier $RuntimeIdentifier -KeepCount $KeepPublishedBuilds
            if ($null -eq $process) {
                throw 'No published build is available. Run publish first or use -Refresh.'
            }

            return $process
        }
        'BuiltExe' {
            return Start-BuiltProcess -Configuration $BuildConfiguration
        }
        'DotnetRun' {
            return Start-DotnetRunProcess -Configuration $BuildConfiguration
        }
        default {
            try {
                $publishedProcess = Start-PublishedProcess -Configuration $PublishConfiguration -RuntimeIdentifier $RuntimeIdentifier -KeepCount $KeepPublishedBuilds
                if ($null -ne $publishedProcess) {
                    return $publishedProcess
                }
            }
            catch {
                Write-LauncherLog -Level 'WARN' -Message "Published launch failed: $($_.Exception.Message)"
                if (-not (Test-IsApplicationControlFailure -Exception $_.Exception)) {
                    Write-LauncherLog -Level 'WARN' -Message 'Falling back to built executable anyway.'
                }
            }

            try {
                return Start-BuiltProcess -Configuration $BuildConfiguration
            }
            catch {
                Write-LauncherLog -Level 'WARN' -Message "Built executable launch failed: $($_.Exception.Message)"
            }

            return Start-DotnetRunProcess -Configuration $BuildConfiguration
        }
    }
}

function Invoke-InspectCast {
    $scriptPath = Join-Path $repoRoot 'tools\app\Inspect-CastWindow.ps1'
    & $scriptPath -FixEmbeddedSize:$FixEmbeddedSize
}

function Invoke-RefreshShortcuts {
    $scriptPath = Join-Path $repoRoot 'tools\app\Refresh-Desktop-Launcher.ps1'
    & $scriptPath -RefreshPublishedBuild:$RefreshPublishedBuild
}

function Invoke-VisualTest {
    $runningProcess = Get-RunningAppProcess
    if (-not $runningProcess) {
        $null = Invoke-Run
        Start-Sleep -Seconds 3
    }

    $scriptPath = Join-Path $repoRoot 'tools\app\Capture-VisualState.ps1'
    if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
        & $scriptPath
    }
    else {
        & $scriptPath -OutputDirectory $OutputDirectory
    }
}

function Invoke-SmokeCast {
    $process = Invoke-Run
    Start-Sleep -Seconds 3

    $appProcess = if ($process -and $process.Id) {
        Get-Process -Id $process.Id -ErrorAction SilentlyContinue
    }
    else {
        Get-RunningAppProcess
    }

    if (-not $appProcess) {
        throw 'Quest Multi Stream is not running, so the cast smoke test cannot continue.'
    }

    Focus-AppProcess -Process $appProcess | Out-Null
    $window = Get-AppAutomationWindow -Process $appProcess
    if ($null -eq $window) {
        throw 'Could not bind UI automation to the Quest Multi Stream window.'
    }

    $startButtons = Find-WindowButtons -Window $window -Name 'Start Cast'
    $startButton = $null
    foreach ($candidate in $startButtons) {
        if (-not $candidate.Current.IsEnabled) {
            continue
        }

        if ($candidate.Current.IsOffscreen) {
            continue
        }

        $startButton = $candidate
        break
    }

    if ($null -eq $startButton) {
        throw "Could not find a 'Start Cast' button in the app window."
    }

    Invoke-WindowButton -Button $startButton
    Start-Sleep -Seconds 8

    $capture = Invoke-VisualTest
    $scrcpyWindows = @($capture.Windows | Where-Object { $_.Role -eq 'scrcpy-window' })

    [pscustomobject]@{
        Started = $scrcpyWindows.Count -gt 0
        ScrcpyWindowCount = $scrcpyWindows.Count
        OutputDirectory = $capture.OutputDirectory
        ManifestPath = $capture.ManifestPath
        Windows = $capture.Windows
    }
}

function Invoke-VerifyLaunch {
    $process = Invoke-Run
    Start-Sleep -Seconds 5

    $processInfo = if ($process -and $process.Id) {
        Get-Process -Id $process.Id -ErrorAction SilentlyContinue
    }
    else {
        $null
    }

    [pscustomobject]@{
        RequestedMode = $Mode
        Started = [bool]$processInfo
        ProcessId = if ($processInfo) { $processInfo.Id } else { $null }
        ProcessName = if ($processInfo) { $processInfo.ProcessName } else { $null }
        MainWindowTitle = if ($processInfo) { $processInfo.MainWindowTitle } else { $null }
        Path = if ($processInfo) { $processInfo.Path } else { $null }
        LauncherLog = $launcherLogPath
    }
}

function Invoke-Devices {
    $adbPath = Resolve-AdbPath
    if ([string]::IsNullOrWhiteSpace($adbPath)) {
        throw 'adb.exe was not found. Run tools\setup\Install-Scrcpy.ps1 or install Android platform-tools.'
    }

    $adbDevices = Invoke-NativeCapture -FilePath $adbPath -Arguments @('devices', '-l')
    $npxPath = Find-OnPath @('npx.cmd', 'npx.exe', 'npx')
    $hzdbDevices = if ($npxPath) {
        Invoke-NativeCapture -FilePath $npxPath -Arguments @('-y', '@meta-quest/hzdb', 'device', 'list', '--format', 'json')
    }
    else {
        [pscustomobject]@{
            Success = $false
            ExitCode = $null
            Output = 'npx was not found on PATH.'
        }
    }

    [pscustomobject]@{
        AdbPath = $adbPath
        AdbDevices = $adbDevices.Output
        HzdbAvailable = [bool]$npxPath
        HzdbDevices = $hzdbDevices.Output
    }
}

function Invoke-Logs {
    $latestLauncherLog = Get-LatestLogFile -Prefix 'launcher'
    $latestAppLog = Get-LatestLogFile -Prefix 'app'

    [pscustomobject]@{
        LauncherLog = $latestLauncherLog
        LauncherTail = if ($latestLauncherLog) { (Get-Content -Path $latestLauncherLog -Tail $Tail) -join [Environment]::NewLine } else { $null }
        AppLog = $latestAppLog
        AppTail = if ($latestAppLog) { (Get-Content -Path $latestAppLog -Tail $Tail) -join [Environment]::NewLine } else { $null }
    }
}

function Invoke-Doctor {
    $scrcpyPath = Resolve-ScrcpyPath
    $adbPath = Resolve-AdbPath
    $dotnetPath = Find-OnPath @('dotnet.exe', 'dotnet')
    $npxPath = Find-OnPath @('npx.cmd', 'npx.exe', 'npx')

    $dotnetVersion = if ($dotnetPath) {
        (Invoke-NativeCapture -FilePath $dotnetPath -Arguments @('--version')).Output
    }
    else {
        $null
    }

    $scrcpyVersion = if ($scrcpyPath) {
        (Invoke-NativeCapture -FilePath $scrcpyPath -Arguments @('--version')).Output
    }
    else {
        $null
    }

    $adbVersion = if ($adbPath) {
        (Invoke-NativeCapture -FilePath $adbPath -Arguments @('version')).Output
    }
    else {
        $null
    }

    $hzdbVersion = if ($npxPath) {
        (Invoke-NativeCapture -FilePath $npxPath -Arguments @('-y', '@meta-quest/hzdb', '--version')).Output
    }
    else {
        $null
    }

    $adbDevices = if ($adbPath) {
        (Invoke-NativeCapture -FilePath $adbPath -Arguments @('devices', '-l')).Output
    }
    else {
        $null
    }

    $latestLauncherLog = Get-LatestLogFile -Prefix 'launcher'
    $latestAppLog = Get-LatestLogFile -Prefix 'app'
    $runningApps = @(Get-Process -Name 'QuestMultiStream.App' -ErrorAction SilentlyContinue)
    $runningApp = $runningApps | Select-Object -First 1

    [pscustomobject]@{
        RepoRoot = $repoRoot
        DotnetPath = $dotnetPath
        DotnetVersion = $dotnetVersion
        ScrcpyPath = $scrcpyPath
        ScrcpyVersion = $scrcpyVersion
        AdbPath = $adbPath
        AdbVersion = $adbVersion
        NpxPath = $npxPath
        HzdbVersion = $hzdbVersion
        AdbDevices = $adbDevices
        LatestLauncherLog = $latestLauncherLog
        LatestAppLog = $latestAppLog
        AppInstanceCount = $runningApps.Count
        AppRunning = [bool]$runningApp
        AppProcessId = if ($runningApp) { $runningApp.Id } else { $null }
        AppWindowTitle = if ($runningApp) { $runningApp.MainWindowTitle } else { $null }
    }
}

function Show-Help {
    @'
Quest Multi Stream CLI

Commands:
  run               Launch the app. Default mode is Auto.
  build             Build the WPF app and print the built EXE path.
  publish           Publish a versioned single-file build and print the EXE path.
  verify-launch     Launch the app and report whether a process is still alive.
  doctor            Show toolchain, device, log, and running-app diagnostics.
  devices           Dump Quest device visibility through adb and hzdb.
  logs              Print the latest launcher/app logs and tail them.
  inspect-cast      Inspect the hosted cast window; pass -FixEmbeddedSize to repair a live mismatch.
  visual-test       Capture screenshot evidence for the app and visible cast windows.
  smoke-cast        Launch or reuse the app, press Start Cast, then capture screenshot evidence.
  refresh-shortcuts Recreate the desktop and Start Menu shortcuts.
  help              Show this message.

Useful options:
  -Mode Auto|Published|BuiltExe|DotnetRun
  -Refresh
  -Wait
  -BuildConfiguration Debug|Release
  -PublishConfiguration Release
  -RuntimeIdentifier win-x64
  -Tail 40
  -OutputDirectory <path>
'@
}

try {
    switch ($Command.ToLowerInvariant()) {
        'run' {
            $process = Invoke-Run
            $process | Select-Object Id, ProcessName, ExecutablePath
        }
        'build' {
            $exePath = Invoke-Build -Configuration $BuildConfiguration
            [pscustomobject]@{
                Command = 'build'
                Configuration = $BuildConfiguration
                ExecutablePath = $exePath
            }
        }
        'publish' {
            $exePath = Invoke-Publish -Configuration $PublishConfiguration -RuntimeIdentifier $RuntimeIdentifier -KeepCount $KeepPublishedBuilds
            [pscustomobject]@{
                Command = 'publish'
                Configuration = $PublishConfiguration
                RuntimeIdentifier = $RuntimeIdentifier
                ExecutablePath = $exePath
            }
        }
        'verify-launch' {
            Invoke-VerifyLaunch | Format-List *
        }
        'doctor' {
            Invoke-Doctor | Format-List *
        }
        'devices' {
            Invoke-Devices | Format-List *
        }
        'logs' {
            Invoke-Logs | Format-List *
        }
        'inspect-cast' {
            Invoke-InspectCast
        }
        'visual-test' {
            Invoke-VisualTest | Format-List *
        }
        'smoke-cast' {
            $result = Invoke-SmokeCast
            if (-not $result.Started) {
                throw "Cast smoke test did not produce a visible scrcpy window. Inspect $($result.ManifestPath)."
            }

            $result | Format-List *
        }
        'refresh-shortcuts' {
            Invoke-RefreshShortcuts
        }
        'help' {
            Show-Help
        }
        default {
            throw "Unknown command '$Command'. Run '.\tools\questms.ps1 help' for usage."
        }
    }
}
catch {
    Write-LauncherLog -Level 'ERROR' -Message $_.Exception.Message
    throw
}
