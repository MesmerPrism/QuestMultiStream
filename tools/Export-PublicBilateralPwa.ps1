[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$DestinationPath,
    [switch]$Check
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$sourceRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$sourceDirectory = Join-Path $sourceRoot 'src/QuestMultiStream.Browser/wwwroot'
$destination = [IO.Path]::GetFullPath($DestinationPath)
if ([IO.Path]::GetFileName($destination.TrimEnd([IO.Path]::DirectorySeparatorChar)) -cne 'bilateral-control') {
    throw 'Destination must be a dedicated bilateral-control directory.'
}
$assets = @(
    'app.mjs', 'bilateral-owner.mjs', 'capture-mode.mjs', 'decoder-policy.mjs',
    'frame-status.mjs', 'gateway.mjs', 'hub.css', 'hub.mjs', 'index.html',
    'live-owner.mjs', 'manifest.webmanifest', 'public-cache.mjs', 'public-sw.mjs',
    'pwa.mjs', 'session-view.mjs', 'style.css', 'view.mjs', 'viewer.html',
    'icons/apple-touch-icon.png', 'icons/quest-multi-stream-192.png',
    'icons/quest-multi-stream-512.png'
) | Sort-Object -CaseSensitive
$utf8 = [Text.UTF8Encoding]::new($false)
function Get-BytesHash([byte[]]$Bytes) {
    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($Bytes)).ToLowerInvariant()
}
# Fail before writing if the destination contains anything outside the finite projection.
if (Test-Path -LiteralPath $destination) {
    if ((Get-Item -LiteralPath $destination).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Destination cannot be a link.' }
    foreach ($item in Get-ChildItem -LiteralPath $destination -Recurse -Force) {
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Public export cannot contain links.' }
        if (!$item.PSIsContainer) {
            $relative = [IO.Path]::GetRelativePath($destination, $item.FullName).Replace('\', '/')
            if ($relative -cnotin $assets -and $relative -cne 'export-manifest.json') { throw 'Destination contains an unreviewed file.' }
        }
    }
}
$sourceInventory = @{}
foreach ($asset in $assets) {
    $path = Join-Path $sourceDirectory $asset
    if (!(Test-Path -LiteralPath $path -PathType Leaf) -or ((Get-Item -LiteralPath $path).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw "Missing or linked source asset: $asset" }
    $sourceInventory[$asset] = Get-BytesHash ([IO.File]::ReadAllBytes($path))
}
$exporterHash = Get-BytesHash ([IO.File]::ReadAllBytes($PSCommandPath))
# Transform changes (for example generated HTML) must also invalidate the shell.
$versionInput = $exporterHash + "`n" + (($assets | ForEach-Object { $_ + ':' + $sourceInventory[$_] }) -join "`n")
$version = Get-BytesHash ($utf8.GetBytes($versionInput))
$scope = '/bilateral-control/'
$staticPaths = @($scope) + @($assets | Where-Object { $_ -cne 'public-sw.mjs' } | ForEach-Object { $scope + $_ })
$outputs = @{}
foreach ($asset in $assets) {
    $bytes = [IO.File]::ReadAllBytes((Join-Path $sourceDirectory $asset))
    if ($asset -cin @('index.html', 'viewer.html')) {
        $html = $utf8.GetString($bytes).Replace('<html lang="en">', '<html lang="en" data-public-gateway="true">')
        if (!$html.Contains('data-public-gateway="true"')) { throw 'HTML public-mode marker was not applied.' }
        $html = $html.Replace('<head>', '<head><meta name="referrer" content="no-referrer">')
        if ($asset -ceq 'index.html') {
            $notice = '<div class="appAccess"><p>Public Bilateral Control · <a href="/projects/bilateral-streaming.html">About the project</a> · <a href="https://github.com/MesmerPrism/QuestMultiStream">Source</a>. Connect using the current operator link from your PC. A supported Chrome browser and local network permission are required; no certificate import is needed.</p>'
            $html = $html.Replace('<div class="appAccess">', $notice).Replace('Quest Multi Stream · Control hub', 'Bilateral Control · Quest Multi Stream')
        }
        $bytes = $utf8.GetBytes($html)
    } elseif ($asset -ceq 'manifest.webmanifest') {
        $manifest = $utf8.GetString($bytes) | ConvertFrom-Json -AsHashtable
        foreach ($field in @('id', 'scope', 'start_url')) { $manifest[$field] = $scope }
        $manifest['name'] = 'Bilateral Control'
        $manifest['short_name'] = 'Bilateral Control'
        $bytes = $utf8.GetBytes(($manifest | ConvertTo-Json -Depth 12) + "`n")
    } elseif ($asset -ceq 'public-sw.mjs') {
        $worker = $utf8.GetString($bytes).Replace("const CACHE_VERSION='unexported';", "const CACHE_VERSION='$version';")
        $worker = $worker.Replace('const STATIC_PATHS=[];', 'const STATIC_PATHS=' + (ConvertTo-Json -InputObject $staticPaths -Compress) + ';')
        $bytes = $utf8.GetBytes($worker)
    }
    $outputs[$asset] = $bytes
}
$revision = & git -C $sourceRoot rev-parse HEAD
if ($LASTEXITCODE -ne 0 -or $revision -cnotmatch '^[0-9a-f]{40}$') { throw 'Cannot read owner source revision.' }
$files = @($assets | ForEach-Object { [ordered]@{ path = $_; sha256 = (Get-BytesHash $outputs[$_]); sourcePath = 'src/QuestMultiStream.Browser/wwwroot/' + $_; sourceSha256 = $sourceInventory[$_] } })
$export = [ordered]@{
    schema = 'questms.public-bilateral-export.v1'
    sourceRepository = 'https://github.com/MesmerPrism/QuestMultiStream'
    sourceRevision = [string]$revision
    exporterSha256 = $exporterHash
    appScope = $scope
    cacheVersion = $version
    files = $files
}
$outputs['export-manifest.json'] = $utf8.GetBytes(($export | ConvertTo-Json -Depth 12) + "`n")
foreach ($name in @($assets) + @('export-manifest.json')) {
    $target = Join-Path $destination $name
    if ($Check) {
        if (!(Test-Path -LiteralPath $target -PathType Leaf) -or (Get-BytesHash ([IO.File]::ReadAllBytes($target))) -cne (Get-BytesHash $outputs[$name])) { throw "Public export differs: $name" }
    } else {
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target)) | Out-Null
        [IO.File]::WriteAllBytes($target, $outputs[$name])
    }
}
[ordered]@{ checked = [bool]$Check; files = $outputs.Count; cacheVersion = $version; sourceRevision = $revision } | ConvertTo-Json -Compress
