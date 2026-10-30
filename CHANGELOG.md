# Changelog

Each release's section here becomes its notes on GitHub Releases and in the
update prompt (`build/release.ps1` refuses to pack a version without one).

## Unreleased

### Added

- Clip library: every clip by game with thumbnails, a player, and a timeline
  over the audio waveform where trimming is dragging two handles (the start
  snaps to a keyframe, so the cut is exact). Compress for Discord, copy the
  file to paste into Discord, show in folder or send to the Recycle Bin from
  the same window. Double-click the tray icon or click "Clip saved" to open
  it. It is a separate process that exists only while its window is open.

### Changed

- The tray's trim dialog and its "Trim a clip" and "Compress a clip for
  Discord" items are gone; the library does both. `--trim` and `--compress`
  still work from the command line.
- Clips are written under a temporary name and renamed when complete.

### Fixed

- Ghostclip no longer closes right after starting when a request to quit was
  left over from before it started, which could happen when it restarted
  after an update.

## 0.4.1

### Fixed

- Running the installer again no longer deletes your settings, per-game
  profiles and logs. They now live in `%LOCALAPPDATA%\GhostclipData`, outside
  the install folder; this version moves them there from
  `%LOCALAPPDATA%\Ghostclip\Data`, along with any clip still waiting to be
  written, and frees the old replay buffer.

## 0.4.0

First public release.

### Recording

- Instant replay of the last 60 s (configurable, up to 10 min) on Alt+F10,
  saved as MP4 in `Videos\Ghostclip\<Game>` with no re-encode. The clip is
  safe within tens of milliseconds and the MP4 is written in under 2 s.
- Desktop Duplication capture and GPU hardware encoding only (NVENC, AMF or
  QuickSync), H.264 or HEVC. No CPU encoder fallback, nothing injected into
  the game.
- Game-only audio through Windows process loopback, following the game you
  play rather than the window in front.
- Microphone on its own track.
- Per-game profiles for clip length, audio source and microphone mute.
- Recording pauses while the screen is locked and resumes on unlock.

### Clip tools

- Trim by stream copy.
- Compress for Discord: re-encodes on the GPU to fit the upload limit, mixing
  game and microphone into one track.
- Command line: `--save`, `--trim <clip> <start> <end>`, `--compress <clip>`.

### Distribution

- Per-user installer with no admin prompt, .NET and FFmpeg included.
- Updates from GitHub Releases, applied only when you choose "Restart to
  update".
- Minimal LGPL FFmpeg build with only the components Ghostclip uses.
- Optional start with Windows.
