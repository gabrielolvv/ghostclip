# Ghostclip

[Português](README.pt-BR.md)

Open-source instant replay recorder for Windows 10/11. It sits in the tray,
keeps the last minute of your screen, game audio and microphone, and saves a
clip when you press the hotkey. Goal number one is taking as few FPS from the
game as possible.

Nothing runs inside the game: no DLL injection, no render hooks, no memory
reads and no global keyboard hook. Capture uses Desktop Duplication, the
hotkey uses `RegisterHotKey`, game-only audio uses Windows' own process
loopback, and encoding runs on the GPU's hardware encoder (NVENC, AMF or
QuickSync). There is no CPU encoder fallback.

## Features

- **Instant replay** of the last 60 s (configurable) on Alt+F10, saved as MP4
  in a folder per game, with no re-encode.
- **Game-only audio.** Optionally records just the game: no Discord call, no
  music. It follows the game you're playing, not whatever window is in front.
- **Microphone on its own track,** so you can mute or rebalance it later.
- **Per-game profiles** for clip length, audio source and microphone mute.
- **Trim** a clip without re-encoding.
- **Compress for Discord:** re-encodes on the GPU to fit the upload limit
  (10 MB by default), mixing game and mic into one track.

## Status

**Phase 3.** Recording, saving, audio routing, profiles, trimming and
compression work. Not done yet: game benchmark numbers, installer, a GIF for
this page. The full plan is in [docs/spec.md](docs/spec.md).

## How it works

```
Ghostclip.exe (tray)              Ghostclip.Worker.exe (headless)        ffmpeg.exe
  hotkey, settings, game       ⇄    supervises the recorder,        →     ddagrab → GPU encoder
  detection, saving, trimming,      game/system audio  ── pipe →          → 2 s MPEG-TS segments
  compression                       microphone         ── pipe →          %LOCALAPPDATA%\Ghostclip\Buffer
        named pipe (JSON)           rotates the buffer
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
- **Audio.** Each track is WASAPI capture sent to FFmpeg over a named pipe,
  padded with silence against the QueryPerformanceCounter clock when nothing
  plays. Game-only audio switches the capture source (whole mix or one process
  tree) on the same pipe, so changing games never restarts recording. FFmpeg
  starts the audio clock about 150 ms before the first video frame; the worker
  measures that gap for each session and saving shifts the audio back.

## Build and run

Requires Windows 10 2004+ and the .NET 10 SDK.

```powershell
.\tools\get-tools.ps1        # LGPL FFmpeg and PresentMon into tools\
dotnet build
.\artifacts\bin\Debug\Ghostclip.exe
```

Tests: `dotnet test --project tests/Ghostclip.Core.Tests`

## Usage

- **Alt+F10** saves to `Videos\Ghostclip\<Game>\<Game>_YYYY-MM-DD_HH-MM-SS.mp4`.
- The tray menu shows what is being recorded and has trim, compress, settings
  and folders. The icon's dot is red while recording.
- Settings live in `%LOCALAPPDATA%\Ghostclip\config.json` (also editable from
  the tray) and logs in `%LOCALAPPDATA%\Ghostclip\Logs`.
- Command line, for scripts, macros or a Stream Deck:

| Command | Does |
|---|---|
| `Ghostclip.exe --save` | The running instance saves a clip |
| `Ghostclip.exe --trim <clip> <start> <end>` | Writes `<clip>_trim.mp4` by stream copy |
| `Ghostclip.exe --compress <clip>` | Writes `<clip>_discord.mp4` under the Discord limit |

## Measurements

i7-13620H laptop, Intel UHD only (QuickSync), 1080p60, H.264 20 Mbps, idle
desktop. **Not a game benchmark yet**, so there are no FPS numbers.

| Measure | Result | Target |
|---|---:|---:|
| Clip safe after the hotkey | 15–130 ms | |
| MP4 written | ~1.1–1.4 s | < 2 s |
| CPU: ffmpeg + worker (one 10 s sample) | ~1.3% | < 1% |
| RAM: ffmpeg + worker (working set) | ~146–161 MB + ~45 MB | < 150 MB |
| Audio vs. video after compensation | 53–98 ms, including the test's own playback latency | |
| Worker crash → recording again | ~1.3 s, buffer kept | |
| Game-only audio: another app's sound on the game track | 35 dB below the game | |
| Trim 10 s out of a 22 s clip | stream copy, under a second | |
| Compress 24 s for a 10 MB limit | 8.7 MB, 720p60, 7–9 s | ≤ 10 MB |

RAM is over target on this machine. On an integrated GPU, video surfaces live
in system RAM and count towards FFmpeg's memory; on a dedicated GPU they would
be in VRAM. A native capture and encode spike (DXGI + Media Foundation, in
[spike/native](spike/native/README.md)) used more memory, not less, because
both paths sit on the same Intel media driver. So Ghostclip stays on FFmpeg.

## Known limitations

- Only QuickSync has been tested. NVENC and AMF are built in but have not run
  on real hardware yet.
- Trimming by stream copy starts at a keyframe, so a cut can begin up to 2 s
  before the requested start.
- Game detection for game-only audio: a program counts as the game when it
  runs fullscreen or borderless, or has a profile. A windowed game needs a
  profile.
- If a clip crosses a worker crash, it skips the second or so the restart took.
- Changing a video setting or turning the microphone on or off restarts
  recording and clears the buffer, because segments in two formats can't be
  joined without re-encoding. The audio source and per-game settings switch
  live.
