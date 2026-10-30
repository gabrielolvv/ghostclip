<#
.SYNOPSIS
Turns a stretch of a Ghostclip clip into the README GIF (assets\demo.gif).

.DESCRIPTION
Two passes with FFmpeg: palettegen builds a 256-colour palette from the
stretch, paletteuse maps the frames to it. One palette for the whole stretch
keeps colours stable between frames, which also keeps the file small. GitHub
shows GIFs up to 10 MB in a README; aim for 5-8 MB.

Needs an FFmpeg with the gif encoder, such as the development build from
tools\get-tools.ps1 (the minimal build that ships with Ghostclip has none).

.EXAMPLE
.\assets\make-gif.ps1 -Clip "$env:USERPROFILE\Videos\Ghostclip\Game\Game_2026-10-30_21-00-00.mp4" -Start 12 -Seconds 6
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Clip,
    [double]$Start = 0,
    [double]$Seconds = 6,
    [int]$Width = 720,
    [int]$Fps = 15,
    [string]$Out = (Join-Path $PSScriptRoot 'demo.gif'),
    [string]$FFmpeg
)
$ErrorActionPreference = 'Stop'
$inv = [Globalization.CultureInfo]::InvariantCulture

if (-not $FFmpeg) {
    $FFmpeg = Get-ChildItem -Path (Join-Path $PSScriptRoot '..\tools\ffmpeg') -Filter ffmpeg.exe -Recurse -File -ErrorAction SilentlyContinue |
        Select-Object -First 1 -ExpandProperty FullName
    if (-not $FFmpeg) { throw 'FFmpeg not found. Run tools\get-tools.ps1 or pass -FFmpeg.' }
}
$Clip = (Resolve-Path -LiteralPath $Clip).Path
$palette = Join-Path ([IO.Path]::GetTempPath()) "ghostclip-palette-$PID.png"
$scale = "fps=$Fps,scale=$($Width):-2:flags=lanczos"
$range = @('-ss', $Start.ToString($inv), '-t', $Seconds.ToString($inv), '-i', $Clip)

try {
    & $FFmpeg -hide_banner -loglevel error -y @range -vf "$scale,palettegen=stats_mode=diff" $palette
    if ($LASTEXITCODE -ne 0) { throw "palettegen failed ($LASTEXITCODE)" }
    # diff_mode=rectangle only redraws the part of each frame that changed.
    & $FFmpeg -hide_banner -loglevel error -y @range -i $palette -lavfi "$scale [x]; [x][1:v] paletteuse=dither=sierra2_4a:diff_mode=rectangle" -loop 0 $Out
    if ($LASTEXITCODE -ne 0) { throw "paletteuse failed ($LASTEXITCODE)" }
}
finally {
    Remove-Item -LiteralPath $palette -ErrorAction SilentlyContinue
}

$size = (Get-Item -LiteralPath $Out).Length / 1MB
'{0}: {1:0.0} MB' -f $Out, $size
if ($size -gt 10) { Write-Warning 'Over 10 MB: GitHub will not show it. Lower -Width, -Fps or -Seconds.' }
