<#
.SYNOPSIS
    Downloads the latest official Windows scrcpy release into tools\scrcpy.
#>
[CmdletBinding()]
param(
    [string]$Version,
    [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$installRoot = Join-Path $repoRoot 'tools\scrcpy'
New-Item -ItemType Directory -Path $installRoot -Force | Out-Null

if ([string]::IsNullOrWhiteSpace($Version)) {
    $release = Invoke-RestMethod -Headers @{ 'User-Agent' = 'QuestMultiStreamSetup' } `
        -Uri 'https://api.github.com/repos/Genymobile/scrcpy/releases/latest'
}
else {
    $release = Invoke-RestMethod -Headers @{ 'User-Agent' = 'QuestMultiStreamSetup' } `
        -Uri "https://api.github.com/repos/Genymobile/scrcpy/releases/tags/v$Version"
}

$asset = $release.assets |
    Where-Object { $_.name -match '^scrcpy-win64-v.+\.zip$' } |
    Select-Object -First 1

if ($null -eq $asset) {
    throw 'Could not find a Windows x64 scrcpy asset in the selected release.'
}

$zipName = [System.IO.Path]::GetFileName($asset.browser_download_url)
$versionFolder = [System.IO.Path]::GetFileNameWithoutExtension($zipName)
$targetFolder = Join-Path $installRoot $versionFolder
$zipPath = Join-Path $installRoot $zipName

if ((Test-Path $targetFolder) -and -not $Force) {
    Write-Host "scrcpy already installed at $targetFolder"
    return
}

if (Test-Path $targetFolder) {
    Remove-Item -LiteralPath $targetFolder -Recurse -Force
}

Invoke-WebRequest -Headers @{ 'User-Agent' = 'QuestMultiStreamSetup' } `
    -Uri $asset.browser_download_url `
    -OutFile $zipPath

Expand-Archive -LiteralPath $zipPath -DestinationPath $installRoot -Force
Remove-Item -LiteralPath $zipPath -Force

Write-Host "Installed scrcpy to $targetFolder"
