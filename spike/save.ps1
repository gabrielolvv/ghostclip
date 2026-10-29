<#
.SYNOPSIS
Phase 0: saves the last N seconds of the buffer as an MP4, without re-encoding.

.DESCRIPTION
Joins the MPEG-TS segments with FFmpeg's concat protocol (timestamps are
already continuous across segments) and remuxes with -c copy. The clip starts
at the beginning of a segment, which is always a keyframe. Runs at below
normal priority.

Output: Videos\Ghostclip\<Game>\<Game>_YYYY-MM-DD_HH-MM-SS.mp4

.EXAMPLE
.\spike\save.ps1
.\spike\save.ps1 -Game "Counter-Strike 2" -ClipSeconds 30 -Verify
#>
[CmdletBinding()]
param(
    [int]$ClipSeconds = 60,
    [int]$SegmentSeconds = 2,
    [string]$Game = 'Desktop',
    [string]$BufferDir,
    [string]$OutRoot,
    # Leaves out the segment still being written.
    [switch]$ExcludeCurrent,
    # Moves the index to the start of the file (only helps progressive
    # streaming on the web). Costs a second pass over the file: measured
    # +130 to +900 ms on an entry-level NVMe, so it is off.
    [bool]$FastStart = $false,
    # Checks the result with ffprobe (outside the timed section).
    [switch]$Verify,
    [switch]$Quiet,
    [string]$FFmpeg
)
. "$PSScriptRoot\common.ps1"

$sw = [Diagnostics.Stopwatch]::StartNew()
$now = Get-Date
$ffmpegPath = Find-Tool 'ffmpeg.exe' $FFmpeg
if (-not $BufferDir) { $BufferDir = $DefaultBufferDir }
if (-not $OutRoot) { $OutRoot = Join-Path ([Environment]::GetFolderPath('MyVideos')) 'Ghostclip' }

$files = @(Get-ChildItem -LiteralPath $BufferDir -Filter 'seg_*.ts' -File -ErrorAction SilentlyContinue)
$segments = @(Select-ClipSegments -Files $files -ClipSeconds $ClipSeconds -SegmentSeconds $SegmentSeconds -ExcludeCurrent:$ExcludeCurrent)
if ($segments.Count -eq 0) { throw "Buffer is empty in $BufferDir. Is record.ps1 running?" }

$out = Get-ClipPath -Root $OutRoot -Game $Game -Time $now
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $out) | Out-Null

$concatUrl = 'concat:' + (($segments | ForEach-Object { $_.FullName }) -join '|')
$ffArgs = @('-hide_banner', '-nostdin', '-loglevel', 'error', '-y', '-i', $concatUrl, '-map', '0', '-c', 'copy')
if ($FastStart) { $ffArgs += @('-movflags', '+faststart') }
$ffArgs += @('-f', 'mp4', $out)

$r = Invoke-Native -Path $ffmpegPath -Arguments $ffArgs -TimeoutSeconds 30 -Priority BelowNormal
$sw.Stop()
if ($r.ExitCode -ne 0) {
    Remove-Item -LiteralPath $out -ErrorAction SilentlyContinue
    throw "ffmpeg failed to save (code $($r.ExitCode)): $($r.StdErr.Trim())"
}

if (-not $Quiet) { [Media.SystemSounds]::Asterisk.Play() }

$result = [pscustomobject]@{
    Path      = $out
    ElapsedMs = [int]$sw.Elapsed.TotalMilliseconds
    Segments  = $segments.Count
    SizeMB    = [math]::Round((Get-Item -LiteralPath $out).Length / 1MB, 1)
    Duration  = $null
    FirstKey  = $null
}

if ($Verify) {
    $ffprobe = Join-Path (Split-Path -Parent $ffmpegPath) 'ffprobe.exe'
    if (Test-Path -LiteralPath $ffprobe) {
        $d = Invoke-Native -Path $ffprobe -Arguments @('-v', 'error', '-show_entries', 'format=duration',
            '-of', 'default=nw=1:nk=1', $out)
        $result.Duration = [math]::Round([double]::Parse($d.StdOut.Trim(), $Invariant), 2)
        $k = Invoke-Native -Path $ffprobe -Arguments @('-v', 'error', '-select_streams', 'v:0', '-read_intervals', '%+#1',
            '-show_entries', 'packet=flags', '-of', 'default=nw=1:nk=1', $out)
        $result.FirstKey = $k.StdOut.Trim().StartsWith('K')
    }
}

if (-not $Quiet) {
    Write-Host ("Clip saved in {0} ms: {1} ({2} segments, {3} MB)" -f $result.ElapsedMs, $out, $result.Segments, $result.SizeMB)
    if ($Verify) { Write-Host ("Duration {0} s, starts on keyframe: {1}" -f $result.Duration, $result.FirstKey) }
}
$result
