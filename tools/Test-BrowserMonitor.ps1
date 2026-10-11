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
& $Node --test (Join-Path $root 'src/QuestMultiStream.Browser/wwwroot/view.test.mjs') (Join-Path $root 'src/QuestMultiStream.Browser/wwwroot/decoder-policy.test.mjs')
if ($LASTEXITCODE -ne 0) { throw 'Actual browser codec/crop controls failed' }
