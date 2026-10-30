# Changelog

Each release's section here becomes its notes on GitHub Releases and in the
update prompt (`build/release.ps1` refuses to pack a version without one).

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
