# Changelog

Each release's section here becomes its notes on GitHub Releases and in the
update prompt (`build/release.ps1` refuses to pack a version without one).

## Unreleased

### Added

- A game's profile can record its session by itself, from when the game opens until
  it closes. The session key still stops it, and then it stays stopped until the game
  next opens. A session you started by hand is never stopped for you. It also saves
  and stops if the drive runs low, so a game left open overnight can't fill it.
- Send to Discord: paste a channel's webhook address in Settings, and a button in
  the player posts the clip or picture there, after asking. A clip too big for the
  limit, with several tracks or in HEVC goes as a Discord size copy. Nothing is sent
  on its own, and the address stays out of logs and diagnostics.
- Settings > Clips > Delete clips older than: past the days you choose, clips and
  screenshots go to the Recycle Bin, when no game is running. Off by default. As with
  the space limit, favorites, clips you renamed and anything Ghostclip didn't make
  stay. On a USB stick or a network share it doesn't run, and if Windows would have
  to delete one for good, such as a clip too big for the bin, it asks first.
- In a vertical copy you choose the part of the picture: drag the 9:16 frame over
  the video, or move it with the arrows. It starts in the middle, as before.

## 0.8.13

### Added

- Volume for each track in the Discord size and vertical copies, from nothing to
  double, so the game can go quieter under your voice. Turned down to nothing, a
  track is left out.
- Save frame in the player: the frame on screen becomes a JPEG next to the clip,
  at the same quality as a screenshot.
- The player goes at 0.25x, 0.5x, 1x or 2x, from a button or the < and > keys, to
  see a play in slow motion or get through a long session. The file stays as it is.

## 0.8.12

### Added

- GIF in the player: a small looping GIF of the trim marks, 15 seconds at most,
  for chats that won't play a video. GIF has no GPU encoder, so this one export
  runs on the CPU, at low priority and only while it's being made.
- In the library, the clip under the pointer plays, muted, from its last 15
  seconds, so you can find the one you want without opening them one by one.

## 0.8.11

### Added

- Settings > Clips > Pause for these programs: while a password manager or a
  bank's app you list is the active window, recording pauses by itself and the
  replay buffer is deleted, and it goes on when another program becomes active.
  It takes up to half a second to notice, and beside another active window the
  program can still be recorded. Programs only: not a website in a browser.
- Spanish: the tray, notifications, library, settings and site. Like Portuguese,
  it follows Windows, or can be picked in Settings.

### Changed

- The replay buffer is deleted when you exit Ghostclip or Windows shuts down,
  since it holds your last moments of screen. If Windows cuts that short, it goes
  the next time Ghostclip starts. Settings > Advanced can keep it on disk until
  then, as before.
- A silent Discord size copy uses the room the sound would have taken: better
  picture, and clips too long to fit with sound can fit without it.
- In the library's selection, Delete goes to the Recycle Bin question and Ctrl+A
  picks everything on screen, so the bar can be reached from the keyboard.

### Fixed

- Saving a clip to a drive without hard links (exFAT, FAT32) no longer holds up
  the tray and its other hotkeys while it copies.
- In Settings, choosing or removing an optional hotkey keeps the keyboard's place
  instead of going back to the top of the page.

## 0.8.10

### Added

- After a new install, a notification says that Ghostclip is recording, which
  key saves a clip and where to find the library, once recording has started.
- A warning when the drive with the replay buffer or the clips is almost full,
  before recording or saving fails, once until it has room again.
- Shortcuts in the library's player, besides Space and Esc: the arrows seek 5 s,
  or 1 s with Shift, comma and period go frame by frame, and I and O mark where
  the trim starts and ends.

## 0.8.9

### Added

- Choose the sound in the copies you share: on a clip with several audio tracks,
  the buttons above its actions leave the microphone or Discord out of Discord
  size and Vertical, or make the copy silent. The clip itself keeps every track.
- Sort the library by newest, oldest, largest or longest, and show only videos
  or only screenshots, to find what to delete when the disk fills up. The sort
  is remembered.

## 0.8.8

### Added

- Join in the library's selection bar makes one video from several clips, oldest
  first, without re-encoding, saved next to the oldest. Clips recorded with
  other video settings or other audio tracks can't be joined this way, and
  Ghostclip says which.
- Settings > Video > Replay buffer folder keeps the buffer on the drive you
  choose, since it writes to its drive all the time while recording. Changing
  it restarts recording and clears the buffer, and uninstalling removes it.
- Commands for Stream Deck, macros and mouse software: besides `--save`,
  `Ghostclip.exe --save-short`, `--screenshot`, `--session`, `--mute` and
  `--pause` do what the hotkeys do in the running copy, without taking a key
  from other programs. With no copy running they do nothing, `--save` included,
  which used to start Ghostclip.

## 0.8.7

### Added

- Pause recording, for a bank's page or a password: the tray menu, or a key
  you choose in Settings > Clips, stops recording and deletes the replay
  buffer until you resume. A running session is saved first, and a tone says
  which way it went, even over a fullscreen game. Ghostclip records again the
  next time it starts.
- Vertical in the library makes a 9:16 copy of a clip for Shorts, TikTok and
  Reels: the middle of the picture at 1080x1920, encoded on the GPU, with the
  audio tracks mixed into one, saved next to the clip.
- Select several clips in the library to send them to the Recycle Bin or
  favorite them at once: the box on a clip, Ctrl+click or Space to pick,
  Shift+click for a range, Select all for what's on screen. Sending asks first,
  and says if any of them is a favorite.

## 0.8.6

### Added

- Alt+F8 mutes the microphone while you play, and pressing it again turns it
  back on. Recording goes on: the microphone track records silence meanwhile,
  so nothing restarts and the replay buffer stays. A short falling or rising
  tone says which, even over a fullscreen game, a notification too, and the
  tray menu has the same switch. The key is taken only while the microphone
  is on, can be changed in Settings > Audio, and Ghostclip always starts with
  the microphone on.
- A second clip key for when you only want the moment: Settings > Clips > Save
  a short clip saves the last 15 s, or the length you set, while Alt+F10 keeps
  saving its usual clip. It comes without a key, so updating takes nothing away
  from other programs or games; once you choose one, the tray menu has it too.
- Settings > Clips > Confirmation sounds (was "Sound when a clip is saved")
  says what it covers: clips, screenshots and sessions.

### Changed

- Settings > Video > Monitor lists your screens by name, resolution and which
  one is the main screen, instead of asking for a number. Two identical
  monitors are told apart by where they sit (on the left, on the right). A
  monitor that is unplugged stays selected and shows as not connected, so
  saving never moves recording to another screen. Monitors wired to a second
  GPU (a gaming laptop's HDMI port, often) are listed too, for Windows
  Graphics Capture.

### Fixed

- With Windows Graphics Capture and more than one monitor, Ghostclip could
  record a different screen than Desktop Duplication with the same setting:
  the two count monitors in different orders. Both now record the same one.
  If you use Windows Graphics Capture with several monitors, check Settings >
  Video > Monitor after updating.
- After the recorder restarted, for example when the screen was locked, the
  next settings you saved never reached it: recording went on with the old
  monitor, frame rate and clip length until you saved again or restarted
  Ghostclip. Every save now applies.
- Choosing a hotkey in Settings no longer runs what that key already does.
  Pressing Alt+F9 there used to start a session, and the button kept waiting;
  now the keys are recorded, and saving says if two actions share one. Ghostclip
  lets go of its hotkeys while the button waits and takes them back right after.

## 0.8.5

### Added

- Settings > Advanced > Copy diagnostics: puts the Ghostclip and Windows
  versions, each GPU with its driver and display mode, the capture and
  priority settings and the latest recorder lines from the log on the
  clipboard, ready to paste in a bug report or an FPS test.

## 0.8.4

### Added

- Windows Graphics Capture as a second way to read the screen (Settings >
  Video > Capture). Desktop Duplication stays the default; if a game loses
  FPS while recording, Windows Graphics Capture may cost it less. On
  Windows 10 it can show a yellow border around the screen while recording.
  The bundled FFmpeg now includes it (the gfxcapture filter).

## 0.8.3

### Changed

- Games first: the recorder now runs below normal priority on the CPU and on
  the GPU, so capture and encoding wait behind the game's frames instead of
  competing with them. Testers lost about 20 FPS while recording. It is on
  by default and can be turned off in Settings > Advanced if clips stutter on
  a GPU loaded to the limit; switching it applies at once, without
  restarting recording.
- Looking up favorites keeps each favorite's game folder and name in a set,
  so a library with thousands of clips and favorites lists them without
  walking every favorite for every clip.

## 0.8.2

### Fixed

- A session also splits at a recorder restart too quick for the tray to see
  it stop (under a second): each recorder run is now named, and a new one
  ends the part right before its first segment.
- Starring a clip while the space limit is making room waits for that pass,
  so the clip can't be deleted a moment after it was starred.
- Favorites and a session's bookmarks follow clips to a moved clips folder
  (another drive, or another folder in Settings): a clip is also recognized
  by its game folder and name. Before, they were lost and favorites stopped
  protecting clips from the space limit.
- A session's bookmarks no longer land minutes off when the recorder had
  just cleared an old buffer: Windows can give a new segment the creation
  time of a deleted one with the same name, so a bookmark's place is now
  counted in segments instead.
- Deleting or renaming a same-named copy of a starred clip (a backup folder
  inside the clips folder) no longer takes the original's star.
- Cleaning up cut-off saves follows the space limit's rule: only a .part file
  named after its game's folder is Ghostclip's, so a browser's paused
  download with a date in its name is left alone.

## 0.8.1

### Changed

- A downloaded update installs on its own: when you exit Ghostclip from the
  tray, or the next time it starts, including when Windows signs you in.
  "Update now" in the library and "Restart to update" in the tray still
  install it right away. Exiting with the library open leaves it for the
  next start, and opening Ghostclip while it already runs never installs.

### Fixed

- The space limit no longer counts or deletes another program's files that
  have a date in their name, such as Telegram's downloads when the clips
  folder is Downloads: only files Ghostclip named, in their game's folder.
  Clips you renamed don't count toward the limit either.
- The space limit judges age by when a file was written, so a session saved
  just now (named after its start) and a trim made today of an old clip
  (named after that clip) are no longer deleted as the oldest.
- When the favorites file can't be read, the space limit deletes nothing
  instead of treating favorites as none, and favorites can't be changed
  until it reads again, so it is never overwritten empty. A program holding
  the file for a moment is waited out instead of taken for a broken file.
- A trim of a trim, or its Discord copy, counts toward the space limit again.
- Opening the library, or running --trim, --compress or --save, while an
  update was waiting no longer installs it: the installer stopped the
  recording and only that window came back, leaving no tray until the next
  sign-in.
- A session whose bookmarks can't be written is still reported as saved.
- A session recording survives the recorder stopping mid-way (a crash, a
  locked screen): the part so far is saved, and the session goes on in a new
  file once recording is back. Before, it could join segments that don't fit
  together, collect nothing more after a long lock, or put every later
  bookmark late by the length of the pause.
- A short session no longer shows "Clip not saved" and comes back as a second
  copy at the next start: its last segment is copied instead of linked while
  the recorder still writes it, and a saved clip's leftover folder is never
  saved again.
- Saving a session checks for free space first and says how much it needs,
  instead of failing with FFmpeg's write error.
- A full disk during a session no longer stops Ghostclip from exiting (it
  could stay running with no tray icon) or loses the clip saved with a
  bookmark.
- Two screenshots taken in the same second are both kept.
- A clip hotkey chosen before screenshots and sessions existed (Alt+F9 or
  Alt+F11) no longer clashes with their defaults: the new one moves to a free
  key, so the settings page saves again and the tray stops warning at start.
- The library's favorite star goes back to how it really is when saving it
  fails, and a delete or rename that worked is no longer reported as failed
  because the favorites file was busy.
- The remaining error messages in English (deleting a clip, reading a clip's
  keyframes or sound) are translated.
- Saving settings, favorites or a session's bookmarks waits up to about a
  second for a program holding the file (antivirus, the library reading it)
  instead of half that, and favorites and bookmarks no longer fail on the
  first try when the library happens to be reading them.

## 0.8.0

### Added

- A space limit for clips (Settings > Clips, off by default). Past it, the
  oldest clips and screenshots are deleted for good after each save. Favorites,
  clips you renamed, the newest clip and anything that isn't one of
  Ghostclip's files in a game's folder are never deleted, so a clips folder
  shared with other videos is safe. Setting a limit that would delete
  something asks first and says how much; the setting shows the space in use.

### Changed

- A soft cyan light follows the pointer across the library: a halo over the
  window, and inside the card, button or sidebar item under it, whose edge
  lights up near the pointer. Cards lift and glow, the active sidebar item has
  a gradient, and the REC dot pulses. Off when Windows is set to reduce
  animations.
- The tray menu matches the library: dark, roomier, rounded corners on
  Windows 11, a cyan highlight on the item under the pointer, slightly
  see-through.

## 0.7.0

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
