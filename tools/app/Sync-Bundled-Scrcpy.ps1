<#
.SYNOPSIS
    Copies the newest bundled scrcpy runtime into a publish/package output.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$DestinationRoot,
    [switch]$FailIfMissing
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$scrcpyRoot = Join-Path $repoRoot 'tools\scrcpy'

if (-not (Test-Path $scrcpyRoot)) {
    if ($FailIfMissing) {
        throw "Bundled scrcpy root was not found at $scrcpyRoot"
    }

    Write-Warning "Bundled scrcpy root was not found at $scrcpyRoot"
    return
}

$runtimeDirectory = Get-ChildItem -Path $scrcpyRoot -Directory |
    Where-Object { Test-Path (Join-Path $_.FullName 'scrcpy.exe') } |
    Sort-Object LastWriteTimeUtc -Descending |
    Select-Object -First 1

if ($null -eq $runtimeDirectory) {
    if ($FailIfMissing) {
        throw "No bundled scrcpy runtime with scrcpy.exe was found under $scrcpyRoot"
    }

    Write-Warning "No bundled scrcpy runtime with scrcpy.exe was found under $scrcpyRoot"
    return
}

$resolvedDestinationRoot = [System.IO.Path]::GetFullPath($DestinationRoot)
$destination = Join-Path $resolvedDestinationRoot 'scrcpy'

New-Item -ItemType Directory -Force -Path $resolvedDestinationRoot | Out-Null
if (Test-Path $destination) {
    Remove-Item -LiteralPath $destination -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $destination | Out-Null
Copy-Item -Path (Join-Path $runtimeDirectory.FullName '*') -Destination $destination -Recurse -Force

$noticePath = Join-Path $repoRoot 'THIRD_PARTY_NOTICES.md'
if (Test-Path $noticePath) {
    Copy-Item -LiteralPath $noticePath -Destination (Join-Path $resolvedDestinationRoot 'THIRD_PARTY_NOTICES.md') -Force
}

[pscustomobject]@{
    SourceRuntime = $runtimeDirectory.FullName
    DestinationRuntime = $destination
}
