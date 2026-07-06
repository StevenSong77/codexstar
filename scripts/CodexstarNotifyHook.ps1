param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]] $NotifyArgs
)

$ErrorActionPreference = 'SilentlyContinue'

$installDir = Split-Path -Parent $PSScriptRoot
$appExe = Join-Path $installDir 'app\CodexStatusLight.exe'
$stateDir = if ($env:CODEX_STATUS_LIGHT_STATE_DIR) {
    $env:CODEX_STATUS_LIGHT_STATE_DIR
} else {
    Join-Path $env:LOCALAPPDATA 'Codexstar'
}
$eventLog = Join-Path $stateDir 'notify-events.jsonl'

function Write-NotifyEvent {
    param([string] $Message)
    try {
        New-Item -ItemType Directory -Force -Path $stateDir | Out-Null
        $payload = [ordered]@{
            timestampUtc = [DateTime]::UtcNow.ToString('o')
            message = $Message
            args = @($NotifyArgs)
        }
        Add-Content -LiteralPath $eventLog -Encoding UTF8 -Value ($payload | ConvertTo-Json -Compress -Depth 5)
    } catch {
    }
}

function Remove-CodexstarPreviousNotifyArg {
    param([string[]] $Arguments)
    $result = New-Object System.Collections.Generic.List[string]
    for ($i = 0; $i -lt $Arguments.Count; $i++) {
        if ($Arguments[$i] -eq '--previous-notify') {
            $i++
            continue
        }

        $result.Add($Arguments[$i])
    }

    return [string[]]$result.ToArray()
}

function Invoke-PreviousNotify {
    param([string[]] $Arguments)

    for ($i = 0; $i -lt $Arguments.Count; $i++) {
        if ($Arguments[$i] -ne '--previous-notify' -or $i + 1 -ge $Arguments.Count) {
            continue
        }

        try {
            $previous = @($Arguments[$i + 1] | ConvertFrom-Json -ErrorAction Stop)
            if ($previous.Count -eq 0) {
                return
            }

            $command = [string]$previous[0]
            $commandArgs = @()
            if ($previous.Count -gt 1) {
                $commandArgs += @($previous[1..($previous.Count - 1)])
            }

            $forwardArgs = @(Remove-CodexstarPreviousNotifyArg -Arguments $Arguments)
            if ($commandArgs -contains 'turn-ended' -and $forwardArgs.Count -gt 0 -and $forwardArgs[0] -eq 'turn-ended') {
                $forwardArgs = @($forwardArgs | Select-Object -Skip 1)
            }

            & $command @commandArgs @forwardArgs
            return
        } catch {
            Write-NotifyEvent ("previous notify failed: {0}" -f $_.Exception.Message)
            return
        }
    }
}

Write-NotifyEvent 'hook invoked'

try {
    if (-not (Get-Process -Name 'CodexStatusLight' -ErrorAction SilentlyContinue) -and
        (Test-Path -LiteralPath $appExe -PathType Leaf)) {
        Start-Process -FilePath $appExe -WorkingDirectory (Split-Path -Parent $appExe)
    }
} catch {
    Write-NotifyEvent ("app start failed: {0}" -f $_.Exception.Message)
}

Invoke-PreviousNotify -Arguments $NotifyArgs
exit 0
