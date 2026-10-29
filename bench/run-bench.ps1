<#
.SYNOPSIS
Measures the recorder's cost in a game's built-in benchmark, with PresentMon.

.DESCRIPTION
Runs N rounds with the recorder off and N with it on. In each round it:
  - starts spike\record.ps1 (on rounds), already in steady state before
    measuring;
  - collects the game's frames with PresentMon for -Seconds seconds;
  - samples ffmpeg's CPU and RAM and the GPU engines' usage (encoding,
    Video Processing, 3D) every -SampleSeconds.
At the end it writes summary.md with the table (bench\summarize.ps1).

Default order is alternating (off, on, off, on...): on a laptop, heat lowers
the clock over the session, and grouping the "on" rounds at the end would put
all of that effect on the recorder. Use -Order Grouped for 3 + 3.

PresentMon needs PowerShell as administrator (or the user in the
"Performance Log Users" group).

.EXAMPLE
# Manual: the script asks you to start the benchmark on each round.
.\bench\run-bench.ps1 -ProcessName "Cyberpunk2077.exe"

.EXAMPLE
# Automatic: launches the game with its benchmark arguments on each round.
.\bench\run-bench.ps1 -ProcessName "SOTTR.exe" -GameExe "D:\Games\SOTTR\SOTTR.exe" -GameArgs "-benchmark" -RecordArgs @{ Encoder = 'qsv'; Variant = 'nv12' }
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ProcessName,
    [int]$Runs = 3,
    [int]$Seconds = 60,
    # Time between Enter (or launching the game) and the start of collection.
    [int]$DelaySeconds = 10,
    [int]$CooldownSeconds = 30,
    [int]$SampleSeconds = 1,
    [ValidateSet('Alternating', 'Grouped')][string]$Order = 'Alternating',
    [string]$GameExe,
    [string]$GameArgs,
    [int]$GameStartupSeconds = 45,
    # Parameters passed on to record.ps1, e.g. @{ Encoder = 'qsv'; Variant = 'direct' }
    [hashtable]$RecordArgs = @{},
    # Saves a clip halfway through each "on" round and times it.
    [switch]$SaveDuringRun,
    [switch]$NoGpuCounters,
    [string]$PresentMon,
    [string]$OutDir
)
. "$PSScriptRoot\..\spike\common.ps1"

$principal = New-Object Security.Principal.WindowsPrincipal ([Security.Principal.WindowsIdentity]::GetCurrent())
$isAdmin = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
$isPerfLog = $principal.IsInRole((New-Object Security.Principal.SecurityIdentifier 'S-1-5-32-559'))
if (-not ($isAdmin -or $isPerfLog)) {
    throw 'PresentMon needs PowerShell as administrator (or the user in the "Performance Log Users" group).'
}

$pmPath = Find-Tool 'PresentMon*.exe' $PresentMon
$recordPs1 = Join-Path $RepoRoot 'spike\record.ps1'
$savePs1 = Join-Path $RepoRoot 'spike\save.ps1'
$psExe = (Get-Process -Id $PID).Path

$exeName = if ($ProcessName.EndsWith('.exe', 'OrdinalIgnoreCase')) { $ProcessName } else { "$ProcessName.exe" }
$procBase = [IO.Path]::GetFileNameWithoutExtension($exeName)

if (-not $OutDir) { $OutDir = Join-Path $PSScriptRoot ('results\' + (Get-Date -Format 'yyyy-MM-dd_HH-mm-ss')) }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$schedule = if ($Order -eq 'Grouped') { @('off') * $Runs + @('on') * $Runs } else { foreach ($i in 1..$Runs) { 'off'; 'on' } }

$recArgs = @()
foreach ($k in $RecordArgs.Keys) {
    $v = $RecordArgs[$k]
    if ($v -is [bool] -or $v -is [switch]) { if ($v) { $recArgs += "-$k" } }
    else { $recArgs += @("-$k", $v) }
}

function Format-Num([double]$Value) { return $Value.ToString('0.###', $Invariant) }

function Start-Recorder {
    Remove-Item -LiteralPath $RecordPidFile -ErrorAction SilentlyContinue
    $a = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $recordPs1) + $recArgs
    $shell = Start-Process -FilePath $psExe -ArgumentList (Join-NativeArgs $a) -WindowStyle Minimized -PassThru
    # The first use may run the encoder probe before recording.
    $deadline = (Get-Date).AddSeconds(90)
    while ((Get-Date) -lt $deadline) {
        if (Test-Path -LiteralPath $RecordPidFile) {
            $ffPid = [int](Get-Content -LiteralPath $RecordPidFile -Raw)
            $ff = Get-Process -Id $ffPid -ErrorAction SilentlyContinue
            if ($ff) { return [pscustomobject]@{ Shell = $shell; FFmpeg = $ff } }
        }
        if ($shell.HasExited) { break }
        Start-Sleep -Milliseconds 250
    }
    $tail = if (Test-Path -LiteralPath $RecordLog) { (Get-Content -LiteralPath $RecordLog -Tail 10) -join "`n" } else { '' }
    throw "The recorder did not start.`n$tail"
}

function Stop-Recorder($Recorder) {
    if (-not $Recorder) { return }
    foreach ($p in $Recorder.FFmpeg, $Recorder.Shell) {
        if ($p -and -not $p.HasExited) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue }
    }
}

function Read-RecordProgress {
    $info = [ordered]@{ drop_frames = $null; dup_frames = $null; fps = $null; speed = $null }
    if (-not (Test-Path -LiteralPath $RecordProgress)) { return $info }
    foreach ($line in Get-Content -LiteralPath $RecordProgress) {
        $kv = $line -split '=', 2
        if ($kv.Count -eq 2 -and $info.Contains($kv[0])) { $info[$kv[0]] = $kv[1].Trim() }
    }
    return $info
}

# Sums utilization per (pid, engine type) from the GPU counters. The WMI
# classes are not localized, unlike Get-Counter's counter names.
function Get-GpuUsage {
    $usage = @{}
    $engines = Get-CimInstance -ClassName Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine -Property Name, UtilizationPercentage
    foreach ($e in $engines) {
        if ($e.Name -match '^pid_(\d+)_.*engtype_(.+)$') {
            $key = "$($Matches[1])|$($Matches[2])"
            $prev = 0.0
            if ($usage.ContainsKey($key)) { $prev = $usage[$key] }
            $usage[$key] = $prev + [double]$e.UtilizationPercentage
        }
    }
    return $usage
}

function Get-Usage($Usage, [int]$ProcessId, [string]$Engine) {
    $key = "$ProcessId|$Engine"
    if ($Usage.ContainsKey($key)) { return $Usage[$key] }
    return 0.0
}

$runsMeta = @()
$cores = [Environment]::ProcessorCount
for ($i = 0; $i -lt $schedule.Count; $i++) {
    $mode = $schedule[$i]
    $tag = 'run{0:00}-{1}' -f ($i + 1), $mode
    $framesCsv = Join-Path $OutDir "$tag-frames.csv"
    $resCsv = Join-Path $OutDir "$tag-res.csv"
    $label = if ($mode -eq 'on') { 'ON' } else { 'off' }
    Write-Host ''
    Write-Host ("Round {0}/{1}: recorder {2}" -f ($i + 1), $schedule.Count, $label)

    if ($mode -eq 'off' -and (Test-Path -LiteralPath $RecordPidFile)) {
        Write-Warning 'A record.ps1 is running. Close it for the "off" round to count.'
    }

    $recorder = $null
    $game = $null
    $saveMs = $null
    $rows = New-Object Collections.Generic.List[object]
    try {
        if ($mode -eq 'on') { $recorder = Start-Recorder }

        if ($GameExe) {
            $game = Start-Process -FilePath $GameExe -ArgumentList $GameArgs -PassThru
            Write-Host "Game launched. Waiting $GameStartupSeconds s."
            Start-Sleep -Seconds $GameStartupSeconds
        }
        else {
            Read-Host "Get the benchmark ready, press Enter and start it within $DelaySeconds s" | Out-Null
        }

        $pmArgs = @('--process_name', $exeName, '--output_file', $framesCsv, '--delay', $DelaySeconds,
            '--timed', $Seconds, '--terminate_after_timed', '--stop_existing_session', '--no_console_stats',
            # Stable columns (MsBetweenPresents, SwapChainAddress): the default CSV changes between versions.
            '--v1_metrics')
        $pm = Start-Process -FilePath $pmPath -ArgumentList (Join-NativeArgs $pmArgs) -WindowStyle Hidden -PassThru

        $sw = [Diagnostics.Stopwatch]::StartNew()
        $prevCpu = $null
        $prevT = 0.0
        while (-not $pm.HasExited) {
            Start-Sleep -Seconds $SampleSeconds
            $t = $sw.Elapsed.TotalSeconds - $DelaySeconds
            if ($t -gt $Seconds + 60) {
                Write-Warning 'PresentMon did not finish in the expected time; stopping it.'
                Stop-Process -Id $pm.Id -Force -ErrorAction SilentlyContinue
                break
            }
            if ($t -lt 0) { continue }

            $row = [ordered]@{ t = Format-Num $t }
            $ffPid = 0
            if ($recorder -and -not $recorder.FFmpeg.HasExited) {
                $ff = $recorder.FFmpeg
                $ff.Refresh()
                $ffPid = $ff.Id
                $cpu = $ff.TotalProcessorTime.TotalSeconds
                if ($null -ne $prevCpu) {
                    $row.ff_cpu_pct = Format-Num (($cpu - $prevCpu) / ($t - $prevT) / $cores * 100)
                }
                $prevCpu = $cpu
                $row.ff_ws_mb = Format-Num ($ff.WorkingSet64 / 1MB)
                $row.ff_private_mb = Format-Num ($ff.PrivateMemorySize64 / 1MB)
            }
            $prevT = $t

            if (-not $NoGpuCounters) {
                $gpu = Get-GpuUsage
                $gamePid = @(Get-Process -Name $procBase -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty Id)
                if ($ffPid) {
                    # On Intel, QSV shows up as "VideoDecode" (the same video engine
                    # decodes and encodes). The recorder decodes nothing, so adding is safe.
                    $row.ff_encode_pct = Format-Num ((Get-Usage $gpu $ffPid 'VideoEncode') + (Get-Usage $gpu $ffPid 'VideoDecode'))
                    $row.ff_videoproc_pct = Format-Num (Get-Usage $gpu $ffPid 'VideoProcessing')
                    $row.ff_3d_pct = Format-Num (Get-Usage $gpu $ffPid '3D')
                }
                if ($gamePid.Count -gt 0) { $row.game_3d_pct = Format-Num (Get-Usage $gpu $gamePid[0] '3D') }
                $total3d = 0.0
                foreach ($k in $gpu.Keys) { if ($k.EndsWith('|3D')) { $total3d += $gpu[$k] } }
                $row.total_3d_pct = Format-Num ([math]::Min(100, $total3d))
            }
            $rows.Add([pscustomobject]$row)

            if ($SaveDuringRun -and $mode -eq 'on' -and $null -eq $saveMs -and $t -ge $Seconds / 2) {
                $saved = & $savePs1 -Game 'Bench' -Quiet
                $saveMs = $saved.ElapsedMs
                Write-Host "  clip saved in $saveMs ms"
            }
        }
    }
    finally {
        $progress = $null
        if ($recorder) { $progress = Read-RecordProgress }
        Stop-Recorder $recorder
        if ($game -and -not $game.HasExited) {
            $game.CloseMainWindow() | Out-Null
            if (-not $game.WaitForExit(10000)) { Stop-Process -Id $game.Id -Force -ErrorAction SilentlyContinue }
        }
    }

    # Same columns on every row, so the CSV doesn't lose fields.
    $columns = @('t', 'ff_cpu_pct', 'ff_ws_mb', 'ff_private_mb', 'ff_encode_pct', 'ff_videoproc_pct', 'ff_3d_pct', 'game_3d_pct', 'total_3d_pct')
    $rows | Select-Object -Property $columns | Export-Csv -LiteralPath $resCsv -NoTypeInformation -Encoding UTF8

    if (-not (Test-Path -LiteralPath $framesCsv)) {
        Write-Warning "PresentMon did not produce $framesCsv. Is the process name ($exeName) right?"
    }

    $meta = [ordered]@{ tag = $tag; mode = $mode; saveMs = $saveMs; recorder = $progress }
    $meta | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $OutDir "$tag.json") -Encoding UTF8
    $runsMeta += [pscustomobject]$meta

    if ($i -lt $schedule.Count - 1 -and $CooldownSeconds -gt 0) {
        Write-Host "Cooling down for $CooldownSeconds s."
        Start-Sleep -Seconds $CooldownSeconds
    }
}

$gpuInfo = Get-CimInstance Win32_VideoController | ForEach-Object { "$($_.Name) ($($_.DriverVersion))" }
$probe = if (Test-Path -LiteralPath $ProbeFile) { Get-Content -LiteralPath $ProbeFile -Raw | ConvertFrom-Json } else { $null }
[ordered]@{
    date       = (Get-Date).ToString('o')
    process    = $exeName
    seconds    = $Seconds
    order      = $Order
    cpu        = (Get-CimInstance Win32_Processor | Select-Object -First 1).Name
    gpu        = @($gpuInfo)
    os         = [Environment]::OSVersion.VersionString
    recordArgs = $RecordArgs
    probe      = $probe
    runs       = $runsMeta
} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $OutDir 'meta.json') -Encoding UTF8

& "$PSScriptRoot\summarize.ps1" -Dir $OutDir
