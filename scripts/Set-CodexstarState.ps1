param(
    [ValidateSet('idle', 'working', 'done', 'input', 'error')]
    [string] $Status = 'idle',
    [string] $Message = '',
    [int] $TtlSeconds = 0
)

$ErrorActionPreference = 'Stop'

$stateDir = if ($env:CODEX_STATUS_LIGHT_STATE_DIR) {
    $env:CODEX_STATUS_LIGHT_STATE_DIR
} else {
    Join-Path $env:LOCALAPPDATA 'Codexstar'
}
$statePath = Join-Path $stateDir 'state.json'
New-Item -ItemType Directory -Force -Path $stateDir | Out-Null

$payload = [ordered]@{
    status = $Status
    message = $Message
}

if ($TtlSeconds -gt 0) {
    $payload.expiresAtUtc = [DateTime]::UtcNow.AddSeconds($TtlSeconds).ToString('o')
}

$json = $payload | ConvertTo-Json -Compress
$utf8NoBom = [System.Text.UTF8Encoding]::new($false)
$tmpPath = Join-Path $stateDir ("state.{0}.tmp" -f ([guid]::NewGuid().ToString('N')))

try {
    [System.IO.File]::WriteAllText($tmpPath, $json, $utf8NoBom)

    $written = $false
    for ($i = 0; $i -lt 12; $i++) {
        try {
            Move-Item -LiteralPath $tmpPath -Destination $statePath -Force
            $written = $true
            break
        } catch {
            Start-Sleep -Milliseconds 80
        }
    }

    if (-not $written) {
        throw "state write failed after retries: $statePath"
    }
} finally {
    if (Test-Path -LiteralPath $tmpPath -PathType Leaf) {
        Remove-Item -LiteralPath $tmpPath -Force -ErrorAction SilentlyContinue
    }
}
