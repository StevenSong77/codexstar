# Changelog

## 1.1

- Added English UI toggle for task cards, menus, tooltips, and settings panels.

## 1.0.1

- Added quota page display toggle between remaining percent and refresh time.
- Reduced idle logging and startup session replay noise.
- Added bounded long-running event/audio bookkeeping to reduce gradual memory growth.
- Added completion audio player cap and timeout cleanup.
- Debounced global Codex state refresh and reduced idle fallback session scans.
- Added safer render short-circuiting while preserving visible duration updates.
- Improved runtime cleanup for timers, watchers, and audio resources.

## 1.0.0

- Initial Codexstar Windows release package.
- Floating Codex task state cards and collapsed bulb mode.
- Quota page with weekly and five-hour quota timing.
- Configurable completion sounds with preview controls.
- Tray menu, scale control, hide/show support, and watcher startup.
- Portable installer scripts with Codex notify hook backup/wrapping.
- Self-contained Windows x64 publish package.
