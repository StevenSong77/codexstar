# Codexstar

Codexstar is a Windows floating status light for Codex. It shows running Codex tasks, task duration, completion/review state, quota timing, and configurable completion sounds.

## Requirements

- Windows 10/11 x64.
- Codex Desktop installed and used on the same Windows account.
- The release package is self-contained and does not require a separate .NET runtime.

## Install

1. Download `Codexstar-v1.0.0-win-x64.zip`.
2. Extract it.
3. Run `scripts\Install-Codexstar.cmd`.
4. The default install path is `%LOCALAPPDATA%\Programs\Codexstar`.

The installer creates a Startup folder shortcut for the watcher. The watcher starts Codexstar when Codex is running.

## Completion Hook

The installer backs up `%USERPROFILE%\.codex\config.toml` before adding the Codexstar `notify` hook.

The hook does not directly play audio. It starts or wakes Codexstar and then forwards to any previous notify command. Codexstar itself remains responsible for completion sound playback so one completed turn does not play twice.

## Uninstall

Run:

```powershell
%LOCALAPPDATA%\Programs\Codexstar\scripts\Uninstall-Codexstar.ps1 -RemoveFiles
```

The uninstaller removes the Startup shortcut and restores the previous Codex `notify` line using the install state where possible.

## Data Location

Codexstar stores its local state at:

```text
%LOCALAPPDATA%\Codexstar
```

The Codex root defaults to:

```text
%USERPROFILE%\.codex
```

Advanced users can override these with:

```text
CODEX_STATUS_LIGHT_STATE_DIR
CODEX_STATUS_LIGHT_CODEX_ROOT
```

## Known Limits

- Task and quota display depend on Codex writing compatible session `.jsonl` events.
- The first public release is a per-user portable install. It does not register in Windows "Apps & features".
- The binary remains `CodexStatusLight.exe` for compatibility with existing local scripts, while the product name is Codexstar.

## Build

```powershell
dotnet build .\CodexStatusLight.csproj -c Release
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Build-CodexstarRelease.ps1
```
