# Ghostclip specification

Open-source instant replay recorder for Windows 10/11, in the style of
Medal/ShadowPlay. Goal number one is minimal FPS and resource cost; extra
features only go in if they don't make that worse.

## Measurable targets (definition of done)

- 1080p60, 20 Mbps, 60 s buffer: average FPS drop ≤ 5% and 1% low drop ≤ 2%
  compared with the recorder turned off, measured with PresentMon.
- Capture process: CPU < 1% and RAM < 150 MB in steady state.
- Clip saved in under 2 s, without re-encoding.
- Nothing runs inside the game: no DLL injection, no render hook, no memory
  reads, no global keyboard hook (use RegisterHotKey).

## Required architecture

1. Two processes:
   - Host: tray app in .NET 10, no Electron and no WebView. Handles hotkeys,
     settings (JSON), game detection from the foreground process, the
     clip-saved notification and worker supervision. The settings window is
     only loaded when opened.
   - Capture worker: headless process, no UI and no network, owner of capture,
     encoder and audio. The host restarts the worker if it hangs, without
     losing the buffer. Communication over a named pipe.
2. Video capture:
   - Desktop Duplication (DDA) by default; Windows Graphics Capture as a
     fallback when DDA delivers no frames or the monitor is on another GPU.
   - Capture at the clip rate (e.g. 60 fps), never at the monitor rate.
   - Frames stay on the GPU end to end: a single copy/scale on the way to the
     encoder, a fixed pool of reused textures, zero allocation per frame.
3. Encoding: hardware encoder (NVENC, AMF or QuickSync), detected
   automatically. H.264 by default, HEVC optional, GOP of 1 to 2 s aligned with
   the segments. If there is no hardware encoder, tell the user; never fall
   back to a CPU encoder silently.
4. Replay buffer: ~2 s MPEG-TS segments (crash resistant) in
   `%LOCALAPPDATA%\Ghostclip\Buffer`, with circular rotation keeping only what
   is needed. Keep the interface ready for a future in-RAM buffer option.
5. Audio: WASAPI loopback of the system and an optional microphone, on separate
   tracks. Option to record only the game's audio via process loopback
   (ActivateAudioInterfaceAsync, Windows 10 2004+). Sync audio and video using
   QueryPerformanceCounter timestamps.
6. Save: on the hotkey, join the segments with stream copy (no re-encode),
   start at the nearest keyframe and remux to MP4 in
   `Videos\Ghostclip\<Game>\<Game>_YYYY-MM-DD_HH-MM-SS.mp4`. Short confirmation
   sound.
7. All work outside capture (thumbnails, export, compression) runs at below
   normal priority.

## Phases (one at a time, stopping for testing at the end of each)

- **Phase 0, feasibility spike:** worker using the FFmpeg CLI with
  `-init_hw_device d3d11va -filter_complex ddagrab=0:framerate=60` and
  h264_nvenc / h264_amf / h264_qsv (for QSV, use `hwmap=derive_device=qsv`),
  with the segment muxer (`-segment_time 2`, `-segment_wrap N`) and a save
  script using concat `-c copy`. Measure the cost in a game. The result decides
  whether the FFmpeg path is enough or we move to native capture.
- **Phase 1, MVP:** tray host, hotkey, FFmpeg worker with encoder detection,
  system audio over a named pipe, clip saving, per-game folders, JSON config.
- **Phase 2, native capture** (only if Phase 0 misses the targets): C# worker
  with DDA/WGC via Vortice.Windows or CsWinRT and a hardware encoder via Media
  Foundation.
- **Phase 3:** microphone on a separate track, game-only audio, per-game
  profiles, simple trimming by stream copy, compression to Discord's size
  limit.
- **Phase 4, distribution:** one-click per-user installer, no admin (e.g.
  Velopack), minimal LGPL FFmpeg (no libx264 or other GPL components) and
  signed updates.

## The benchmark is part of the product

- Reproducible script: run a game's built-in benchmark 3 times with the
  recorder off and 3 times with it on, collect with PresentMon and generate
  the table (average FPS, 1% low, CPU, RAM, encoder usage).
- The table goes in the README.

## Code rules

- C# with nullable enabled; minimal, justified dependencies.
- Recover without crashing from: resolution change, disconnected monitor, D3D
  device loss, driver reset, change of the default audio device.
- Rotating log files, with configurable level.
- Unit tests for segment rotation, clip assembly and file names.
- README in English and Portuguese, with a GIF.

## Way of working

- Before coding, show the folder structure and the chosen dependencies, with
  the reason for each one.
- At the end of each phase, explain how to test it and which numbers to
  measure.
- Whenever a decision affects performance, explain the trade-off.
