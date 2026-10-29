<#
.SYNOPSIS
Phase 0: finds out which hardware encoders work with ddagrab on this machine.

.DESCRIPTION
For each encoder (NVENC, AMF, QuickSync) and each input variant (nv12,
direct), captures a few frames from the monitor and encodes them into the null
muxer. Nothing is written to disk. The result goes to
%LOCALAPPDATA%\Ghostclip\probe.json and is reused by record.ps1.

.EXAMPLE
.\spike\probe-encoders.ps1
.\spike\probe-encoders.ps1 -Codec hevc
#>
[CmdletBinding()]
param(
    [ValidateSet('h264', 'hevc')][string]$Codec = 'h264',
    [int]$Monitor = 0,
    [int]$Frames = 120,
    [switch]$NoLowPower,
    [string]$FFmpeg
)
. "$PSScriptRoot\common.ps1"

$ffmpegPath = Find-Tool 'ffmpeg.exe' $FFmpeg
Write-Host "FFmpeg: $ffmpegPath"
Write-Host (Get-FFmpegVersion $ffmpegPath)

$filters = (Invoke-Native -Path $ffmpegPath -Arguments @('-hide_banner', '-filters') -TimeoutSeconds 15).StdOut
foreach ($f in 'ddagrab', 'hwmap', 'scale_d3d11', 'vpp_qsv') {
    $state = if ($filters -match "\s$f\s") { 'yes' } else { 'NO' }
    Write-Host ("  filter {0,-12} {1}" -f $f, $state)
}

$results = Invoke-EncoderProbe -FFmpeg $ffmpegPath -Codec $Codec -Monitor $Monitor -Frames $Frames -NoLowPower:$NoLowPower
$results | Format-Table Encoder, Variant, Ok, Seconds, Error -AutoSize -Wrap | Out-String -Width 200 | Write-Host

$choice = Save-ProbeResult -FFmpeg $ffmpegPath -Codec $Codec -Monitor $Monitor -Results $results
if ($choice) {
    Write-Host "Automatic choice: $Codec/$($choice.Encoder), $($choice.Variant) variant"
    Write-Host "Saved to $ProbeFile"
}
else {
    Write-Warning 'No hardware encoder worked. Ghostclip does not record with a CPU encoder.'
    exit 1
}
