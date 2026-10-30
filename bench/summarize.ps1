<#
.SYNOPSIS
Builds the results table (summary.md) from a run-bench.ps1 folder.

.DESCRIPTION
Average FPS = frames / total time (1000 / mean frame time).
1% low      = 1000 / mean of the slowest 1% of frames. Stricter than the 99th
              percentile, which leaves out exactly that 1% and hides rare
              stutters.
Each metric is computed per round; the table shows the mean across rounds and
the standard deviation between them.

.EXAMPLE
.\bench\summarize.ps1 -Dir .\bench\results\2026-10-29_21-00-00
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Dir,
    [double]$MaxAvgDropPct = 5,
    [double]$MaxLowDropPct = 2,
    [double]$MaxCpuPct = 1,
    [double]$MaxRamMb = 150,
    [double]$MaxSaveMs = 2000
)
Set-StrictMode -Version 3.0
$ErrorActionPreference = 'Stop'
$inv = [Globalization.CultureInfo]::InvariantCulture

function ConvertTo-Double($Text) {
    $v = 0.0
    if ($null -ne $Text -and [double]::TryParse([string]$Text, [Globalization.NumberStyles]::Float, $inv, [ref]$v)) { return $v }
    return [double]::NaN
}

function Get-Mean([double[]]$Values) {
    $v = @($Values | Where-Object { -not [double]::IsNaN($_) })
    if ($v.Count -eq 0) { return [double]::NaN }
    return ($v | Measure-Object -Average).Average
}

function Get-StdDev([double[]]$Values) {
    $v = @($Values | Where-Object { -not [double]::IsNaN($_) })
    if ($v.Count -lt 2) { return 0.0 }
    $m = Get-Mean $v
    $sum = 0.0
    foreach ($x in $v) { $sum += ($x - $m) * ($x - $m) }
    return [math]::Sqrt($sum / ($v.Count - 1))
}

function Get-Max([double[]]$Values) {
    $v = @($Values | Where-Object { -not [double]::IsNaN($_) })
    if ($v.Count -eq 0) { return [double]::NaN }
    return ($v | Measure-Object -Maximum).Maximum
}

function Get-FrameStats([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return $null }
    $rows = @(Import-Csv -LiteralPath $Path)
    if ($rows.Count -eq 0) { return $null }
    $names = $rows[0].PSObject.Properties.Name
    # PresentMon 1.x metrics use MsBetweenPresents; 2.x calls it FrameTime.
    $col = @('MsBetweenPresents', 'FrameTime', 'MsBetweenAppStart') | Where-Object { $names -contains $_ } | Select-Object -First 1
    if (-not $col) { throw "No frame time column in $Path" }
    # With more than one swap chain (launcher, overlay), keep the game's: the one with the most frames.
    if ($names -contains 'SwapChainAddress') {
        $rows = @(($rows | Group-Object SwapChainAddress | Sort-Object Count -Descending | Select-Object -First 1).Group)
    }
    $ft = New-Object Collections.Generic.List[double]
    foreach ($r in $rows) {
        $v = ConvertTo-Double $r.$col
        if (-not [double]::IsNaN($v) -and $v -gt 0) { $ft.Add($v) }
    }
    if ($ft.Count -eq 0) { return $null }
    $sorted = $ft.ToArray()
    [array]::Sort($sorted)
    $mean = ($sorted | Measure-Object -Average).Average
    $worstCount = [math]::Max(1, [int][math]::Ceiling(0.01 * $sorted.Count))
    $worst = ($sorted | Select-Object -Last $worstCount | Measure-Object -Average).Average
    return [pscustomobject]@{ Frames = $sorted.Count; AvgFps = 1000 / $mean; Low1 = 1000 / $worst }
}

function Get-Column([object[]]$Rows, [string]$Name) {
    if ($Rows.Count -eq 0 -or -not ($Rows[0].PSObject.Properties.Name -contains $Name)) { return @() }
    return [double[]]@($Rows | ForEach-Object { ConvertTo-Double $_.$Name })
}

function Get-ResourceStats([string]$Path) {
    $rows = @()
    if (Test-Path -LiteralPath $Path) { $rows = @(Import-Csv -LiteralPath $Path) }
    # rec_* adds up every recorder process (host, worker, ffmpeg); older results only have ffmpeg.
    $prefix = 'ff'
    if ($rows.Count -gt 0 -and $rows[0].PSObject.Properties.Name -contains 'rec_ws_mb') { $prefix = 'rec' }
    return [pscustomobject]@{
        Cpu       = Get-Mean (Get-Column $rows "$($prefix)_cpu_pct")
        WsMean    = Get-Mean (Get-Column $rows "$($prefix)_ws_mb")
        WsMax     = Get-Max (Get-Column $rows "$($prefix)_ws_mb")
        FfWsMean  = Get-Mean (Get-Column $rows 'ff_ws_mb')
        Encode    = Get-Mean (Get-Column $rows 'ff_encode_pct')
        VideoProc = Get-Mean (Get-Column $rows 'ff_videoproc_pct')
        Ff3d      = Get-Mean (Get-Column $rows 'ff_3d_pct')
        Game3d    = Get-Mean (Get-Column $rows 'game_3d_pct')
        Total3d   = Get-Mean (Get-Column $rows 'total_3d_pct')
    }
}

$runs = @()
foreach ($f in Get-ChildItem -LiteralPath $Dir -Filter 'run*-frames.csv' | Sort-Object Name) {
    if ($f.Name -notmatch '^(run\d+)-(on|off)-frames\.csv$') { continue }
    $tag = "$($Matches[1])-$($Matches[2])"
    $frames = Get-FrameStats $f.FullName
    if (-not $frames) { Write-Warning "No frames in $($f.Name)"; continue }
    $res = Get-ResourceStats (Join-Path $Dir "$tag-res.csv")
    $meta = $null
    $metaPath = Join-Path $Dir "$tag.json"
    if (Test-Path -LiteralPath $metaPath) { $meta = Get-Content -LiteralPath $metaPath -Raw | ConvertFrom-Json }
    $saveMs = [double]::NaN
    $snapshotMs = [double]::NaN
    $drop = [double]::NaN
    $dup = [double]::NaN
    if ($meta) {
        if ($null -ne $meta.saveMs) { $saveMs = [double]$meta.saveMs }
        if ($meta.PSObject.Properties.Name -contains 'snapshotMs' -and $null -ne $meta.snapshotMs) { $snapshotMs = [double]$meta.snapshotMs }
        if ($meta.recorder) {
            $drop = ConvertTo-Double $meta.recorder.drop_frames
            $dup = ConvertTo-Double $meta.recorder.dup_frames
        }
    }
    $runs += [pscustomobject]@{
        Tag = $tag; Mode = $Matches[2]; Frames = $frames.Frames; AvgFps = $frames.AvgFps; Low1 = $frames.Low1
        Res = $res; SaveMs = $saveMs; SnapshotMs = $snapshotMs; Drop = $drop; Dup = $dup
    }
}
if ($runs.Count -eq 0) { throw "No valid rounds in $Dir" }

$off = @($runs | Where-Object Mode -eq 'off')
$on = @($runs | Where-Object Mode -eq 'on')

function Get-ModeValue([object[]]$Set, [scriptblock]$Pick) {
    if ($Set.Count -eq 0) { return [double]::NaN }
    return Get-Mean ([double[]]@($Set | ForEach-Object $Pick))
}

function Format-Value([double]$Value, [string]$Format = '0.0', [string]$Suffix = '') {
    if ([double]::IsNaN($Value)) { return '–' }
    return $Value.ToString($Format, $inv) + $Suffix
}

function Format-MeanSd([object[]]$Set, [scriptblock]$Pick) {
    if ($Set.Count -eq 0) { return '–' }
    $vals = [double[]]@($Set | ForEach-Object $Pick)
    return '{0} ± {1}' -f (Format-Value (Get-Mean $vals)), (Format-Value (Get-StdDev $vals))
}

function Format-Pass([double]$Value, [double]$Limit) {
    if ([double]::IsNaN($Value)) { return '–' }
    if ($Value -le $Limit) { return 'yes' } else { return '**no**' }
}

$offAvg = Get-ModeValue $off { $_.AvgFps }
$onAvg = Get-ModeValue $on { $_.AvgFps }
$offLow = Get-ModeValue $off { $_.Low1 }
$onLow = Get-ModeValue $on { $_.Low1 }
$avgDrop = ($offAvg - $onAvg) / $offAvg * 100
$lowDrop = ($offLow - $onLow) / $offLow * 100
$cpu = Get-ModeValue $on { $_.Res.Cpu }
$wsMean = Get-ModeValue $on { $_.Res.WsMean }
$wsMax = Get-Max ([double[]]@($on | ForEach-Object { $_.Res.WsMax }))
$saveMs = Get-Max ([double[]]@($on | ForEach-Object { $_.SaveMs }))
$snapshotMs = Get-Max ([double[]]@($on | ForEach-Object { $_.SnapshotMs }))
$ffWs = Get-ModeValue $on { $_.Res.FfWsMean }
$drop = Get-Max ([double[]]@($on | ForEach-Object { $_.Drop }))
$dup = Get-Max ([double[]]@($on | ForEach-Object { $_.Dup }))

$md = New-Object Collections.Generic.List[string]
$md.Add('| Metric | Off | On | Change | Target | Pass |')
$md.Add('|---|---:|---:|---:|---|---|')
$md.Add("| Average FPS | $(Format-MeanSd $off { $_.AvgFps }) | $(Format-MeanSd $on { $_.AvgFps }) | $(Format-Value (-$avgDrop) '+0.0;-0.0' '%') | drop ≤ $MaxAvgDropPct% | $(Format-Pass $avgDrop $MaxAvgDropPct) |")
$md.Add("| 1% low | $(Format-MeanSd $off { $_.Low1 }) | $(Format-MeanSd $on { $_.Low1 }) | $(Format-Value (-$lowDrop) '+0.0;-0.0' '%') | drop ≤ $MaxLowDropPct% | $(Format-Pass $lowDrop $MaxLowDropPct) |")
$md.Add("| Recorder CPU | – | $(Format-Value $cpu '0.00' '%') | | < $MaxCpuPct% | $(Format-Pass $cpu $MaxCpuPct) |")
$md.Add("| Recorder RAM (mean / peak) | – | $(Format-Value $wsMean '0') / $(Format-Value $wsMax '0') MB | | < $MaxRamMb MB | $(Format-Pass $wsMean $MaxRamMb) |")
if (-not [double]::IsNaN($ffWs) -and $ffWs -ne $wsMean) { $md.Add("| RAM of ffmpeg alone (mean) | – | $(Format-Value $ffWs '0') MB | | | |") }
$md.Add("| Recorder GPU encoding (Video Encode + Video Decode) | – | $(Format-Value (Get-ModeValue $on { $_.Res.Encode }) '0.0' '%') | | | |")
$md.Add("| Recorder GPU Video Processing | – | $(Format-Value (Get-ModeValue $on { $_.Res.VideoProc }) '0.0' '%') | | | |")
$md.Add("| Recorder GPU 3D | – | $(Format-Value (Get-ModeValue $on { $_.Res.Ff3d }) '0.0' '%') | | | |")
$md.Add("| Game GPU 3D | $(Format-Value (Get-ModeValue $off { $_.Res.Game3d }) '0' '%') | $(Format-Value (Get-ModeValue $on { $_.Res.Game3d }) '0' '%') | | | |")
$md.Add("| Frames dropped / duplicated by the recorder | – | $(Format-Value $drop '0') / $(Format-Value $dup '0') | | | |")
$md.Add("| Clip safe after the hotkey (worst case) | – | $(Format-Value $snapshotMs '0' ' ms') | | | |")
$md.Add("| Clip save time (worst case) | – | $(Format-Value $saveMs '0' ' ms') | | < $MaxSaveMs ms | $(Format-Pass $saveMs $MaxSaveMs) |")
$md.Add('')
$md.Add('| Round | Average FPS | 1% low | Frames |')
$md.Add('|---|---:|---:|---:|')
foreach ($r in $runs) {
    $md.Add("| $($r.Tag) | $(Format-Value $r.AvgFps) | $(Format-Value $r.Low1) | $($r.Frames) |")
}

$text = $md -join "`r`n"
Set-Content -LiteralPath (Join-Path $Dir 'summary.md') -Value $text -Encoding UTF8
Write-Host ''
Write-Host $text
