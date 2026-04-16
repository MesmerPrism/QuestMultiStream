<#
.SYNOPSIS
    Publishes a versioned single-file desktop build for Quest Multi Stream.
#>
[CmdletBinding()]
param(
    [ValidateSet('Release')]
    [string]$Configuration = 'Release',
    [ValidateSet('win-x64')]
    [string]$RuntimeIdentifier = 'win-x64'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$cliPath = Join-Path $repoRoot 'tools\questms.ps1'

if (-not (Test-Path $cliPath)) {
    throw "CLI script not found at $cliPath"
}

& $cliPath publish `
    -PublishConfiguration $Configuration `
    -RuntimeIdentifier $RuntimeIdentifier
