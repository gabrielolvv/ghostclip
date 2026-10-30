<#
.SYNOPSIS
Measures Ghostclip's cost in a game's built-in benchmark, with PresentMon.

.DESCRIPTION
Runs N rounds with the recorder off and N with it on. In each round it:
  - starts Ghostclip (on rounds) and waits until it is recording, so the
    measurement starts in steady state;
  - collects the game's frames with PresentMon for -Seconds seconds;
  - samples CPU and RAM of every recorder process (Ghostclip.exe,
    Ghostclip.Worker.exe and ffmpeg.exe) and the GPU engines' usage
    (encoding, Video Processing, 3D) every -SampleSeconds.
At the end it writes summary.md with the table (bench\summarize.ps1).

Ghostclip runs with your own settings (%LOCALAPPDATA%\Ghostclip\Data\config.json),
so set the resolution, bitrate and audio you want to measure there first.
A running Ghostclip is closed before the first round and started again at the
end. -Recorder Spike measures the Phase 0 script (spike\record.ps1) instead.

Default order is alternating (off, on, off, on...): on a laptop, heat lowers
the clock over the session, and grouping the "on" rounds at the end would put
all of that effect on the recorder. Use -Order Grouped for 3 + 3.

PresentMon needs PowerShell as administrator (or the user in the
"Performance Log Users" group).

.EXAMPLE
# Manual: the script asks you to start the benchmark on each round.
.\bench\run-bench.ps1 -ProcessName "Cyberpunk2077.exe" -SaveDuringRun

.EXAMPLE
# Automatic: launches the game with its benchmark arguments on each round.
.\bench\run-bench.ps1 -ProcessName "SOTTR.exe" -GameExe "D:\Games\SOTTR\SOTTR.exe" -GameArgs "-benchmark"

.EXAMPLE
# The Phase 0 recorder script, with its own options.
.\bench\run-bench.ps1 -ProcessName "SOTTR.exe" -Recorder Spike -RecordArgs @{ Encoder = 'qsv'; Variant = 'nv12' }
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
    [ValidateSet('App', 'Spike')][string]$Recorder = 'App',
    # Ghostclip.exe to run. Default: the installed copy, else the Release build, else the Debug build.
    [string]$Exe,
    # Parameters passed on to record.ps1 with -Recorder Spike, e.g. @{ Encoder = 'qsv'; Variant = 'direct' }
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

$appData = Join-Path $env:LOCALAPPDATA 'Ghostclip\Data'
$appLogs = Join-Path $appData 'Logs'
$appExe = $null
if ($Recorder -eq 'App') {
    $candidates = if ($Exe) { @($Exe) } else {
        @((Join-Path $env:LOCALAPPDATA 'Ghostclip\current\Ghostclip.exe'),
          (Join-Path $RepoRoot 'artifacts\bin\Release\Ghostclip.exe'),
          (Join-Path $RepoRoot 'artifacts\bin\Debug\Ghostclip.exe'))
    }
    $appExe = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if (-not $appExe) { throw 'Ghostclip.exe not found. Install it, run "dotnet build -c Release" or pass -Exe.' }
    $appExe = (Resolve-Path -LiteralPath $appExe).Path
    Write-Host "Recorder: $appExe"
}

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

function Get-Timestamp { return (Get-Date).ToString('yyyy-MM-dd HH:mm:ss.fff', $Invariant) }

# Log lines written since $Since. Lines start with the same timestamp format,
# so comparing strings is comparing times.
function Get-LogLines([string]$Name, [string]$Since) {
    $files = Get-ChildItem -LiteralPath $appLogs -Filter "$Name-*.log" -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime | Select-Object -Last 2
    foreach ($f in $files) {
        foreach ($line in Get-Content -LiteralPath $f.FullName) {
            if ([string]::CompareOrdinal($line, $Since) -ge 0) { $line }
        }
    }
}

function Get-ChildProcess([int]$ParentId, [string]$Name) {
    $p = Get-CimInstance Win32_Process -Filter "ParentProcessId=$ParentId AND Name='$Name'" | Select-Object -First 1
    if ($p) { return Get-Process -Id $p.ProcessId -ErrorAction SilentlyContinue }
    return $null
}

function Test-GhostclipRunning { return [bool](Get-Process -Name Ghostclip, Ghostclip.Worker -ErrorAction SilentlyContinue) }

# Asks the running instance to quit, the same way the uninstaller does.
function Stop-Ghostclip {
    $exit = $null
    if ([Threading.EventWaitHandle]::TryOpenExisting('Local\Ghostclip.Exit', [ref]$exit)) {
        [void]$exit.Set()
        $exit.Dispose()
    }
    $deadline = (Get-Date).AddSeconds(15)
    while ((Test-GhostclipRunning) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 250 }
    Get-Process -Name Ghostclip, Ghostclip.Worker -ErrorAction SilentlyContinue | Stop-Process -Force
}

function Start-App {
    $since = Get-Timestamp
    $app = Start-Process -FilePath $appExe -PassThru
    # The first run on a PC probes the encoders before recording.
    $deadline = (Get-Date).AddSeconds(90)
    while ((Get-Date) -lt $deadline -and -not $app.HasExited) {
        Start-Sleep -Milliseconds 500
        $pattern = 'Recording with (.+?), \d+ fps'
        $line = Get-LogLines 'worker' $since | Where-Object { $_ -match $pattern } | Select-Object -Last 1
        if (-not ($line -match $pattern)) { continue }
        $encoder = $Matches[1]
        $worker = Get-ChildProcess $app.Id 'Ghostclip.Worker.exe'
        $ff = if ($worker) { Get-ChildProcess $worker.Id 'ffmpeg.exe' } else { $null }
        if ($ff) {
            return [pscustomobject]@{ Processes = @($app, $worker, $ff); FFmpeg = $ff; Encoder = $encoder; Since = $since }
        }
    }
    $tail = (Get-LogLines 'worker' $since | Select-Object -Last 10) -join "`n"
    throw "Ghostclip did not start recording. Logs: $appLogs`n$tail"
}

function Start-Spike {
    Remove-Item -LiteralPath $RecordPidFile -ErrorAction SilentlyContinue
    $a = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $recordPs1) + $recArgs
    $shell = Start-Process -FilePath $psExe -ArgumentList (Join-NativeArgs $a) -WindowStyle Minimized -PassThru
    # The first use may run the encoder probe before recording.
    $deadline = (Get-Date).AddSeconds(90)
    while ((Get-Date) -lt $deadline) {
        if (Test-Path -LiteralPath $RecordPidFile) {
            $ffPid = [int](Get-Content -LiteralPath $RecordPidFile -Raw)
            $ff = Get-Process -Id $ffPid -ErrorAction SilentlyContinue
            # The PowerShell shell around it is the harness, not the recorder.
            if ($ff) { return [pscustomobject]@{ Processes = @($ff); FFmpeg = $ff; Shell = $shell; Encoder = $null } }
        }
        if ($shell.HasExited) { break }
        Start-Sleep -Milliseconds 250
    }
    $tail = if (Test-Path -LiteralPath $RecordLog) { (Get-Content -LiteralPath $RecordLog -Tail 10) -join "`n" } else { '' }
    throw "The recorder did not start.`n$tail"
}

function Start-Recorder { if ($Recorder -eq 'App') { return Start-App } else { return Start-Spike } }

function Stop-Recorder($Rec) {
    if (-not $Rec) { return }
    if ($Recorder -eq 'App') { Stop-Ghostclip; return }
    foreach ($p in $Rec.FFmpeg, $Rec.Shell) {
        if ($p -and -not $p.HasExited) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue }
    }
}

# drop_frames / dup_frames as FFmpeg reported them.
function Read-RecordProgress($Rec) {
    $info = [ordered]@{ drop_frames = $null; dup_frames = $null; fps = $null; speed = $null }
    if ($Recorder -eq 'App') {
        # The worker logs the counts when it stops; its log is flushed every 2 s.
        $deadline = (Get-Date).AddSeconds(5)
        while ((Get-Date) -lt $deadline) {
            $pattern = 'Recorder stopped after .*: (\d+) frames, (\d+) dropped, (\d+) duplicated'
            $line = Get-LogLines 'worker' $Rec.Since | Where-Object { $_ -match $pattern } | Select-Object -Last 1
            if ($line -match $pattern) { $info.drop_frames = $Matches[2]; $info.dup_frames = $Matches[3]; break }
            Start-Sleep -Milliseconds 500
        }
        return $info
    }
    if (-not (Test-Path -LiteralPath $RecordProgress)) { return $info }
    foreach ($line in Get-Content -LiteralPath $RecordProgress) {
        $kv = $line -split '=', 2
        if ($kv.Count -eq 2 -and $info.Contains($kv[0])) { $info[$kv[0]] = $kv[1].Trim() }
    }
    return $info
}

# Same path as the hotkey: the running instance takes the snapshot and remuxes.
# Returns the times Ghostclip logged: snapshot (clip safe) and snapshot + remux (MP4 written).
function Save-AppClip {
    $since = Get-Timestamp
    Start-Process -FilePath $appExe -ArgumentList '--save' -Wait
    $snapshot = $null
    $deadline = (Get-Date).AddSeconds(15)
    while ((Get-Date) -lt $deadline) {
        foreach ($line in Get-LogLines 'host' $since) {
            if ($line -match 'Snapshot of .* in (\d+) ms') { $snapshot = [double]$Matches[1] }
            elseif ($line -match 'Clip saved: .*\(remux (\d+) ms\)' -and $null -ne $snapshot) {
                return [pscustomobject]@{ SnapshotMs = $snapshot; SaveMs = $snapshot + [double]$Matches[1] }
            }
        }
        Start-Sleep -Milliseconds 200
    }
    Write-Warning 'No "Clip saved" line in the Ghostclip log within 15 s.'
    return [pscustomobject]@{ SnapshotMs = $snapshot; SaveMs = $null }
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

$wasRunning = $Recorder -eq 'App' -and (Test-GhostclipRunning)
if ($wasRunning) {
    Write-Host 'Closing the running Ghostclip for the "off" rounds.'
    Stop-Ghostclip
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

    if ($mode -eq 'off') {
        if ($Recorder -eq 'App' -and (Test-GhostclipRunning)) { Stop-Ghostclip }
        if ($Recorder -eq 'Spike' -and (Test-Path -LiteralPath $RecordPidFile)) {
            Write-Warning 'A record.ps1 is running. Close it for the "off" round to count.'
        }
    }

    $rec = $null
    $game = $null
    $save = $null
    $rows = New-Object Collections.Generic.List[object]
    try {
        if ($mode -eq 'on') {
            $rec = Start-Recorder
            if ($rec.Encoder) { Write-Host "  recording with $($rec.Encoder)" }
        }

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
        $prevCpu = @{}
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
            if ($rec) {
                # rec_*: every recorder process together; ff_*: ffmpeg alone.
                $cpuSum = 0.0; $wsSum = 0.0; $privSum = 0.0; $haveCpu = $true
                foreach ($p in $rec.Processes) {
                    if ($p.HasExited) { continue }
                    $p.Refresh()
                    $cpu = $p.TotalProcessorTime.TotalSeconds
                    if ($prevCpu.ContainsKey($p.Id)) {
                        $pct = ($cpu - $prevCpu[$p.Id]) / ($t - $prevT) / $cores * 100
                        $cpuSum += $pct
                        if ($p.Id -eq $rec.FFmpeg.Id) { $row.ff_cpu_pct = Format-Num $pct }
                    }
                    else { $haveCpu = $false }
                    $prevCpu[$p.Id] = $cpu
                    $wsSum += $p.WorkingSet64 / 1MB
                    $privSum += $p.PrivateMemorySize64 / 1MB
                    if ($p.Id -eq $rec.FFmpeg.Id) {
                        $ffPid = $p.Id
                        $row.ff_ws_mb = Format-Num ($p.WorkingSet64 / 1MB)
                        $row.ff_private_mb = Format-Num ($p.PrivateMemorySize64 / 1MB)
                    }
                }
                if ($haveCpu) { $row.rec_cpu_pct = Format-Num $cpuSum }
                $row.rec_ws_mb = Format-Num $wsSum
                $row.rec_private_mb = Format-Num $privSum
                if (-not $ffPid) { Write-Warning 'ffmpeg is not running: the recorder restarted during the round.' }
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

            if ($SaveDuringRun -and $mode -eq 'on' -and $null -eq $save -and $t -ge $Seconds / 2) {
                if ($Recorder -eq 'App') { $save = Save-AppClip }
                else { $save = [pscustomobject]@{ SnapshotMs = $null; SaveMs = (& $savePs1 -Game 'Bench' -Quiet).ElapsedMs } }
                Write-Host "  clip saved in $($save.SaveMs) ms (safe after $($save.SnapshotMs) ms)"
            }
        }
    }
    finally {
        $progress = $null
        if ($Recorder -eq 'Spike' -and $rec) { $progress = Read-RecordProgress $rec }
        Stop-Recorder $rec
        if ($Recorder -eq 'App' -and $rec) { $progress = Read-RecordProgress $rec }
        if ($game -and -not $game.HasExited) {
            $game.CloseMainWindow() | Out-Null
            if (-not $game.WaitForExit(10000)) { Stop-Process -Id $game.Id -Force -ErrorAction SilentlyContinue }
        }
    }

    # Same columns on every row, so the CSV doesn't lose fields.
    $columns = @('t', 'rec_cpu_pct', 'rec_ws_mb', 'rec_private_mb', 'ff_cpu_pct', 'ff_ws_mb', 'ff_private_mb',
        'ff_encode_pct', 'ff_videoproc_pct', 'ff_3d_pct', 'game_3d_pct', 'total_3d_pct')
    $rows | Select-Object -Property $columns | Export-Csv -LiteralPath $resCsv -NoTypeInformation -Encoding UTF8

    if (-not (Test-Path -LiteralPath $framesCsv)) {
        Write-Warning "PresentMon did not produce $framesCsv. Is the process name ($exeName) right?"
    }

    $meta = [ordered]@{
        tag = $tag; mode = $mode; encoder = $(if ($rec) { $rec.Encoder } else { $null })
        saveMs = $(if ($save) { $save.SaveMs } else { $null }); snapshotMs = $(if ($save) { $save.SnapshotMs } else { $null })
        recorder = $progress
    }
    $meta | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $OutDir "$tag.json") -Encoding UTF8
    $runsMeta += [pscustomobject]$meta

    if ($i -lt $schedule.Count - 1 -and $CooldownSeconds -gt 0) {
        Write-Host "Cooling down for $CooldownSeconds s."
        Start-Sleep -Seconds $CooldownSeconds
    }
}

if ($wasRunning) {
    Write-Host 'Starting Ghostclip again.'
    # Through Explorer, so it runs as the normal user and not elevated like this shell.
    Start-Process -FilePath explorer.exe -ArgumentList "`"$appExe`"" | Out-Null
}

$gpuInfo = Get-CimInstance Win32_VideoController | ForEach-Object { "$($_.Name) ($($_.DriverVersion))" }
$probePath = if ($Recorder -eq 'App') { Join-Path $appData 'probe.json' } else { $ProbeFile }
$probe = if (Test-Path -LiteralPath $probePath) { Get-Content -LiteralPath $probePath -Raw | ConvertFrom-Json } else { $null }
$appConfig = $null
$appVersion = $null
if ($Recorder -eq 'App') {
    $configPath = Join-Path $appData 'config.json'
    if (Test-Path -LiteralPath $configPath) { $appConfig = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json }
    $appVersion = (Get-Item -LiteralPath $appExe).VersionInfo.ProductVersion
}
[ordered]@{
    date       = (Get-Date).ToString('o')
    process    = $exeName
    seconds    = $Seconds
    order      = $Order
    recorder   = $Recorder
    version    = $appVersion
    config     = $appConfig
    cpu        = (Get-CimInstance Win32_Processor | Select-Object -First 1).Name
    gpu        = @($gpuInfo)
    os         = [Environment]::OSVersion.VersionString
    recordArgs = $RecordArgs
    probe      = $probe
    runs       = $runsMeta
} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $OutDir 'meta.json') -Encoding UTF8

& "$PSScriptRoot\summarize.ps1" -Dir $OutDir
