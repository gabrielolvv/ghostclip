# Releasing

## Cutting a release

1. Update the version in `Directory.Build.props`.
2. Add a `## <version>` section to [CHANGELOG.md](../CHANGELOG.md). It becomes
   the GitHub release text and the update's notes; packing fails without it.
3. Commit, then tag and push the tag:

   ```powershell
   git tag v0.4.0
   git push origin v0.4.0
   ```

4. The [Release workflow](../.github/workflows/release.yml) builds the minimal
   FFmpeg, runs the tests, publishes, packs with Velopack and creates the
   GitHub release. Installed copies pick it up within six hours (or two
   minutes after their next start) and offer "Restart to update" in the tray.

## Building locally

```powershell
.\build\ffmpeg\build.ps1              # minimal LGPL FFmpeg into tools\ffmpeg-min (needs Docker)
.\build\release.ps1 -Version 0.4.0    # artifacts\release\Ghostclip-win-Setup.exe and update packages
```

## What ships

| Piece | Why |
|---|---|
| Self-contained .NET 10 (win-x64) | Users don't need .NET installed; installing a runtime would need admin |
| `ffmpeg\ffmpeg.exe`, `ffprobe.exe` | Built by `build/ffmpeg`: LGPL 2.1+, only the components Ghostclip uses, checked by `verify.ps1` |
| `ffmpeg\COPYING.LGPLv2.1`, `README.txt` | The FFmpeg license and where its exact source and build recipe are |

The installer is per-user: it installs to `%LOCALAPPDATA%\Ghostclip\current`
with no admin prompt, and settings, buffer and logs live in
`%LOCALAPPDATA%\GhostclipData`, outside the install folder because Velopack
empties `%LOCALAPPDATA%\Ghostclip` on a fresh install. Uninstalling removes
both.

## Integrity and signing

Velopack checks every downloaded update against the SHA-256 in the release
manifest, fetched over HTTPS from GitHub.

Authenticode signing (which removes the "unknown publisher" warning) goes
through [SignPath](https://signpath.org), free for open-source projects:

1. Apply for the SignPath Foundation program with this repository.
2. In SignPath, create the project `ghostclip` with a signing policy
   `release-signing` and the artifact configuration for a zip of the publish
   folder (sign `*.exe` and `*.dll`).
3. In the GitHub repository settings, add the secret `SIGNPATH_API_TOKEN` and
   the variables `SIGNPATH_ORGANIZATION_ID` and `SIGNPATH_ENABLED=true`.

From then on the workflow signs every binary before packing. Until then
releases are unsigned.
