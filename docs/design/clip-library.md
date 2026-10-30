# Clip library window: design

Status: step 1 built (library window); step 2 (settings page) not started.

## Why

Today Ghostclip is a tray icon, a property grid for settings and a dialog
that takes trim points as numbers. It works, but after saving a clip there is
nowhere to watch it, pick the good part by eye or send it on. Medal and
ShadowPlay set the expectation that a recorder has a library. It is also what
a README GIF shows, and the property grid is hard on anyone who isn't a
developer.

## Goals

- A window that lists every clip by game, newest first, with thumbnails,
  length and date, and a search box.
- A clip view with a player and a timeline where trimming is dragging two
  handles, then "Save trimmed copy" (stream copy, as today).
- One-click actions on a clip: compress for Discord, copy the file to the
  clipboard (pasting into Discord uploads it), show in folder, delete to the
  Recycle Bin.
- Opened from the tray (double-click or "Open library") and by clicking the
  "Clip saved" notification, which opens that clip.
- Later, step 2: a settings page in the same window replaces the property
  grid.

## Non-goals

- Editing beyond trimming: no effects, text, music or merging clips.
- Uploading, accounts, links or anything that needs a server.
- Organising beyond folders per game (tags, favourites) in the first version.

## Anti-goals: what would make this a failure even if it works

- Any cost while a game runs and the window is closed. The library is a
  separate process that exists only while the window is open: no background
  indexing, no file watcher in the tray host, no thumbnails made at save time.
- Closing the window leaves memory behind. Closing ends the process; the tray
  host stays at its current ~60 MB.
- A trim that gives something other than what the timeline showed. Stream
  copy starts at a keyframe (every 2 s), so the start handle snaps to
  keyframes and says so.

## Visual direction: futuristic, minimalist, modern

- **Dark by default.** Near-black canvas (`#0B0D10`), one step lighter for
  cards (`#12151A`), hairline borders at 6–10% white. No gradients, no
  textures. A light theme can come later; games and capture tools live in the
  dark.
- **One accent.** Electric cyan (`#5CE1E6`) for selection, focus, the trim
  range and the primary action, and nothing else. Red (`#FF4D5E`) only for
  the recording dot and delete. The recording dot is the one element with a
  glow.
- **Content first.** Thumbnails are the largest thing on screen; chrome
  shrinks to the native title bar painted in the window's own near-black
  (so resizing, snap layouts and the caption buttons behave like any Windows
  11 window). Card actions appear on hover instead of sitting on every card.
- **Type.** Segoe UI Variable (part of Windows 11; Segoe UI on Windows 10)
  for text, and Cascadia Mono (Consolas where it is missing) for timecodes,
  sizes and the status line, which is set in spaced capitals:
  `REC · QSV · 60 FPS · 60 S`. Both ship with Windows, so nothing is bundled.
- **Timeline.** The trim range sits on the clip's audio waveform, so a shot,
  a kill or a shout is visible before you play it. FFmpeg measures 400 peak
  levels per clip (`astats`, already in the minimal build) and the page draws
  them as bars, cached with the thumbnail.
- **Motion.** 150 ms fades and slides for hover, selection and view changes;
  nothing loops or animates on its own, so an open window costs no GPU while
  idle.

## How it works

```
Ghostclip.exe (tray, unchanged cost)
    │ starts on demand
    ▼
Ghostclip.exe --library [clip]   ← separate process, ends when the window closes
    ├─ WinForms window hosting WebView2 (the Edge engine that ships with Windows)
    │     UI: plain HTML/CSS/JS from the install folder, no framework, no build step
    │     <video> plays the MP4 straight from disk, hardware-decoded
    ├─ C# bridge (WebView2 messages, JSON): list clips, trim, compress, copy, delete
    └─ ffmpeg / ffprobe (bundled): thumbnails, duration, tracks, keyframe times
```

- **Same executable, new mode.** `--library` is handled before the tray's
  single-instance check, like `--trim` today. A second launch with a clip
  path forwards it to the open library over a named pipe and exits, so the
  window comes to the front on that clip.
- **Files, not a database.** The clips folder is the library: one folder per
  game, as saving already does. The list is a scan of it when the window
  opens, plus a `FileSystemWatcher` while it stays open. Thumbnails and probe
  results are cached in `%LOCALAPPDATA%\GhostclipData\Thumbs`, keyed by path,
  size and modified time, made lazily for the cards on screen, two at a time,
  at below-normal priority.
- **Video from disk.** WebView2 maps the clips folder to a virtual host
  (`https://clips.ghostclip/...`), so `<video>` streams with range requests
  instead of loading the file.
- **Actions reuse `ClipTools`** (trim, compress) and `ClipNaming` (the
  `_trim` / `_discord` names), so the tray menu and command line keep working
  and give the same results.

## Folder structure and dependencies

```
src/Ghostclip.Core/
  ClipLibrary.cs          scan the clips folder, group by game, sort, search (unit tested)
  ThumbnailCache.cs       cache keys and invalidation (unit tested)
src/Ghostclip.Host/Library/
  LibraryProgram.cs       --library entry point, single instance, pipe for "open this clip"
  LibraryWindow.cs        Form + WebView2, window size and position remembered
  LibraryBridge.cs        JSON messages between the page and C#
  wwwroot/                index.html, app.js, app.css, icons
build/ffmpeg/configure-flags.txt   + mjpeg encoder and image2 muxer, for thumbnails
```

| Dependency | Why |
|---|---|
| `Microsoft.Web.WebView2` (NuGet, Microsoft, BSD) | A modern UI and a hardware-decoded video player without shipping a browser. The runtime is part of Windows 11 and of updated Windows 10, so the installer grows by about 1 MB. If the runtime is missing, the library says so with a download link and the tray keeps recording. |
| mjpeg + image2 in the minimal FFmpeg | One JPEG thumbnail per clip, from the FFmpeg already shipped. Adds a few hundred KB; still LGPL. |

Considered and set aside: WPF with `MediaElement` (would load WPF into the
tray process or need a second UI stack, and relies on the Windows codec
packs for HEVC), WinUI 3 (adds the Windows App SDK runtime and packaging
complexity), keeping WinForms only (no decent video player or timeline).

## Performance trade-offs

| While | Cost | Why it's acceptable |
|---|---|---|
| Window closed | none: no process | the point of the separate process |
| Window open | about 440–460 MB RAM across the library process and its 6 WebView2 processes (measured; the estimate was 150–250 MB) | only while you're looking at clips, usually not mid-game; all of it is returned on close |
| First open of many new clips | one FFmpeg frame decode per visible clip | lazy, two at a time, below-normal priority, then cached |
| Playing a clip | GPU video decode | the same engine as playing a video in Edge |

If the library is opened during a game, the recorder keeps its own budget;
the library's cost is the price of having it open, and its decode work runs
at below-normal priority.

## Steps

1. Library window: grid by game, search, thumbnails, player, timeline trim
   with keyframe snapping, compress, copy file, show in folder, delete to
   Recycle Bin. Tray double-click and "Open library" open it; the "Clip
   saved" notification opens the clip. The trim dialog goes away.
2. Settings page in the library window, replacing the property grid. The tray
   host watches `config.json` and applies changes, which also makes hand
   edits apply live. Recording status badge in the title bar.

### Step 2 in detail

- **Where.** A gear at the foot of the sidebar opens a Settings view in the
  main area; the tray's "Settings..." item opens the library straight on it
  (`--library --settings`). The property grid window is removed.
- **Layout.** One column of cards, the same tokens as the library: Clips
  (hotkey, clip length, folder, sound, Discord limit), Video (frame rate,
  bitrate, codec, encoder, output height, monitor, advanced encoder options),
  Audio (record sound, everything or game only, microphone, sync offset),
  Game profiles (one row per game, name suggested from the clip folders) and
  Advanced (start with Windows, FFmpeg path, log level). Two-value choices
  are segmented controls, on/off are switches, numbers carry their unit. The
  encoder list marks what this PC was measured to support.
- **Hotkey.** Click the field and press the combination; it is checked with
  the same parser the tray uses before it can be saved.
- **Saving.** Edits stay on the page until "Save" in a bar that appears at the
  bottom with "Discard". A change that restarts recording (video, the
  microphone switch) first turns the bar into a warning that saving clears
  the replay buffer, and needs a second click. Whether a change restarts
  recording is decided by `GhostclipConfig.SameCaptureAs`, the rule the worker
  already uses.
- **Applying.** The library writes `config.json` (atomically, as now). The
  tray watches that one file and applies whatever changed: hotkey, log level,
  autostart, and the worker's settings, which restarts recording only when
  `SameCaptureAs` says so. A hand edit of the file applies the same way. A
  changed clips folder takes effect in the open library at once.
- **Status badge.** The tray writes a small `status.json` (its process id,
  recording state, encoder, rounded frame rate, audio) only when one of those
  changes, and deletes it on exit; the library shows it in the sidebar as
  `REC · QSV · 60 FPS`, `PAUSED · SCREEN LOCKED` or `NOT RUNNING` when the file
  is missing or its process is gone.
- **Without WebView2** there is no settings window; `config.json` can still
  be edited by hand and applies live.

## How to test and what to measure

- Unit: library scan and grouping (game folders, derived `_trim` /
  `_discord` files next to their source, missing or locked files), cache
  keys, keyframe snapping.
- By hand: H.264 and HEVC playback, trim result against the timeline, copy
  and paste into Discord, delete goes to the Recycle Bin, two launches with a
  clip path.
- Numbers: time to show the grid with 100 clips (cold and cached), RAM while
  open, RAM and processes after closing (back to the tray alone), thumbnail
  time per clip.

### Measured (step 1)

i7-13620H laptop, Intel UHD, Debug build, recording 1080p60 at the same
time, 100 clips (24 s each) in 5 game folders.

| Measure | First open (no cache) | Cached |
|---|---:|---:|
| Grid with 100 clips on screen | 2.35 s | 1.01 s |
| First 8 thumbnails | 3.99 s | 1.01 s |
| RAM: library process + 6 WebView2 processes | 456 MB | 439 MB |
| After closing | process ends, WebView2 gone within 2 s | same |
| Recorder (host + worker + FFmpeg) | 255 MB before, 256–261 MB after | unchanged |

Per clip, made once and cached (24 s clip): thumbnail 1.2 s, keyframes
1.9 s, waveform peaks 0.25 s; the cache holds about 13 KB per clip. Only
clips scrolled into view are probed. A trim with the start dragged to
9.6 s snapped to the keyframe at 8.23 s and the file came out 9.80 s long
(8.23 to 18 s), matching the timeline.

## Calls made in this proposal (open to change)

- WebView2 with plain HTML/JS rather than WPF or WinUI.
- A separate process per open window rather than a window inside the tray.
- English UI, like the rest of the app.
- Dark only in the first version; cyan as the single accent; the native
  title bar painted dark rather than one drawn by the page.
- Clips are remuxed to a `.part` file and renamed when complete, so the
  library never lists a clip that is still being written.
- The player plays the game track. Chromium plays only the first audio track
  of an MP4, so the microphone is heard in the result of "Compress for
  Discord" (which mixes both) but not in the preview. A "hear the mic"
  toggle would need a temporary mixed preview; left for later.
- Thumbnails from FFmpeg in the library process rather than Windows Explorer
  thumbnails, which need the HEVC extension for HEVC clips.
