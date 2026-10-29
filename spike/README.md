# Phase 0: feasibility spike with FFmpeg

The question this phase answers: **does the FFmpeg CLI (ddagrab + hardware
encoder + segment muxer) meet the cost targets?** If it does, the MVP uses that
path. If not, we move to native capture (Phase 2).

Everything here is throwaway: PowerShell scripts, no host, no pipe and no real
audio.

## Pieces

| Script | What it does |
|---|---|
| `tools\get-tools.ps1` | Downloads LGPL FFmpeg (BtbN) and PresentMon into `tools\`, checking SHA-256 and signature. |
| `spike\probe-encoders.ps1` | Tries each encoder (NVENC, AMF, QSV) with both input variants and saves the choice in `%LOCALAPPDATA%\Ghostclip\probe.json`. |
| `spike\record.ps1` | Keeps the buffer: ddagrab at 60 fps, hardware encoder, 2 s MPEG-TS segments in rotation. |
| `spike\save.ps1` | Joins the most recent segments with `-c copy` and writes the MP4 to `Videos\Ghostclip\<Game>\`. |
| `spike\hotkey.ps1` | Alt+F10 (via `RegisterHotKey`) runs `save.ps1` without leaving the game. |
| `bench\run-bench.ps1` | Runs with the recorder off and on, with PresentMon, plus sampling of CPU, RAM and GPU. |
| `bench\summarize.ps1` | Builds the `summary.md` table from a results folder. |

## Decisions that affect performance

- **Capture rate = clip rate.** `ddagrab framerate=60` takes at most 60 frames
  per second, even on a 144 Hz monitor. With `-fps_mode cfr`, a missing frame
  is duplicated rather than dropped, so the GOP stays aligned with the
  segments.
- **Two encoder input variants**, both staying on the GPU:
  - `nv12`: the BGRA → NV12 conversion is an explicit step, done by
    `scale_d3d11` (D3D11 VideoProcessor) for NVENC and AMF, or by `vpp_qsv`
    for QuickSync;
  - `direct`: the encoder takes BGRA and converts on its own.

  Which GPU engine does the conversion depends on the driver. On this laptop's
  Intel UHD, `vpp_qsv` runs on **3D**, the game's engine, while the `direct`
  variant's internal conversion runs on **Video Processing**, which is
  dedicated. The probe prefers `nv12`, but only the benchmark can tell which
  one costs fewer FPS. Run both.
- **QuickSync with `async_depth=1`.** An iGPU's surfaces live in system RAM and
  count towards the process's memory. With a one-frame queue, RAM drops by
  about 30 MB. The cost: the encoder waits on every frame, and with the GPU
  saturated by the game this can turn into dropped frames. If the benchmark
  shows drops, try `-AsyncDepth 4`.
- **QuickSync with `low_power=1`.** Uses the fixed-function encoding block
  (VDENC) instead of the iGPU's execution units. On an iGPU that also runs the
  game, this is the difference that matters most. Quality drops slightly at
  20 Mbps. If it fails, there is `-NoLowPower`.
- **20 Mbps CBR, fixed 2 s GOP, no B-frames.** The disk buffer is predictable
  (about 150 MB for 60 s) and every segment starts on an IDR. The cost is
  slightly lower quality than VBR with B-frames at the same bitrate.
- **NVENC `p1` + `tune ll`.** The lowest load on the NVENC block. Since the
  block is dedicated, slower presets barely take FPS from the game; NVENC
  simply could not be tested here.
- **Save includes the segment being written.** A partial MPEG-TS is readable,
  so the clip runs up to the moment of the hotkey. Without it we would lose up
  to 2 s.
- **`faststart` off on save.** It moves the index to the start of the MP4,
  which only helps progressive streaming on the web, and costs a second pass
  over the file. Here it added 130 to 900 ms and was the largest source of
  variance. Players and Discord uploads work without it. Compression for
  Discord (Phase 3) re-encodes anyway and can turn it on there.
- **Benchmark in alternating order** (off, on, off...). On a laptop, heat
  lowers the clock over the session, and grouping the "on" runs at the end
  would put all of that effect on the recorder.

## First measurements (idle desktop, no game)

i7-13620H, Intel UHD (driver 32.0.101.5972), 1080p60, QSV H.264, 20 Mbps, FFmpeg
n8.1.3 LGPL. 30 s average after a 5 s warm-up. One run each, so these are
indications, not results.

| Variant | CPU | RAM (working set) | GPU 3D | Video Processing | Video Decode (encoding) |
|---|---:|---:|---:|---:|---:|
| `nv12`, async 4 | 1.10% | 207 MB | 4.5% | 0% | 19.1% |
| `direct`, async 4 | 0.93% | 171 MB | 7.0% | 9.5% | 18.3% |
| `nv12`, async 1 | 0.64% | 171 MB | 4.9% | 0% | 19.0% |
| `direct`, async 1 | 0.98% | 144 MB | 7.9% | 9.3% | 18.6% |

Recording through `record.ps1` (`direct`, async 1): 59.9 fps, 1 dropped frame
in 85 s, 0.50% average CPU (1.22% peak) and 155 MB RAM. The buffer stayed at 33
files and 157 MB. Clips come out at 60 s (62 s with the segment being written)
and start on a keyframe.

Time to save a 60 s clip (143 MB), with 15 s between saves:

| `faststart` | Times |
|---|---|
| off (default) | 368, 371, 375 ms |
| on | 498, 725, 1267 ms |

With several saves in a row the time rises to 2–3 s, even without `faststart`.
The remux itself takes about 300 ms; the rest is the SSD (an entry-level NVMe)
absorbing several 143 MB files in sequence.

Notes:
- On Intel, QSV encoding shows up in Task Manager's **Video Decode** engine,
  not Video Encode. The benchmark adds both.
- This FFmpeg's `scale_d3d11` fails on this GPU when creating the NV12 output
  texture (`80070057`). QSV doesn't use that filter; for NVENC and AMF, if it
  fails, the probe falls back to the `direct` variant. Not yet tested on an
  NVIDIA or AMD GPU.
- CPU swings between 0.6% and 1.1%, close to the 1% limit. RAM only meets the
  target with `direct` and async 1.

## How to test on your PC

Use PowerShell in the repository folder. If the scripts are blocked:
`Set-ExecutionPolicy -Scope Process Bypass`.

**1. Tools**

```powershell
.\tools\get-tools.ps1
```

**2. Encoders.** Expected on this laptop: only `qsv` with `Ok = True`.

```powershell
.\spike\probe-encoders.ps1
```

**3. Record and save without a game** (sanity check)

```powershell
.\spike\record.ps1
# in another window, after about 70 s:
.\spike\save.ps1 -Verify
```

Check that:
- the clip opens in a player and lasts about 60 s (`Duration`);
- `starts on keyframe: True`;
- the save time is under 2000 ms;
- `%LOCALAPPDATA%\Ghostclip\Buffer` stays at about 34 files and 160 MB;
- in Task Manager, `ffmpeg.exe` stays under 1% CPU and 150 MB of memory.

**4. Hotkey inside a game**

```powershell
.\spike\record.ps1          # window 1
.\spike\hotkey.ps1 -Game "GameName"  # window 2
```

Play, press Alt+F10, listen for the sound and check the file in
`Videos\Ghostclip\GameName\`.

**5. Benchmark** (PowerShell **as administrator**, because of PresentMon)

```powershell
.\bench\run-bench.ps1 -ProcessName "Game.exe" -SaveDuringRun
.\bench\run-bench.ps1 -ProcessName "Game.exe" -RecordArgs @{ Variant = 'direct' } -SaveDuringRun
```

Each run asks you to get the game's benchmark ready, press Enter and start it
within 10 s. The result goes to `bench\results\<date>\summary.md`. For games
that accept a benchmark from the command line, use `-GameExe` and `-GameArgs`
and the script launches the game on each run.

For the measurement to count: borderless fullscreen (or "fullscreen" with
Windows fullscreen optimizations), same scene and same settings in every run,
laptop plugged in and on the same power profile, and nothing heavy open in the
background.

## Numbers to measure

The `summary.md` of each variant (`nv12` and `direct`), with:

| Number | Target |
|---|---|
| Average FPS drop | ≤ 5% |
| 1% low drop | ≤ 2% |
| `ffmpeg.exe` CPU | < 1% |
| `ffmpeg.exe` RAM | < 150 MB |
| Save time during the game | < 2 s |
| Dropped / duplicated frames | close to 0 (many duplicates mean DDA is not delivering frames) |
| Recorder's encoding and 3D usage | no target; shows where the cost is |

Keep the `meta.json` from the results folder too (CPU, GPU, driver and encoder
used), plus the name of the game and scene.

## Decision criteria

- **Meets the targets** → Phase 1 uses FFmpeg as the worker's engine.
- **Misses by a little** and the cost is in the recorder's 3D usage → try the
  other variant and `-Height 720`. If it still misses, Phase 2.
- **Misses on 1% low** during saves → the problem is the disk or the remux,
  not capture; fix the save before deciding.
- **Many duplicated frames** in the game → DDA is not delivering frames
  (exclusive fullscreen). That calls for the WGC fallback, which only exists
  in Phase 2.
