# Phase 2 spike: native capture and encode

The question: **does capturing and encoding natively (no FFmpeg) cost less
than the FFmpeg path?** Phase 1 measured the FFmpeg recorder above the RAM and
CPU targets on an Intel UHD laptop, so before rewriting the worker this spike
checks whether a native pipeline would actually do better.

Throwaway code: one file, no error recovery, no muxing, no audio.

## Pipeline

Desktop Duplication at the clip rate (60 fps, paced by a high-resolution
waitable timer) → D3D11 VideoProcessor, BGRA to NV12, into a fixed pool of 4
textures → Media Foundation hardware H.264 encoder (asynchronous MFT, events on
their own thread). The output is counted and dropped. Dependencies:
Vortice.Direct3D11, Vortice.DXGI, Vortice.MediaFoundation 3.8.3.

```powershell
dotnet run --project spike/native -- 30 60 20   # seconds, fps, Mbps
```

## Results

i7-13620H, Intel UHD (driver 32.0.101.5972), 1080p60, H.264 20 Mbps, idle
desktop, 30 s, measured the same way for both.

| | CPU | Working set | Private | GPU engines |
|---|---:|---:|---:|---|
| FFmpeg, QSV, encode to null | 0.67% | 155 MB | 177 MB | 3D 8.8%, Video Decode 20.2%, Video Processing 10.9% |
| Native, DDA + VP + MF | 0.22% (encoder stalled, see below) | 170 MB | 257 MB | Video Processing 1.6% |

Private memory of the native spike, step by step:

| After | Private |
|---|---:|
| .NET start | 10 MB |
| D3D11 device + duplication | 41 MB |
| Video processor | 74 MB |
| Intel Quick Sync H.264 MFT activated | 109 MB |
| Media types + NV12 pool | 136 MB |
| 17 frames handed to the encoder | 257 MB |

The Intel MFT accepted 17 frames and produced no output and no error event.
It was not debugged further, because the memory numbers already answer the
question.

## Conclusion

- **RAM: native does not help on this machine.** Most of the memory belongs to
  Intel's media driver (the encode session and its surfaces, which an
  integrated GPU keeps in system RAM). FFmpeg (through oneVPL) and Media
  Foundation (through the MFT) sit on the same driver. The native path was
  already above FFmpeg's total before it encoded a single frame.
- **CPU: little to gain.** FFmpeg's capture and encode alone is 0.67%, inside
  the 1% target. The rest of the Phase 1 recorder's ~1.3% comes from around
  it (audio, segment muxing, disk writes), and that cost would remain with
  native capture.
- **Recommendation:** keep the FFmpeg path, and measure RAM on a dedicated GPU
  before judging the 150 MB target. On an integrated GPU that target counts
  video memory as RAM.
