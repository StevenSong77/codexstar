param(
    [string] $InstallDir = (Join-Path $env:LOCALAPPDATA 'Programs\Codexstar'),
    [switch] $SkipNotify,
    [switch] $SkipStartup,
    [switch] $SkipStopExisting,
    [switch] $NoStart
)

$ErrorActionPreference = 'Stop'

function Write-Step {
    param([string] $Message)
    Write-Host "[Codexstar] $Message"
}

function Write-Utf8NoBom {
    param(
        [string] $Path,
        [string] $Text
    )
    $utf8NoBom = [System.Text.UTF8Encoding]::new($false)
    [System.IO.File]::WriteAllText($Path, $Text, $utf8NoBom)
}

function ConvertTo-TomlString {
    param([string] $Value)
    return '"' + $Value.Replace('\', '\\').Replace('"', '\"') + '"'
}

function ConvertTo-TomlArray {
    param([string[]] $Values)
    $items = @($Values | ForEach-Object { ConvertTo-TomlString $_ })
    return '[ ' + ($items -join ', ') + ' ]'
}

function ConvertFrom-TomlStringArray {
    param([string] $Value)

    $items = New-Object System.Collections.Generic.List[string]
    $buffer = New-Object System.Text.StringBuilder
    $inString = $false
    $escape = $false

    for ($i = 0; $i -lt $Value.Length; $i++) {
        $ch = $Value[$i]
        if (-not $inString) {
            if ($ch -eq '"') {
                $inString = $true
                [void]$buffer.Clear()
            }
            continue
        }

        if ($escape) {
            switch ($ch) {
                'n' { [void]$buffer.Append("`n") }
                'r' { [void]$buffer.Append("`r") }
                't' { [void]$buffer.Append("`t") }
                default { [void]$buffer.Append($ch) }
            }
            $escape = $false
            continue
        }

        if ($ch -eq '\') {
            $escape = $true
            continue
        }

        if ($ch -eq '"') {
            $items.Add($buffer.ToString())
            $inString = $false
            continue
        }

        [void]$buffer.Append($ch)
    }

    return [string[]]$items.ToArray()
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

function Set-CodexNotifyHook {
    param(
        [string] $ConfigPath,
        [string] $HookPath,
        [string] $InstallStatePath
    )

    if (-not (Test-Path -LiteralPath $ConfigPath -PathType Leaf)) {
        Write-Step "Codex config not found; notify hook skipped: $ConfigPath"
        return
    }

    $lines = [System.IO.File]::ReadAllLines($ConfigPath)
    $block = Find-NotifyBlock -Lines $lines
    if ($block -and $block.Value -like '*CodexstarNotifyHook.ps1*') {
        Write-Step 'Codex notify hook already contains Codexstar; skipped.'
        return
    }

    $backupPath = "$ConfigPath.codexstar-backup-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
    Copy-Item -LiteralPath $ConfigPath -Destination $backupPath -Force

    $hookCommand = @(
        'powershell.exe',
        '-NoProfile',
        '-ExecutionPolicy',
        'Bypass',
        '-File',
        $HookPath,
        'turn-ended'
    )

    $hadNotify = $false
    $previousNotifyToml = $null
    if ($block) {
        $hadNotify = $true
        $previousNotifyToml = $block.Value
        $previousArray = ConvertFrom-TomlStringArray -Value $block.Value
        if ($previousArray.Count -gt 0) {
            $previousJson = ConvertTo-Json -InputObject ([string[]]$previousArray) -Compress
            $hookCommand += @('--previous-notify', $previousJson)
        }
    }

    $newNotifyLine = 'notify = ' + (ConvertTo-TomlArray -Values $hookCommand)
    $newLines = New-Object System.Collections.Generic.List[string]
    if ($block) {
        for ($i = 0; $i -lt $lines.Count; $i++) {
            if ($i -eq $block.Start) {
                $newLines.Add($newNotifyLine)
                $i = $block.End
                continue
            }
            $newLines.Add($lines[$i])
        }
    } else {
        $newLines.AddRange([string[]]$lines)
        if ($newLines.Count -gt 0 -and -not [string]::IsNullOrWhiteSpace($newLines[$newLines.Count - 1])) {
            $newLines.Add('')
        }
        $newLines.Add($newNotifyLine)
    }

    Write-Utf8NoBom -Path $ConfigPath -Text (($newLines -join "`r`n") + "`r`n")
    Write-Step "Codex notify hook installed. Backup: $backupPath"

    $state = [ordered]@{
        installedAtUtc = [DateTime]::UtcNow.ToString('o')
        installDir = $InstallDir
        configPath = $ConfigPath
        backupPath = $backupPath
        hadNotify = $hadNotify
        previousNotifyToml = $previousNotifyToml
        installedNotifyToml = ($newNotifyLine -replace '^\s*notify\s*=\s*', '')
    }
    Write-Utf8NoBom -Path $InstallStatePath -Text ($state | ConvertTo-Json -Depth 6)
}

$packageRoot = Split-Path -Parent $PSScriptRoot
$sourceAppDir = Join-Path $packageRoot 'app'
$sourceScriptsDir = Join-Path $packageRoot 'scripts'
if (-not (Test-Path -LiteralPath (Join-Path $sourceAppDir 'CodexStatusLight.exe') -PathType Leaf)) {
    throw "Package app payload missing: $sourceAppDir"
}

$installAppDir = Join-Path $InstallDir 'app'
$installScriptsDir = Join-Path $InstallDir 'scripts'
$stateDir = Join-Path $env:LOCALAPPDATA 'Codexstar'
$installStatePath = Join-Path $stateDir 'install-state.json'

Write-Step "Installing to $InstallDir"
New-Item -ItemType Directory -Force -Path $installAppDir, $installScriptsDir, $stateDir | Out-Null

if (-not $SkipStopExisting) {
    Get-Process -Name 'CodexStatusLight' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
}

Remove-Item -LiteralPath $installAppDir -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $installScriptsDir -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $installAppDir, $installScriptsDir | Out-Null
Copy-Item -Path (Join-Path $sourceAppDir '*') -Destination $installAppDir -Recurse -Force
Copy-Item -Path (Join-Path $sourceScriptsDir '*') -Destination $installScriptsDir -Recurse -Force

foreach ($doc in @('README.md', 'CHANGELOG.md', 'CREDITS.md', 'LICENSE-NOTE.md')) {
    $sourceDoc = Join-Path $packageRoot $doc
    if (Test-Path -LiteralPath $sourceDoc -PathType Leaf) {
        Copy-Item -LiteralPath $sourceDoc -Destination (Join-Path $InstallDir $doc) -Force
    }
}

$watchCmd = Join-Path $installScriptsDir 'Watch-Codexstar.cmd'
$watchHost = Join-Path $env:WINDIR 'System32\wscript.exe'
$watchVbs = Join-Path $installScriptsDir 'Start-CodexstarWatch.vbs'
if (-not $SkipStartup) {
    $shell = New-Object -ComObject WScript.Shell
    $startupDir = [Environment]::GetFolderPath('Startup')
    if ([string]::IsNullOrWhiteSpace($startupDir)) {
        $startupDir = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Startup'
    }
    New-Item -ItemType Directory -Force -Path $startupDir | Out-Null
    $startupShortcut = Join-Path $startupDir 'Codexstar Watcher.lnk'
    $shortcut = $shell.CreateShortcut($startupShortcut)
    $shortcut.TargetPath = $watchHost
    $shortcut.Arguments = '"{0}"' -f $watchVbs
    $shortcut.WorkingDirectory = $installScriptsDir
    $shortcut.WindowStyle = 7
    $shortcut.IconLocation = Join-Path $installAppDir 'CodexStatusLight.exe'
    $shortcut.Save()
    Write-Step "Startup shortcut installed: $startupShortcut"
}

if (-not $SkipNotify) {
    $codexConfig = Join-Path (Join-Path $env:USERPROFILE '.codex') 'config.toml'
    $hookPath = Join-Path $installScriptsDir 'CodexstarNotifyHook.ps1'
    Set-CodexNotifyHook -ConfigPath $codexConfig -HookPath $hookPath -InstallStatePath $installStatePath
}

if (-not $NoStart) {
    Start-Process -FilePath $watchHost -ArgumentList @($watchVbs) -WindowStyle Hidden
    Write-Step 'Watcher started.'
}

Write-Step 'Install complete.'
