<#
.SYNOPSIS
Draws the Ghostclip ghost and writes assets\ghostclip.ico (16 to 256 px, PNG entries).

.DESCRIPTION
Same drawing as the tray icon (src\Ghostclip.Host\TrayVisuals.cs), without the
status dot. Run it again only if the design changes; the .ico is committed.
#>
Set-StrictMode -Version 3.0
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

function New-GhostPng([int]$size) {
    $bmp = New-Object Drawing.Bitmap($size, $size)
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $s = $size / 32.0
    $g.ScaleTransform($s, $s)
    $body = New-Object Drawing.Drawing2D.GraphicsPath
    $body.AddArc(5, 2, 22, 22, 180, 180)
    $body.AddLine(27, 13, 27, 29)
    foreach ($p in @(@(27, 29, 23, 25), @(23, 25, 19, 29), @(19, 29, 16, 25), @(16, 25, 13, 29), @(13, 29, 9, 25), @(9, 25, 5, 29))) {
        $body.AddLine([single]$p[0], [single]$p[1], [single]$p[2], [single]$p[3])
    }
    $body.CloseFigure()
    $fill = New-Object Drawing.SolidBrush([Drawing.Color]::FromArgb(240, 240, 245))
    $pen = New-Object Drawing.Pen([Drawing.Color]::FromArgb(40, 40, 50), 1.6)
    $g.FillPath($fill, $body)
    $g.DrawPath($pen, $body)
    $eyes = New-Object Drawing.SolidBrush([Drawing.Color]::FromArgb(40, 40, 50))
    $g.FillEllipse($eyes, 11, 10, 4, 5.5)
    $g.FillEllipse($eyes, 18, 10, 4, 5.5)
    $g.Dispose()
    return $bmp
}

# 256 px as PNG, smaller sizes as classic DIB entries: what every icon reader understands.
function ConvertTo-IconImage([Drawing.Bitmap]$bmp) {
    $size = $bmp.Width
    $ms = New-Object IO.MemoryStream
    if ($size -ge 256) {
        $bmp.Save($ms, [Drawing.Imaging.ImageFormat]::Png)
        return , $ms.ToArray()
    }
    $w = New-Object IO.BinaryWriter($ms)
    $maskStride = [int]([math]::Ceiling($size / 32.0) * 4)
    # BITMAPINFOHEADER; the height counts the colour image and the AND mask.
    $w.Write([uint32]40); $w.Write([int32]$size); $w.Write([int32]($size * 2)); $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]0); $w.Write([uint32]($size * $size * 4 + $maskStride * $size)); $w.Write([int32]0); $w.Write([int32]0); $w.Write([uint32]0); $w.Write([uint32]0)
    for ($y = $size - 1; $y -ge 0; $y--) {
        for ($x = 0; $x -lt $size; $x++) {
            $c = $bmp.GetPixel($x, $y)
            $w.Write([byte]$c.B); $w.Write([byte]$c.G); $w.Write([byte]$c.R); $w.Write([byte]$c.A)
        }
    }
    # Alpha carries transparency; the AND mask stays all zeros.
    $w.Write((New-Object byte[] ($maskStride * $size)))
    return , $ms.ToArray()
}

$sizes = 16, 24, 32, 48, 64, 128, 256
$images = foreach ($size in $sizes) { $b = New-GhostPng $size; , (ConvertTo-IconImage $b); $b.Dispose() }
$out = New-Object IO.MemoryStream
$w = New-Object IO.BinaryWriter($out)
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$images[$i].Length); $w.Write([uint32]$offset)
    $offset += $images[$i].Length
}
foreach ($img in $images) { $w.Write($img) }
[IO.File]::WriteAllBytes((Join-Path $PSScriptRoot 'ghostclip.ico'), $out.ToArray())
"ghostclip.ico: $($out.Length) bytes, sizes $($sizes -join ', ')"
