[CmdletBinding()]
param([string]$Node='node')
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    & dotnet build src/QuestMultiStream.Browser --nologo
    if($LASTEXITCODE -ne 0){throw 'Browser v2 build failed'}
    & dotnet src/QuestMultiStream.Browser/bin/Debug/net10.0/QuestMultiStream.Browser.dll --owner-v2-self-test
    if($LASTEXITCODE -ne 0){throw 'Browser v2 parser/HTTP controls failed'}
    & $Node --test src/QuestMultiStream.Browser/wwwroot/bilateral-owner.test.mjs
    if($LASTEXITCODE -ne 0){throw 'Browser v2 UI controls failed'}
} finally { Pop-Location }
