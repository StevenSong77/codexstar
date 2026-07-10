param(
    [string] $Version = '1.2',
    [string] $ProjectDir = (Split-Path -Parent $PSScriptRoot),
    [string] $OutRoot = 'E:\Tempscript\CodexstarRelease',
    [string] $DesktopDir = ([Environment]::GetFolderPath('Desktop'))
)

$ErrorActionPreference = 'Stop'

$payloadRoot = Join-Path $OutRoot "Codexstar-v$Version-win-x64"
$appOut = Join-Path $payloadRoot 'app'
$scriptsOut = Join-Path $payloadRoot 'scripts'
$sourceStage = Join-Path $OutRoot "Codexstar-v$Version-source"
$releaseZip = Join-Path $DesktopDir "Codexstar-v$Version-win-x64.zip"
$sourceZip = Join-Path $DesktopDir "Codexstar-v$Version-source.zip"

Remove-Item -LiteralPath $payloadRoot, $sourceStage -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $appOut, $scriptsOut, $sourceStage | Out-Null

dotnet publish (Join-Path $ProjectDir 'CodexStatusLight.csproj') -c Release -r win-x64 --self-contained true -o $appOut --nologo

Copy-Item -Path (Join-Path $ProjectDir 'scripts\*') -Destination $scriptsOut -Recurse -Force
foreach ($doc in @('README.md', 'CHANGELOG.md', 'CREDITS.md', 'LICENSE.md', 'LICENSE', 'LICENSE-NOTE.md')) {
    $path = Join-Path $ProjectDir $doc
    if (Test-Path -LiteralPath $path -PathType Leaf) {
        Copy-Item -LiteralPath $path -Destination (Join-Path $payloadRoot $doc) -Force
    }
}

Remove-Item -LiteralPath $releaseZip -Force -ErrorAction SilentlyContinue
Compress-Archive -Path (Join-Path $payloadRoot '*') -DestinationPath $releaseZip -Force

$excludeDirs = @('\bin\', '\obj\', '\publish\', '\.git\')
$excludeFiles = @(
    'Start-CodexStatusLightWatch.vbs',
    'external-balances.json',
    'settings.json',
    'state.json',
    'install-state.json',
    'debug.jsonl',
    'watchdog.log'
)
$sourceFiles = Get-ChildItem -LiteralPath $ProjectDir -Recurse -Force -File | Where-Object {
    $full = $_.FullName
    $fileName = $_.Name
    -not ($excludeDirs | Where-Object { $full.Contains($_) }) -and
    -not ($excludeFiles -contains $fileName) -and
    $_.Name -notmatch '\.(zip|7z|rar)$'
}

foreach ($file in $sourceFiles) {
    $relative = $file.FullName.Substring($ProjectDir.Length).TrimStart('\')
    $dest = Join-Path $sourceStage $relative
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $dest) | Out-Null
    Copy-Item -LiteralPath $file.FullName -Destination $dest -Force
}

Remove-Item -LiteralPath $sourceZip -Force -ErrorAction SilentlyContinue
Compress-Archive -Path (Join-Path $sourceStage '*') -DestinationPath $sourceZip -Force

[pscustomobject]@{
    PayloadRoot = $payloadRoot
    ReleaseZip = $releaseZip
    SourceZip = $sourceZip
}
