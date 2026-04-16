<#
.SYNOPSIS
    Launches Quest Multi Stream through the repo CLI with automatic fallback.
#>
[CmdletBinding()]
param(
    [ValidateSet('Release')]
    [string]$Configuration = 'Release',
    [ValidateSet('win-x64')]
    [string]$RuntimeIdentifier = 'win-x64',
    [switch]$Refresh,
    [switch]$Wait
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$cliPath = Join-Path $repoRoot 'tools\questms.ps1'

if (-not (Test-Path $cliPath)) {
    throw "CLI script not found at $cliPath"
}

& $cliPath run `
    -Mode Auto `
    -PublishConfiguration $Configuration `
    -BuildConfiguration Debug `
    -RuntimeIdentifier $RuntimeIdentifier `
    -Refresh:$Refresh `
    -Wait:$Wait
