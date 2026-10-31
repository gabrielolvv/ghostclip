# Changelog

Each release's section here becomes its notes on GitHub Releases and in the
update prompt (`build/release.ps1` refuses to pack a version without one).

## Unreleased

### Added

- Screenshots: Alt+F11 saves a JPEG of the recorded monitor next to the
  clips, and the library shows them with the clips.
- Session recording: Alt+F9 starts and stops it. It keeps the replay buffer
  from start to stop instead of running a second encoder, so it costs the game
  nothing extra (it does take disk: about 9 GB an hour at 20 Mbps). During a
  session the clip hotkey also adds a bookmark, shown on the timeline. A
  session cut off by a crash or an exit is finished on the next start.
- Discord on its own track (Settings > Audio): the Discord app's sound is left
  out of the main track and recorded on a third one, so a call can be muted or
  turned down later. The library names each clip's tracks.
- Favorites and renaming in the library, with a Favorites list in the sidebar.
- Hotkeys for screenshots and sessions in Settings; two actions can't share one.
- When Ghostclip can't record (no hardware encoder works, FFmpeg missing), the
  library says why at the top, instead of only "NOT RECORDING".
- When an update is downloaded, the library shows it in the sidebar with an
  "Update now" button and a link to what's new, besides the tray menu.

### Changed

- Shortcuts programs use all the time (Ctrl+C, Ctrl+V, Ctrl+Z, Ctrl+S,
  Alt+F4...) can't be a hotkey anymore: as one, Ctrl+C stopped copying in every
  program. One saved before goes back to the default (Alt+F10 for clips).
- The empty library shows the clip hotkey you set, not always Alt+F10.

## 0.6.1

### Fixed

- Clips save on AMD GPUs. AMD's encoder made no keyframes on its own (seen on
  a Radeon RX 6600), so the replay buffer never split into segments and every
  save said "Nothing recorded yet". Keyframes are now forced every 2 s, as
  they already were for NVIDIA and Intel.

## 0.6.0

### Added

- Ghostclip in Portuguese. Settings > General > Language picks English,
  Portuguese or the language Windows uses (the default); the tray menu,
  notifications, library and settings switch as soon as you save, without
  restarting recording. Logs stay in English.

### Fixed

- Ghostclip records on PCs whose GPU driver refuses the captured frames, as
  AMD's encoder did on a Radeon RX 6600 ("SubmitInput() failed with error 18"):
  the frames then go through system memory and are still encoded on the GPU.
  This is tried only when nothing on the GPU works, and can also be picked in
  Settings > Video > Encoder options > Color conversion.

## 0.5.2

### Changed

- Hotkeys read as your keyboard prints them, in the tray menu and on the
  settings page: Ctrl+ç rather than Ctrl+OemSemicolon.
- Closing the library with the window's X while a trim or compression runs
  asks first; updates and uninstalls still close it without asking.

### Fixed

- Cut-off thumbnail and cache writes no longer leave temporary files behind,
  and the cleanup's results and failures go to the log.

## 0.5.1

### Fixed

- Closing the library while a trim or compression runs stops it and leaves
  no half-written file; Ghostclip's own leftovers of work cut off earlier are
  cleaned up (other programs' .part files in the clips folder are left alone).
- Updating or uninstalling with the library open closes it properly instead
  of waiting 15 s to end it.
- The hotkey recorder records the key Windows will match on your keyboard
  layout (AZERTY, QWERTZ, ABNT2), including punctuation and the numpad's
  operators.
- Thumbnails of clips deleted long ago are removed from the cache after 90
  days, and the clip list refreshes itself if the folder watcher misses
  changes.

## 0.5.0

### Added

- Clip library: every clip by game with thumbnails, a player, and a timeline
  over the audio waveform where trimming is dragging two handles (the start
  snaps to a keyframe, so the cut is exact). Compress for Discord, copy the
  file to paste into Discord, show in folder or send to the Recycle Bin from
  the same window. Double-click the tray icon or click "Clip saved" to open
  it. It is a separate process that exists only while its window is open,
  and it reopens where you left it.
- Settings page in the library window, in the same dark design, with a hotkey
  recorder, per-game profiles and the encoders this PC supports marked.
  Changes that restart recording ask once more before saving.
- A badge in the library shows what the recorder is doing: REC · QSV · 60 FPS,
  paused while the screen is locked, or not running.

### Changed

- Settings apply as soon as they're saved, and `config.json` edited by hand
  applies too; a file caught half-edited is reported and the current settings
  stay. The property grid window is gone, and double-clicking the tray icon
  opens the library instead of the settings.
- The tray's trim dialog and its "Trim a clip" and "Compress a clip for
  Discord" items are gone; the library does both. `--trim` and `--compress`
  still work from the command line.
- Clips are written under a temporary name and renamed when complete.

### Fixed

- Ghostclip no longer closes right after starting when a request to quit was
  left over from before it started, which could happen when it restarted
  after an update.
- A hotkey another program already uses no longer leaves you without one:
  the previous hotkey stays and Ghostclip says so. A key on its own (E,
  Space) can't be the hotkey, since it would stop working everywhere else.
- Trims of 60 fps clips start exactly at the keyframe the timeline shows.
- Saving settings waits a moment when another program has `config.json`
  open, instead of failing.

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
