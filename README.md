# Ghostclip

[Português](README.pt-BR.md)

Open-source instant replay recorder for Windows 10/11. It sits in the tray,
keeps the last minute of your screen and system audio, and saves a clip when
you press the hotkey. Goal number one is taking as few FPS from the game as
possible.

Nothing runs inside the game: no DLL injection, no render hooks, no memory
reads and no global keyboard hook. Capture uses Desktop Duplication, the
hotkey uses `RegisterHotKey`, and encoding runs on the GPU's hardware encoder
(NVENC, AMF or QuickSync). There is no CPU encoder fallback.

## Status

**Phase 1, MVP.** Tray app, hotkey, hardware encoder detection, system audio,
per-game folders and JSON settings work. Not done yet: game benchmark numbers,
installer, microphone and game-only audio. The full plan is in
[docs/spec.md](docs/spec.md); the feasibility spike that came first is in
[spike/README.md](spike/README.md).

## How it works

```
Ghostclip.exe (tray)              Ghostclip.Worker.exe (headless)        ffmpeg.exe
  hotkey, settings, game name  ⇄    supervises the recorder,        →     ddagrab → GPU encoder
  saves clips, restarts worker      WASAPI loopback audio  ── pipe →      → 2 s MPEG-TS segments
        named pipe (JSON)           rotates the buffer                    %LOCALAPPDATA%\Ghostclip\Buffer
```

- **Two processes.** The worker owns capture and audio and has no UI and no
  network. If it crashes or stops answering, the host restarts it. The buffer
  is on disk, so nothing is lost; a job object makes sure FFmpeg dies with it.
- **Frames stay on the GPU.** Captured at the clip rate (60 fps), not the
  monitor's, converted and encoded on the GPU.
- **Buffer.** 2 s segments with a keyframe at the start of each, numbered and
  rotated so only about one clip's worth stays on disk.
- **Saving.** The hotkey hard-links the segments into a pending folder (tens of
  milliseconds, whatever the disk) and the chime plays: the clip is safe from
  that moment. Then they are joined into an MP4 by stream copy, with no
  re-encode, at below-normal priority.
- **Audio sync.** FFmpeg starts the audio clock about 150 ms before the first
  video frame. The worker measures that gap on the QueryPerformanceCounter
  clock for each session, and saving shifts the audio back by it.

## Build and run

Requires Windows 10 2004+ and the .NET 10 SDK.

```powershell
.\tools\get-tools.ps1        # LGPL FFmpeg and PresentMon into tools\
dotnet build
.\artifacts\bin\Debug\Ghostclip.exe
```

Tests: `dotnet test --project tests/Ghostclip.Core.Tests`

## Usage

- **Alt+F10** saves the last 60 s to `Videos\Ghostclip\<Game>\<Game>_YYYY-MM-DD_HH-MM-SS.mp4`.
  The game name comes from the foreground program.
- `Ghostclip.exe --save` does the same from a script, a macro or a Stream Deck.
- Right-click the tray icon for the settings, the clips folder and the logs.
  The icon's dot is red while recording.
- Settings live in `%LOCALAPPDATA%\Ghostclip\config.json` and logs in
  `%LOCALAPPDATA%\Ghostclip\Logs`.

## Measurements

i7-13620H laptop, Intel UHD only (QuickSync), 1080p60, H.264 20 Mbps, system
audio on, idle desktop. **Not a game benchmark yet**, so there are no FPS
numbers.

| Measure | Result | Target |
|---|---:|---:|
| Clip safe after the hotkey | 15–40 ms | |
| MP4 written | ~1.1 s | < 2 s |
| CPU: ffmpeg + worker (one 10 s sample) | ~1.3% | < 1% |
| RAM: ffmpeg + worker (working set) | ~146–161 MB + ~45 MB | < 150 MB |
| Audio vs. video after compensation | 53–98 ms, including the test's own playback latency | |
| Worker crash → recording again | ~1.3 s, buffer kept | |

RAM is over target on this machine. On an integrated GPU, video surfaces live
in system RAM and count towards FFmpeg's memory; on a dedicated GPU they would
be in VRAM. That, plus the FPS cost in a real game, is what decides whether
FFmpeg stays or capture goes native (Phase 2).

## Known limitations

- Only QuickSync has been tested. NVENC and AMF are built in but have not run
  on real hardware yet.
- If the clip crosses a worker crash, it skips the second or so the restart
  took.
- Changing a video or audio setting restarts recording and clears the buffer,
  because segments in two formats can't be joined without re-encoding.
