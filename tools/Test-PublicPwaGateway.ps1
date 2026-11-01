[CmdletBinding()]
param(
    [string]$Node='node',
    [int]$HttpPort=0,
    [int]$GatewayPort=0
)
$ErrorActionPreference='Stop'
if(-not $PSBoundParameters.ContainsKey('HttpPort') -and $env:BILATERAL_TEST_HTTP_PORT){$HttpPort=[int]$env:BILATERAL_TEST_HTTP_PORT}
if(-not $PSBoundParameters.ContainsKey('GatewayPort') -and $env:BILATERAL_TEST_GATEWAY_PORT){$GatewayPort=[int]$env:BILATERAL_TEST_GATEWAY_PORT}
if(($HttpPort -eq 0) -ne ($GatewayPort -eq 0)){throw 'Supply both isolated loopback ports or neither'}
if($HttpPort -ne 0 -and ($HttpPort -lt 1024 -or $HttpPort -gt 65535 -or $GatewayPort -lt 1024 -or $GatewayPort -gt 65535 -or $HttpPort -eq $GatewayPort)){throw 'Two distinct operator-range loopback ports required'}
# Callers own any machine-specific port reservation. Omission runs no listeners.
$root=Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    & dotnet build src/QuestMultiStream.Browser --nologo
    if($LASTEXITCODE -ne 0){throw 'Browser/Core gateway build failed'}
    $browser='src/QuestMultiStream.Browser/bin/Debug/net10.0/QuestMultiStream.Browser.dll'
    & dotnet $browser --gateway-self-test
    if($LASTEXITCODE -ne 0){throw 'Gateway selection/auth/origin/preflight unit controls failed'}
    & dotnet $browser --https-self-test
    if($LASTEXITCODE -ne 0){throw 'Existing default/HTTPS unit controls failed'}
    if($HttpPort -ne 0){
        & dotnet $browser --gateway-self-test $HttpPort $GatewayPort
        if($LASTEXITCODE -ne 0){throw 'Isolated gateway Kestrel/WebSocket conformance failed'}
        # The gateway fixture has stopped before the same exact pair is reused.
        & dotnet $browser --https-self-test $HttpPort $GatewayPort
        if($LASTEXITCODE -ne 0){throw 'Existing isolated loopback HTTPS/WSS controls failed'}
    } else { Write-Output 'Socket conformance skipped: no explicit isolated loopback ports selected' }
    $nodeTests=@(Get-ChildItem -LiteralPath 'src/QuestMultiStream.Browser/wwwroot' -Filter '*.test.mjs' -File | Sort-Object Name | ForEach-Object FullName)
    if($nodeTests.Count -eq 0){throw 'Browser Node tests missing'}
    & $Node --test @nodeTests
    if($LASTEXITCODE -ne 0){throw 'Browser Node controls failed'}
} finally { Pop-Location }
