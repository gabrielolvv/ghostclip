<#
.SYNOPSIS
Builds the Ghostclip installer and update packages with Velopack.

.DESCRIPTION
1. Runs the unit tests.
2. Publishes host and worker self-contained for win-x64 into one folder, so
   users don't need .NET installed (installing a runtime would need admin).
3. Adds the minimal LGPL FFmpeg (build\ffmpeg) after checking it has every
   component Ghostclip uses and nothing GPL, plus its license and the notice
   of where its source and build recipe are.
4. Packs with vpk: Ghostclip-win-Setup.exe (per-user, no admin), the full and
   delta .nupkg files and the release manifest that updates check against,
   with this version's section of CHANGELOG.md as the release notes.

Signing: when -SignTemplate is given (or the GHOSTCLIP_SIGN_TEMPLATE
environment variable is set, as the release workflow does for SignPath), vpk
runs it on every binary. Without it the packages are unsigned.

-Stage splits the run for signing services that sign files in between (the
release workflow uses SignPath this way): "publish" stops after step 3,
"pack" starts at step 4 from an existing artifacts\publish.

.EXAMPLE
.\build\release.ps1 -Version 0.4.0
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Version,
    [ValidateSet('all', 'publish', 'pack')][string]$Stage = 'all',
    [string]$FFmpegDir,
    [string]$OutDir,
    [string]$SignTemplate = $env:GHOSTCLIP_SIGN_TEMPLATE,
    [switch]$SkipTests
)
Set-StrictMode -Version 3.0
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
if (-not $FFmpegDir) { $FFmpegDir = Join-Path $root 'tools\ffmpeg-min' }
if (-not $OutDir) { $OutDir = Join-Path $root 'artifacts\release' }
$publish = Join-Path $root 'artifacts\publish'

function Invoke-Step([string]$Name, [scriptblock]$Command) {
    Write-Host "==> $Name"
    & $Command
    if ($LASTEXITCODE -ne 0) { throw "$Name failed ($LASTEXITCODE)" }
}

if ($Stage -ne 'pack') {
if (-not $SkipTests) {
    Invoke-Step 'tests' { dotnet test --project (Join-Path $root 'tests\Ghostclip.Core.Tests') -c Release }
}

$ffmpeg = Join-Path $FFmpegDir 'ffmpeg.exe'
if (-not (Test-Path -LiteralPath $ffmpeg)) { throw "Minimal FFmpeg not found in $FFmpegDir. Run build\ffmpeg\build.ps1 first." }
Invoke-Step 'FFmpeg check' { & (Join-Path $root 'build\ffmpeg\verify.ps1') -FFmpeg $ffmpeg }

if (Test-Path -LiteralPath $publish) { Remove-Item -LiteralPath $publish -Recurse -Force }
$common = @('-c', 'Release', '-r', 'win-x64', '--self-contained', "-p:Version=$Version", '-o', $publish, '-nologo', '-v', 'q')
Invoke-Step 'publish host' { dotnet publish (Join-Path $root 'src\Ghostclip.Host') @common }
Invoke-Step 'publish worker' { dotnet publish (Join-Path $root 'src\Ghostclip.Worker') @common }

$ffOut = Join-Path $publish 'ffmpeg'
New-Item -ItemType Directory -Force -Path $ffOut | Out-Null
Copy-Item -LiteralPath $ffmpeg, (Join-Path $FFmpegDir 'ffprobe.exe'), (Join-Path $FFmpegDir 'COPYING.LGPLv2.1') -Destination $ffOut
Copy-Item -LiteralPath (Join-Path $root 'LICENSE') -Destination $publish
Set-Content -LiteralPath (Join-Path $ffOut 'README.txt') -Encoding UTF8 -Value @(
    'FFmpeg (https://ffmpeg.org), licensed under the LGPL 2.1 or later (COPYING.LGPLv2.1).'
    'Ghostclip runs it as a separate program and does not modify it.'
    'This build: FFmpeg n8.1.3, configured by build/ffmpeg in https://github.com/gabrielolvv/ghostclip'
    '(Dockerfile and configure-flags.txt there rebuild it exactly). FFmpeg source: https://github.com/FFmpeg/FFmpeg/tree/n8.1.3'
)
if ($Stage -eq 'publish') { Write-Host "Published to $publish"; return }
}

if (-not (Test-Path -LiteralPath (Join-Path $publish 'Ghostclip.exe'))) { throw "Nothing published in $publish. Run with -Stage publish first." }

# This version's section of CHANGELOG.md: the GitHub release text and the update notes.
$changelog = @(Get-Content -LiteralPath (Join-Path $root 'CHANGELOG.md') -Encoding UTF8)
$heading = '^## \[?' + [regex]::Escape($Version) + '\]?(\s|$)'
$start = -1
for ($i = 0; $i -lt $changelog.Count; $i++) { if ($changelog[$i] -match $heading) { $start = $i; break } }
if ($start -lt 0) { throw "CHANGELOG.md has no section for $Version." }
$end = $changelog.Count
for ($i = $start + 1; $i -lt $changelog.Count; $i++) { if ($changelog[$i] -match '^## ') { $end = $i; break } }
$notes = Join-Path $root 'artifacts\release-notes.md'
$text = (($changelog[($start + 1)..($end - 1)]) -join "`n").Trim()
[IO.File]::WriteAllText($notes, $text + "`n", (New-Object Text.UTF8Encoding $false))

$pack = @(
    'vpk', 'pack', '--packId', 'Ghostclip', '--packVersion', $Version, '--packDir', $publish,
    '--mainExe', 'Ghostclip.exe', '--packTitle', 'Ghostclip', '--packAuthors', 'gabrielolvv',
    '--icon', (Join-Path $root 'assets\ghostclip.ico'), '--releaseNotes', $notes, '--outputDir', $OutDir
)
if ($SignTemplate) { $pack += @('--signTemplate', $SignTemplate) }
Invoke-Step 'tool restore' { dotnet tool restore }
Invoke-Step 'pack' { dotnet @pack }

Write-Host ''
Get-ChildItem -LiteralPath $OutDir -File | Sort-Object Name | ForEach-Object { '{0,-40} {1,8:0.0} MB' -f $_.Name, ($_.Length / 1MB) }
if (-not $SignTemplate) { Write-Warning 'Packages are not code-signed (no sign template).' }
