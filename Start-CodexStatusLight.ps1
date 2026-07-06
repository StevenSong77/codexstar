$ErrorActionPreference = 'Stop'

$projectDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$publishExe = Join-Path $projectDir 'publish\CodexStatusLight.exe'
$buildExe = Join-Path $projectDir 'bin\Release\net8.0-windows\win-x64\CodexStatusLight.exe'

$exe = if (Test-Path -LiteralPath $publishExe -PathType Leaf) {
    $publishExe
} else {
    $buildExe
}

if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) {
    throw "CodexStatusLight executable not found: $exe"
}

$existing = Get-Process -Name 'CodexStatusLight' -ErrorAction SilentlyContinue
if (-not $existing) {
    Start-Process -FilePath $exe -WorkingDirectory (Split-Path -Parent $exe)
}
