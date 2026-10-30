<#
.SYNOPSIS
Builds the minimal LGPL FFmpeg in Docker and copies ffmpeg.exe and ffprobe.exe to tools\ffmpeg-min.

.DESCRIPTION
Needs Docker Desktop with Linux containers. The first build downloads the
toolchain and sources and takes a while; later ones reuse the cache. The result
is checked by verify.ps1 against everything Ghostclip asks FFmpeg to do.

.EXAMPLE
.\build\ffmpeg\build.ps1
#>
[CmdletBinding()]
param([string]$OutDir)
Set-StrictMode -Version 3.0
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (-not $OutDir) { $OutDir = Join-Path $root 'tools\ffmpeg-min' }

docker build -t ghostclip-ffmpeg $PSScriptRoot
if ($LASTEXITCODE -ne 0) { throw "docker build failed ($LASTEXITCODE)" }

$container = docker create ghostclip-ffmpeg
try {
    if (Test-Path -LiteralPath $OutDir) { Remove-Item -LiteralPath $OutDir -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
    foreach ($file in 'bin/ffmpeg.exe', 'bin/ffprobe.exe', 'COPYING.LGPLv2.1') {
        docker cp "${container}:/out/$file" (Join-Path $OutDir (Split-Path $file -Leaf))
        if ($LASTEXITCODE -ne 0) { throw "docker cp of $file failed" }
    }
}
finally {
    docker rm $container | Out-Null
}

Get-ChildItem -LiteralPath $OutDir | ForEach-Object { '{0,-20} {1,6:0.0} MB' -f $_.Name, ($_.Length / 1MB) }
& (Join-Path $PSScriptRoot 'verify.ps1') -FFmpeg (Join-Path $OutDir 'ffmpeg.exe')
