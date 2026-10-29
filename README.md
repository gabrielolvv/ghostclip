# Ghostclip

[Português](README.pt-BR.md)

Open-source instant replay recorder for Windows 10/11. It sits in the tray,
keeps the last seconds of your screen and saves a clip when you press the
hotkey. Goal number one is taking as few FPS from the game as possible.

Nothing runs inside the game: no DLL injection, no render hooks, no memory
reads and no global keyboard hook.

## Status

**Phase 0, feasibility spike.** PowerShell scripts that measure whether FFmpeg
(ddagrab + hardware encoder) meets the cost targets. See
[spike/README.md](spike/README.md). The full specification is in
[docs/spec.md](docs/spec.md).

## Targets

| Measure (1080p60, 20 Mbps, 60 s buffer) | Target |
|---|---|
| Average FPS drop | ≤ 5% |
| 1% low drop | ≤ 2% |
| Capture process CPU | < 1% |
| Capture process RAM | < 150 MB |
| Time to save a clip | < 2 s, no re-encode |

The benchmark table goes here once there are measurements.
