[CmdletBinding()]
param([string]$Node = 'node', [string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if ($OutputDirectory) {
    & dotnet build (Join-Path $root 'src/QuestMultiStream.Browser') -o $OutputDirectory --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Browser host build failed' }
    & (Join-Path $OutputDirectory 'QuestMultiStream.Browser.exe') --self-test
} else { & dotnet run --project (Join-Path $root 'src/QuestMultiStream.Browser') -- --self-test }
if ($LASTEXITCODE -ne 0) { throw 'Actual browser packet/owner-profile controls failed' }
& $Node --test (Join-Path $root 'src/QuestMultiStream.Browser/wwwroot/view.test.mjs') (Join-Path $root 'src/QuestMultiStream.Browser/wwwroot/decoder-policy.test.mjs') (Join-Path $root 'src/QuestMultiStream.Browser/wwwroot/frame-status.test.mjs') (Join-Path $root 'src/QuestMultiStream.Browser/wwwroot/app-transitions.test.mjs')
if ($LASTEXITCODE -ne 0) { throw 'Actual browser codec/crop controls failed' }

$application = if ($OutputDirectory) { Join-Path $OutputDirectory 'QuestMultiStream.Browser.exe' } else { Join-Path $root 'src/QuestMultiStream.Browser/bin/Debug/net10.0/QuestMultiStream.Browser.exe' }
& (Join-Path $PSScriptRoot 'Test-BrowserMonitorLifetime.ps1') -Application $application
