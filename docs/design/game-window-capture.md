# Game window capture: design notes

Status: paused on this branch, not merged. It goes ahead only if testers' numbers show that
full-screen Windows Graphics Capture (shipped in 0.8.4) beats Desktop Duplication on frame rate.
Otherwise the branch is dropped.

## What it does

A third capture method, "Windows Graphics Capture: game window", records the game's window
(`gfxcapture=hwnd=…`) instead of the monitor. The tray sends the foreground fullscreen window to
the worker, `GameWindowTarget.Choose` picks the window to record, and the picture is fitted into
the monitor's size (`resize_mode=scale_aspect`) so window and monitor segments join. With no game,
or while the game is minimized, it records the monitor.

## Open findings from the second review

Must fix before shipping:

- **Sticks to the first fullscreen program.** `Choose` keeps the window it chose while that window
  is alive. A browser that left F11, or Steam Big Picture, stays recorded for a whole game session.
  Follow the reported window when it changes; keep the old one only while its process is still the
  tray's current game, and tolerate a quick alt-tab.
- **Canvas size can differ from the monitor's.** The canvas comes from the window's monitor, while
  the fallback records `config.Monitor`; an exclusive-fullscreen game at a non-native mode also
  differs. Take the canvas from `config.Monitor`, or record the window's monitor in the fallback
  (and check that `monitor_idx` order matches).
- **Still screens restart recording.** Graphics Capture sends no frames for an unchanging picture,
  so an idle desktop restarts every 30 s: 1–4 s audio and video gaps, a new session part each time,
  "Session saved" balloons and a flickering status. Treat it as a hang only if `out_time` stalls
  too, or accept the duplicate-frame burst.
- **Exclusive-fullscreen alt-tab.** Each alt-tab costs two restarts, with no hysteresis. While the
  game is minimized the desktop is recorded, which goes against "records only the game".
- **Hidden or cloaked windows** loop every 30 s; a real non-fullscreen window of the same process
  is not adopted while the launcher's window is alive.

Smaller:

- `scale_aspect` anchors the picture top-left (bars only right and bottom) and may soften it.
- If `MonitorSize` fails it returns (0, 0) and the window-sized form, with mismatched segments,
  comes back.

Checked fine: DPI awareness, thread safety, no buffer deletes, the 30 s burst cap, `width`/`height`
with `scale_aspect`.

## Testing

None of this can be tested without a gaming PC. Every fix needs a run with a real game:
alt-tab (borderless and exclusive), minimize, quitting to the desktop, a launcher that opens the
game in a second window, and an idle desktop for several minutes.
