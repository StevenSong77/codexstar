param(
    [int]$IntervalSeconds = 5
)

$ErrorActionPreference = 'SilentlyContinue'

$mutex = [System.Threading.Mutex]::new($false, 'Local\CodexStatusLightWatch')
if (-not $mutex.WaitOne(0, $false)) {
    exit 0
}

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$startScript = Join-Path $scriptDir 'Start-CodexStatusLight.ps1'
$logDir = if ($env:CODEX_STATUS_LIGHT_STATE_DIR) {
    $env:CODEX_STATUS_LIGHT_STATE_DIR
} else {
    Join-Path $env:LOCALAPPDATA 'Codexstar'
}
$logPath = Join-Path $logDir 'watchdog.log'

function Write-WatchLog {
    param([string]$Message)
    New-Item -ItemType Directory -Force -Path $logDir | Out-Null
    Add-Content -LiteralPath $logPath -Encoding UTF8 -Value ("[{0}] {1}" -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $Message)
}

Write-WatchLog 'watcher started'

try {
    while ($true) {
        $codexRunning = @(Get-Process -Name 'Codex' -ErrorAction SilentlyContinue).Count -gt 0
        $lightRunning = @(Get-Process -Name 'CodexStatusLight' -ErrorAction SilentlyContinue).Count -gt 0

        if ($codexRunning -and -not $lightRunning -and (Test-Path -LiteralPath $startScript -PathType Leaf)) {
            Write-WatchLog 'codex detected and status light missing; starting status light'
            & $startScript
            Start-Sleep -Seconds 2
        }

        Start-Sleep -Seconds ([Math]::Max(2, $IntervalSeconds))
    }
}
finally {
    $mutex.ReleaseMutex() | Out-Null
    $mutex.Dispose()
    Write-WatchLog 'watcher stopped'
}
