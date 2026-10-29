<#
.SYNOPSIS
Phase 0: keeps the replay buffer with FFmpeg (ddagrab -> hardware encoder -> MPEG-TS segments).

.DESCRIPTION
Captures the monitor at the clip rate (not the monitor's), encodes on the GPU
and writes 2 s segments in circular rotation to %LOCALAPPDATA%\Ghostclip\Buffer.
Stops with q or Ctrl+C, or on its own after -DurationSeconds.

Spike: old buffer segments are deleted on start. Keeping the buffer across
worker restarts is Phase 1 work.

.EXAMPLE
.\spike\record.ps1
.\spike\record.ps1 -Encoder qsv -Variant direct -DurationSeconds 120
#>
[CmdletBinding()]
param(
    [ValidateSet('auto', 'nvenc', 'amf', 'qsv')][string]$Encoder = 'auto',
    [ValidateSet('auto', 'direct', 'nv12')][string]$Variant = 'auto',
    [ValidateSet('h264', 'hevc')][string]$Codec = 'h264',
    [int]$Monitor = 0,
    [int]$Fps = 60,
    [int]$BitrateMbps = 20,
    # 0 = native monitor resolution.
    [int]$Height = 0,
    [int]$BufferSeconds = 60,
    [int]$SegmentSeconds = 2,
    # 0 = until stopped manually.
    [int]$DurationSeconds = 0,
    # Name of a DirectShow device (e.g. "Stereo Mix (Realtek(R) Audio)").
    # Optional: real audio (WASAPI loopback) comes in Phase 1.
    [string]$AudioDevice,
    [string]$BufferDir,
    [ValidateSet('Normal', 'AboveNormal', 'BelowNormal', 'High')][string]$Priority = 'Normal',
    # QSV only. See Get-VideoArgs in common.ps1.
    [int]$AsyncDepth = 1,
    [switch]$NoLowPower,
    [string]$FFmpeg
)
. "$PSScriptRoot\common.ps1"

$ffmpegPath = Find-Tool 'ffmpeg.exe' $FFmpeg
if (-not $BufferDir) { $BufferDir = $DefaultBufferDir }

if ($Encoder -eq 'auto' -or $Variant -eq 'auto') {
    $choice = Get-EncoderChoice -FFmpeg $ffmpegPath -Encoder $Encoder -Variant $Variant -Codec $Codec -Monitor $Monitor -NoLowPower:$NoLowPower
    $Encoder = $choice.Encoder
    $Variant = $choice.Variant
}

$video = Get-VideoArgs -Encoder $Encoder -Variant $Variant -Codec $Codec -Monitor $Monitor -Fps $Fps `
    -BitrateMbps $BitrateMbps -GopSeconds $SegmentSeconds -Height $Height -AsyncDepth $AsyncDepth -NoLowPower:$NoLowPower

# One segment being written, plus slack so the save never races the recorder
# for the oldest file.
$wrap = [int][math]::Ceiling($BufferSeconds / $SegmentSeconds) + 3

New-Item -ItemType Directory -Force -Path $BufferDir | Out-Null
Get-ChildItem -LiteralPath $BufferDir -Filter 'seg_*.ts' -File | Remove-Item -Force

$ffArgs = @('-hide_banner', '-loglevel', 'warning', '-nostats',
    '-progress', $RecordProgress, '-stats_period', 2) + $video.Init
if ($AudioDevice) {
    $ffArgs += @('-f', 'dshow', '-audio_buffer_size', 50, '-i', "audio=$AudioDevice")
}
$ffArgs += @('-filter_complex', "$($video.Filter)[v]", '-map', '[v]')
if ($AudioDevice) {
    $ffArgs += @('-map', '0:a', '-c:a', 'aac', '-b:a', '160k')
}
$ffArgs += $video.Codec
if ($DurationSeconds -gt 0) { $ffArgs += @('-t', $DurationSeconds) }
# A segment_time_delta of half a frame absorbs the rounding of the forced
# keyframe's timestamp, so the cut lands exactly on it.
$delta = (1.0 / (2 * $Fps)).ToString('0.00000', $Invariant)
$ffArgs += @('-f', 'segment', '-segment_time', $SegmentSeconds, '-segment_time_delta', $delta,
    '-segment_format', 'mpegts', '-segment_wrap', $wrap, '-reset_timestamps', 0,
    (Join-Path $BufferDir 'seg_%03d.ts'))

$line = Join-NativeArgs $ffArgs
Write-Host "Encoder: $Codec/$Encoder ($Variant)  ${Fps} fps  $BitrateMbps Mbps  ${BufferSeconds}s buffer in $wrap segments"
Write-Host "Buffer:  $BufferDir"
Write-Host "Log:     $RecordLog"
Write-Verbose "ffmpeg $line"
Write-Host 'Recording. Press q in this window to stop.'

$p = Start-Process -FilePath $ffmpegPath -ArgumentList $line -NoNewWindow -PassThru -RedirectStandardError $RecordLog
try { $p.PriorityClass = $Priority } catch { Write-Warning "Could not set the priority: $_" }
Set-Content -LiteralPath $RecordPidFile -Value $p.Id
try {
    $p.WaitForExit()
}
finally {
    Remove-Item -LiteralPath $RecordPidFile -ErrorAction SilentlyContinue
}

if ($p.ExitCode -ne 0) {
    Write-Warning "ffmpeg exited with code $($p.ExitCode). Last log lines:"
    Get-Content -LiteralPath $RecordLog -Tail 15 | Write-Host
    exit $p.ExitCode
}
