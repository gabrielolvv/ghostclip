# Helpers shared by the Phase 0 scripts. Load with dot-source:
#   . "$PSScriptRoot\common.ps1"
# Works on Windows PowerShell 5.1 and PowerShell 7.

Set-StrictMode -Version 3.0
$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path -Parent $PSScriptRoot
$GhostclipData = Join-Path $env:LOCALAPPDATA 'Ghostclip'
$DefaultBufferDir = Join-Path $GhostclipData 'Buffer'
$ProbeFile = Join-Path $GhostclipData 'probe.json'
$RecordLog = Join-Path $GhostclipData 'record.log'
$RecordProgress = Join-Path $GhostclipData 'record-progress.txt'
$RecordPidFile = Join-Path $GhostclipData 'record.pid'
$Invariant = [Globalization.CultureInfo]::InvariantCulture

# Preference order for automatic detection.
$EncoderOrder = @('nvenc', 'amf', 'qsv')

function Find-Tool {
    param([Parameter(Mandatory)][string]$Name, [string]$Override)
    if ($Override) { return (Resolve-Path -LiteralPath $Override).Path }
    $local = Get-ChildItem -Path (Join-Path $RepoRoot 'tools') -Filter $Name -Recurse -File -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($local) { return $local.FullName }
    $cmd = Get-Command $Name -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($cmd) { return $cmd.Source }
    throw "$Name not found. Run tools\get-tools.ps1 or pass its path."
}

# Quoting per CommandLineToArgvW rules. PowerShell 5.1's Start-Process doesn't
# do this, and a path with a space would break the command line.
function ConvertTo-NativeArg([string]$Value) {
    if ($Value -ne '' -and $Value -notmatch '[\s"]') { return $Value }
    $escaped = ($Value -replace '(\\*)"', '$1$1\"') -replace '(\\+)$', '$1$1'
    return '"' + $escaped + '"'
}

function Join-NativeArgs([object[]]$Arguments) {
    return (@($Arguments) | ForEach-Object { ConvertTo-NativeArg ([string]$_) }) -join ' '
}

# Runs a process and returns exit code, stdout, stderr and duration.
function Invoke-Native {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][object[]]$Arguments,
        [int]$TimeoutSeconds = 60,
        [Diagnostics.ProcessPriorityClass]$Priority = 'Normal'
    )
    $psi = New-Object Diagnostics.ProcessStartInfo
    $psi.FileName = $Path
    $psi.Arguments = Join-NativeArgs $Arguments
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true

    $timedOut = $false
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $p = [Diagnostics.Process]::Start($psi)
    if ($Priority -ne 'Normal') { try { $p.PriorityClass = $Priority } catch { } }
    $out = $p.StandardOutput.ReadToEndAsync()
    $err = $p.StandardError.ReadToEndAsync()
    if (-not $p.WaitForExit($TimeoutSeconds * 1000)) {
        $timedOut = $true
        try { $p.Kill() } catch { }
    }
    $p.WaitForExit()
    $sw.Stop()
    return [pscustomobject]@{
        ExitCode = $p.ExitCode
        StdOut   = $out.Result
        StdErr   = $err.Result
        Seconds  = $sw.Elapsed.TotalSeconds
        TimedOut = $timedOut
    }
}

function Get-FFmpegVersion([string]$FFmpeg) {
    $r = Invoke-Native -Path $FFmpeg -Arguments @('-hide_banner', '-version') -TimeoutSeconds 15
    return ($r.StdOut -split "`r?`n")[0]
}

# Builds the video arguments for a hardware encoder.
#
# Encoder input variants:
#   direct  the encoder takes the BGRA texture from ddagrab and converts to YUV
#           internally (what that costs depends on the driver);
#   nv12    the BGRA -> NV12 conversion (and scaling, if any) happens first, in
#           an explicit GPU step: scale_d3d11 (D3D11 VideoProcessor) for
#           NVENC/AMF, vpp_qsv for QuickSync.
# Both keep the frame on the GPU. Which one costs fewer FPS is exactly what
# Phase 0 measures.
function Get-VideoArgs {
    param(
        [Parameter(Mandatory)][ValidateSet('nvenc', 'amf', 'qsv')][string]$Encoder,
        [ValidateSet('direct', 'nv12')][string]$Variant = 'nv12',
        [ValidateSet('h264', 'hevc')][string]$Codec = 'h264',
        [int]$Monitor = 0,
        [int]$Fps = 60,
        [int]$BitrateMbps = 20,
        [int]$GopSeconds = 2,
        [int]$Height = 0,
        # QSV frame queue. 1 saves ~30 MB of RAM (an iGPU's surfaces live in
        # system RAM), but the encoder waits on every frame: with the GPU
        # saturated by the game this can turn into dropped frames.
        [int]$AsyncDepth = 1,
        [switch]$NoLowPower
    )
    # Scaling implies an explicit conversion.
    if ($Height -gt 0) { $Variant = 'nv12' }
    # Spike: assumes 16:9 when scaling. Phase 1 reads the real monitor size.
    $width = [int]([math]::Round($Height * 16 / 9 / 2) * 2)

    $filter = "ddagrab=output_idx=${Monitor}:framerate=${Fps}"
    if ($Encoder -eq 'qsv') {
        $filter += ',hwmap=derive_device=qsv,format=qsv'
        if ($Variant -eq 'nv12') {
            $filter += ',vpp_qsv=format=nv12'
            if ($Height -gt 0) { $filter += ":w=${width}:h=${Height}" }
        }
    }
    elseif ($Variant -eq 'nv12') {
        $filter += ',scale_d3d11=format=nv12'
        if ($Height -gt 0) { $filter += ":width=${width}:height=${Height}" }
    }

    $gop = $Fps * $GopSeconds
    $rate = "${BitrateMbps}M"
    # Constant rate: the disk buffer size is predictable (20 Mbps x 60 s =
    # 150 MB). Fixed GOP and no B-frames: every 2 s segment starts on an IDR
    # and can be decoded on its own.
    $rateArgs = @('-r', $Fps, '-fps_mode', 'cfr', '-g', $gop, '-bf', 0,
        '-b:v', $rate, '-maxrate', $rate, '-bufsize', $rate)
    $forceKey = @('-force_key_frames', "expr:gte(t,n_forced*$GopSeconds)")

    switch ($Encoder) {
        'nvenc' {
            # p1 + ll: lowest latency and lowest load on the NVENC block.
            $codecArgs = @('-c:v', "${Codec}_nvenc", '-preset', 'p1', '-tune', 'll', '-rc', 'cbr',
                '-no-scenecut', 1, '-forced-idr', 1) + $forceKey
        }
        'amf' {
            $codecArgs = @('-c:v', "${Codec}_amf", '-usage', 'lowlatency', '-quality', 'speed', '-rc', 'cbr',
                '-header_insertion_mode', 'idr')
        }
        'qsv' {
            # low_power uses the fixed-function block (VDENC) instead of the
            # GPU's execution units. On an iGPU that also runs the game this
            # matters a lot.
            $codecArgs = @('-c:v', "${Codec}_qsv", '-preset', 'veryfast', '-forced_idr', 1, '-async_depth', $AsyncDepth)
            if ($Codec -eq 'h264') { $codecArgs += @('-look_ahead', 0) }
            if (-not $NoLowPower) { $codecArgs += @('-low_power', 1) }
            $codecArgs += $forceKey
        }
    }

    return [pscustomobject]@{
        Encoder = $Encoder
        Variant = $Variant
        Init    = @('-init_hw_device', 'd3d11va=gc', '-filter_hw_device', 'gc')
        Filter  = $filter
        Codec   = $rateArgs + $codecArgs
    }
}

# Tries each encoder/variant pair by capturing a few frames into the null
# muxer. Nothing is written to disk.
function Invoke-EncoderProbe {
    param(
        [Parameter(Mandatory)][string]$FFmpeg,
        [string[]]$Encoders = $EncoderOrder,
        [string[]]$Variants = @('nv12', 'direct'),
        [ValidateSet('h264', 'hevc')][string]$Codec = 'h264',
        [int]$Monitor = 0,
        [int]$Frames = 120,
        [switch]$NoLowPower
    )
    $available = (Invoke-Native -Path $FFmpeg -Arguments @('-hide_banner', '-encoders') -TimeoutSeconds 15).StdOut
    $results = @()
    foreach ($enc in $Encoders) {
        foreach ($variant in $Variants) {
            $name = "${Codec}_$enc"
            if ($available -notmatch "\s$name\s") {
                $results += [pscustomobject]@{ Encoder = $enc; Variant = $variant; Ok = $false; Seconds = 0; Error = "$name is not in this FFmpeg build" }
                continue
            }
            $v = Get-VideoArgs -Encoder $enc -Variant $variant -Codec $Codec -Monitor $Monitor -NoLowPower:$NoLowPower
            $ffArgs = @('-hide_banner', '-nostdin', '-loglevel', 'error') + $v.Init +
                @('-filter_complex', $v.Filter, '-frames:v', $Frames) + $v.Codec + @('-f', 'null', '-')
            $r = Invoke-Native -Path $FFmpeg -Arguments $ffArgs -TimeoutSeconds 30
            $ok = ($r.ExitCode -eq 0) -and -not $r.TimedOut
            $msg = ''
            if (-not $ok) {
                $lines = @($r.StdErr -split "`r?`n" | Where-Object { $_.Trim() })
                if ($r.TimedOut) { $msg = 'timed out' }
                elseif ($lines.Count -gt 0) { $msg = $lines[0].Trim() }
                else { $msg = "exit code $($r.ExitCode)" }
            }
            $results += [pscustomobject]@{ Encoder = $enc; Variant = $variant; Ok = $ok; Seconds = [math]::Round($r.Seconds, 2); Error = $msg }
        }
    }
    return $results
}

function Save-ProbeResult {
    param([string]$FFmpeg, [string]$Codec, [int]$Monitor, [object[]]$Results)
    $choice = $Results | Where-Object { $_.Ok } | Select-Object -First 1
    $doc = [ordered]@{
        date    = (Get-Date).ToString('o')
        ffmpeg  = $FFmpeg
        version = Get-FFmpegVersion $FFmpeg
        codec   = $Codec
        monitor = $Monitor
        choice  = $choice
        results = $Results
    }
    New-Item -ItemType Directory -Force -Path $GhostclipData | Out-Null
    $doc | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $ProbeFile -Encoding UTF8
    return $choice
}

# Picks encoder and variant: uses the saved probe when it matches the request,
# otherwise runs a new probe. Never falls back to a CPU encoder.
function Get-EncoderChoice {
    param(
        [Parameter(Mandatory)][string]$FFmpeg,
        [string]$Encoder = 'auto',
        [string]$Variant = 'auto',
        [string]$Codec = 'h264',
        [int]$Monitor = 0,
        [switch]$NoLowPower
    )
    $encoders = if ($Encoder -eq 'auto') { $EncoderOrder } else { @($Encoder) }
    $variants = if ($Variant -eq 'auto') { @('nv12', 'direct') } else { @($Variant) }

    if (Test-Path -LiteralPath $ProbeFile) {
        $saved = Get-Content -LiteralPath $ProbeFile -Raw | ConvertFrom-Json
        if ($saved.ffmpeg -eq $FFmpeg -and $saved.codec -eq $Codec -and $saved.monitor -eq $Monitor) {
            foreach ($e in $encoders) {
                foreach ($v in $variants) {
                    $hit = @($saved.results | Where-Object { $_.Ok -and $_.Encoder -eq $e -and $_.Variant -eq $v })
                    if ($hit.Count -gt 0) { return $hit[0] }
                }
            }
        }
    }

    Write-Host 'Detecting hardware encoder...'
    $results = Invoke-EncoderProbe -FFmpeg $FFmpeg -Encoders $encoders -Variants $variants -Codec $Codec -Monitor $Monitor -NoLowPower:$NoLowPower
    $choice = Save-ProbeResult -FFmpeg $FFmpeg -Codec $Codec -Monitor $Monitor -Results $results
    if (-not $choice) {
        $results | Format-Table -AutoSize | Out-String | Write-Host
        throw 'No hardware encoder worked. Ghostclip does not record with a CPU encoder.'
    }
    return $choice
}

# Picks the segments that make up the clip, oldest first.
#
# The newest segment may still be being written; it is included by default
# because a partial MPEG-TS is readable and holds the last moments before the
# hotkey. Order comes from the modification time (with -segment_wrap the names
# wrap around). A gap larger than 2.5 segments means another recording
# session, and selection stops there.
function Select-ClipSegments {
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Files,
        [Parameter(Mandatory)][double]$ClipSeconds,
        [Parameter(Mandatory)][double]$SegmentSeconds,
        [switch]$ExcludeCurrent
    )
    $sorted = @($Files | Where-Object { $_.Length -gt 0 } | Sort-Object LastWriteTime -Descending)
    if ($ExcludeCurrent -and $sorted.Count -gt 0) { $sorted = @($sorted | Select-Object -Skip 1) }
    if ($sorted.Count -eq 0) { return @() }

    $selected = New-Object Collections.Generic.List[object]
    $selected.Add($sorted[0])
    if ($ExcludeCurrent -or $sorted.Count -eq 1) {
        $covered = $SegmentSeconds
    }
    else {
        $covered = ($sorted[0].LastWriteTime - $sorted[1].LastWriteTime).TotalSeconds
    }
    $maxGap = 2.5 * $SegmentSeconds
    for ($i = 1; $i -lt $sorted.Count -and $covered -lt $ClipSeconds; $i++) {
        $gap = ($sorted[$i - 1].LastWriteTime - $sorted[$i].LastWriteTime).TotalSeconds
        if ($i -gt 1 -and $gap -gt $maxGap) { break }
        $selected.Add($sorted[$i])
        $covered += $SegmentSeconds
    }
    $result = $selected.ToArray()
    [array]::Reverse($result)
    return $result
}

function Get-SafeGameName([string]$Game) {
    if (-not $Game) { return 'Desktop' }
    $invalid = [IO.Path]::GetInvalidFileNameChars()
    $chars = foreach ($c in $Game.ToCharArray()) { if ($invalid -contains $c) { '_' } else { $c } }
    $name = (-join $chars).Trim().TrimEnd('.')
    if (-not $name) { return 'Desktop' }
    return $name
}

# <Root>\<Game>\<Game>_YYYY-MM-DD_HH-MM-SS.mp4, with a _2, _3... suffix if it exists.
function Get-ClipPath([string]$Root, [string]$Game, [datetime]$Time) {
    $safe = Get-SafeGameName $Game
    $dir = Join-Path $Root $safe
    $base = '{0}_{1}' -f $safe, $Time.ToString('yyyy-MM-dd_HH-mm-ss', $Invariant)
    $path = Join-Path $dir "$base.mp4"
    $n = 2
    while (Test-Path -LiteralPath $path) {
        $path = Join-Path $dir ('{0}_{1}.mp4' -f $base, $n)
        $n++
    }
    return $path
}
