param(
    [string] $InstallDir = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'

$appExe = Join-Path $InstallDir 'app\CodexStatusLight.exe'
if (-not (Test-Path -LiteralPath $appExe -PathType Leaf)) {
    throw "Codexstar app not found: $appExe"
}

if (-not (Get-Process -Name 'CodexStatusLight' -ErrorAction SilentlyContinue)) {
    Start-Process -FilePath $appExe -WorkingDirectory (Split-Path -Parent $appExe)
}
