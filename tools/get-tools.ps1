<#
.SYNOPSIS
Downloads FFmpeg (BtbN's LGPL build) and PresentMon (Intel) into tools\.

.DESCRIPTION
FFmpeg: shared LGPL variant, without libx264 or any other GPL component, with
ddagrab, scale_d3d11, NVENC, AMF and QuickSync (libvpl). The archive is checked
against the checksums.sha256 published with the release.
PresentMon: command-line executable signed by Intel; the Authenticode
signature is checked.

.EXAMPLE
.\tools\get-tools.ps1
#>
[CmdletBinding()]
param(
    [string]$FFmpegVersion = '8.1',
    [string]$PresentMonVersion = '2.6.0',
    [switch]$Force
)
Set-StrictMode -Version 3.0
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

$toolsDir = $PSScriptRoot

# --- FFmpeg ---
$ffDir = Join-Path $toolsDir 'ffmpeg'
if ($Force -or -not (Get-ChildItem -Path $ffDir -Filter ffmpeg.exe -Recurse -ErrorAction SilentlyContinue)) {
    $asset = "ffmpeg-n$FFmpegVersion-latest-win64-lgpl-shared-$FFmpegVersion.zip"
    $base = 'https://github.com/BtbN/FFmpeg-Builds/releases/download/latest'
    $tmp = Join-Path ([IO.Path]::GetTempPath()) ("ghostclip-" + [guid]::NewGuid())
    New-Item -ItemType Directory -Path $tmp | Out-Null
    try {
        $zip = Join-Path $tmp $asset
        Write-Host "Downloading $asset..."
        Invoke-WebRequest -Uri "$base/$asset" -OutFile $zip -UseBasicParsing
        $sums = (Invoke-WebRequest -Uri "$base/checksums.sha256" -UseBasicParsing).Content
        if ($sums -is [byte[]]) { $sums = [Text.Encoding]::ASCII.GetString($sums) }
        $line = ($sums -split "`n") | Where-Object { $_ -match [regex]::Escape($asset) } | Select-Object -First 1
        if (-not $line) { throw "checksums.sha256 does not list $asset" }
        $expected = ($line.Trim() -split '\s+')[0].ToLowerInvariant()
        $actual = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($expected -ne $actual) { throw "SHA-256 mismatch for $asset (expected $expected, got $actual)" }
        Write-Host 'SHA-256 matches.'
        if (Test-Path -LiteralPath $ffDir) { Remove-Item -LiteralPath $ffDir -Recurse -Force }
        Expand-Archive -LiteralPath $zip -DestinationPath $ffDir
    }
    finally {
        Remove-Item -LiteralPath $tmp -Recurse -Force -ErrorAction SilentlyContinue
    }
}
$ffmpeg = Get-ChildItem -Path $ffDir -Filter ffmpeg.exe -Recurse | Select-Object -First 1
Write-Host "FFmpeg: $($ffmpeg.FullName)"

# --- PresentMon ---
$pmDir = Join-Path $toolsDir 'presentmon'
$pmExe = Join-Path $pmDir "PresentMon-$PresentMonVersion-x64.exe"
if ($Force -or -not (Test-Path -LiteralPath $pmExe)) {
    New-Item -ItemType Directory -Force -Path $pmDir | Out-Null
    $url = "https://github.com/GameTechDev/PresentMon/releases/download/v$PresentMonVersion/PresentMon-$PresentMonVersion-x64.exe"
    Write-Host "Downloading PresentMon $PresentMonVersion..."
    Invoke-WebRequest -Uri $url -OutFile $pmExe -UseBasicParsing
    $sig = Get-AuthenticodeSignature -LiteralPath $pmExe
    if ($sig.Status -ne 'Valid') {
        Remove-Item -LiteralPath $pmExe -Force
        throw "Invalid PresentMon signature: $($sig.Status)"
    }
    Write-Host "Valid signature: $($sig.SignerCertificate.Subject)"
}
Write-Host "PresentMon: $pmExe"
