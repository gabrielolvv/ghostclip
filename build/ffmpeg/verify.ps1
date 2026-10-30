<#
.SYNOPSIS
Checks that an FFmpeg build has everything Ghostclip uses and nothing GPL.

.EXAMPLE
.\build\ffmpeg\verify.ps1 -FFmpeg .\tools\ffmpeg-min\ffmpeg.exe
#>
[CmdletBinding()]
param([Parameter(Mandatory)][string]$FFmpeg)
Set-StrictMode -Version 3.0
$ErrorActionPreference = 'Stop'

$required = [ordered]@{
    encoders = 'h264_nvenc', 'hevc_nvenc', 'h264_amf', 'hevc_amf', 'h264_qsv', 'hevc_qsv', 'aac', 'mjpeg'
    decoders = 'h264', 'hevc', 'aac', 'pcm_f32le'
    filters  = 'ddagrab', 'hwmap', 'format', 'vpp_qsv', 'scale_d3d11', 'scale', 'fps', 'amix', 'aresample', 'asetnsamples', 'astats', 'ametadata'
    muxers   = 'segment', 'mpegts', 'mp4', 'null', 'image2'
    demuxers = 'concat', 'mpegts', 'mov', 'f32le'
}
$listFlag = @{ encoders = '-encoders'; decoders = '-decoders'; filters = '-filters'; muxers = '-muxers'; demuxers = '-demuxers' }

$missing = @()
foreach ($kind in $required.Keys) {
    $list = (& $FFmpeg -hide_banner $listFlag[$kind] 2>$null) -join "`n"
    foreach ($name in $required[$kind]) {
        if ($list -notmatch "(?m)^\s*\S+\s+$([regex]::Escape($name))(\s|,|$)") { $missing += "$kind/$name" }
    }
}

$version = (& $FFmpeg -hide_banner -version 2>$null) -join "`n"
$gpl = $version -match '--enable-gpl|--enable-nonfree'

if ($missing.Count -eq 0 -and -not $gpl) {
    Write-Host "OK: every component Ghostclip uses is present; no GPL or nonfree parts."
    exit 0
}
if ($missing.Count -gt 0) { Write-Warning ("Missing: " + ($missing -join ', ')) }
if ($gpl) { Write-Warning "This build is GPL or nonfree and must not be shipped with Ghostclip." }
exit 1
