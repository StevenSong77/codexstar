param(
    [string] $InstallDir = (Split-Path -Parent $PSScriptRoot),
    [switch] $SkipStopExisting,
    [switch] $RemoveFiles
)

$ErrorActionPreference = 'SilentlyContinue'

function Write-Step {
    param([string] $Message)
    Write-Host "[Codexstar] $Message"
}

function Write-Utf8NoBom {
    param([string] $Path, [string] $Text)
    $utf8NoBom = [System.Text.UTF8Encoding]::new($false)
    [System.IO.File]::WriteAllText($Path, $Text, $utf8NoBom)
}

function Find-NotifyBlock {
    param([string[]] $Lines)
    for ($i = 0; $i -lt $Lines.Count; $i++) {
        if ($Lines[$i] -notmatch '^\s*notify\s*=') {
            continue
        }

        $valueLines = New-Object System.Collections.Generic.List[string]
        $end = $i
        $valueLines.Add(($Lines[$i] -replace '^\s*notify\s*=\s*', ''))
        while (($valueLines -join "`n") -notmatch '\]\s*$' -and $end + 1 -lt $Lines.Count) {
            $end++
            $valueLines.Add($Lines[$end])
        }

        return [pscustomobject]@{
            Start = $i
            End = $end
            Value = ($valueLines -join "`n").Trim()
        }
    }
    return $null
}

function Restore-CodexNotifyHook {
    param([string] $InstallStatePath)

    if (-not (Test-Path -LiteralPath $InstallStatePath -PathType Leaf)) {
        Write-Step 'No install-state.json found; notify restore skipped.'
        return
    }

    $state = Get-Content -LiteralPath $InstallStatePath -Raw -Encoding UTF8 | ConvertFrom-Json
    $configPath = [string]$state.configPath
    if (-not (Test-Path -LiteralPath $configPath -PathType Leaf)) {
        Write-Step "Codex config missing; notify restore skipped: $configPath"
        return
    }

    $lines = [System.IO.File]::ReadAllLines($configPath)
    $block = Find-NotifyBlock -Lines $lines
    if (-not $block -or $block.Value -notlike '*CodexstarNotifyHook.ps1*') {
        Write-Step 'Codex notify does not contain Codexstar; restore skipped.'
        return
    }

    $newLines = New-Object System.Collections.Generic.List[string]
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($i -eq $block.Start) {
            if ($state.hadNotify -and $state.previousNotifyToml) {
                $newLines.Add('notify = ' + [string]$state.previousNotifyToml)
            }
            $i = $block.End
            continue
        }
        $newLines.Add($lines[$i])
    }

    Write-Utf8NoBom -Path $configPath -Text (($newLines -join "`r`n") + "`r`n")
    Write-Step 'Codex notify restored.'
}

$stateDir = Join-Path $env:LOCALAPPDATA 'Codexstar'
$installStatePath = Join-Path $stateDir 'install-state.json'
$startupDir = [Environment]::GetFolderPath('Startup')
if ([string]::IsNullOrWhiteSpace($startupDir)) {
    $startupDir = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Startup'
}
$startupShortcut = Join-Path $startupDir 'Codexstar Watcher.lnk'

if (-not $SkipStopExisting) {
    Get-Process -Name 'CodexStatusLight' -ErrorAction SilentlyContinue | Stop-Process -Force
    Get-CimInstance Win32_Process |
        Where-Object { $_.Name -eq 'powershell.exe' -and $_.CommandLine -like '*Watch-Codexstar.ps1*' } |
        ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
}

if (Test-Path -LiteralPath $startupShortcut -PathType Leaf) {
    Remove-Item -LiteralPath $startupShortcut -Force
    Write-Step "Startup shortcut removed: $startupShortcut"
}

Restore-CodexNotifyHook -InstallStatePath $installStatePath

if ($RemoveFiles) {
    $cleanupScript = Join-Path $env:TEMP ('codexstar-cleanup-{0}.ps1' -f ([guid]::NewGuid().ToString('N')))
    $content = @"
Start-Sleep -Seconds 1
Remove-Item -LiteralPath '$InstallDir' -Recurse -Force -ErrorAction SilentlyContinue
"@
    Write-Utf8NoBom -Path $cleanupScript -Text $content
    Start-Process -FilePath 'powershell.exe' -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-File',$cleanupScript) -WindowStyle Hidden
    Write-Step "Install directory scheduled for removal: $InstallDir"
}

Write-Step 'Uninstall complete.'
