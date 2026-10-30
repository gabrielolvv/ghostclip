using Serilog;
using Velopack;
using Velopack.Sources;

namespace Ghostclip.Host;

/// <summary>
/// Updates from GitHub Releases through Velopack. Checks a couple of minutes
/// after start and then every few hours, downloads in the background, and only
/// applies when the user picks "Restart to update": never in the middle of a game.
/// Velopack checks every package against the SHA-256 in the release manifest.
/// </summary>
internal sealed class UpdateService(ILogger log)
{
    public const string RepositoryUrl = "https://github.com/gabrielolvv/ghostclip";

    private readonly UpdateManager _manager = new(new GithubSource(RepositoryUrl, accessToken: null, prerelease: false));
    private UpdateInfo? _ready;

    /// <summary>False when running from a build folder instead of an installation: nothing to update.</summary>
    public bool IsInstalled => _manager.IsInstalled;

    public string? CurrentVersion => _manager.CurrentVersion?.ToString();

    /// <summary>Raised on a thread-pool thread with the downloaded version.</summary>
    public event Action<string>? UpdateReady;

    public async Task CheckAsync()
    {
        if (!_manager.IsInstalled || _ready is not null) return;
        try
        {
            var info = await _manager.CheckForUpdatesAsync().ConfigureAwait(false);
            if (info is null) return;
            var version = info.TargetFullRelease.Version.ToString();
            log.Information("Update {Version} available; downloading", version);
            await _manager.DownloadUpdatesAsync(info).ConfigureAwait(false);
            _ready = info;
            UpdateReady?.Invoke(version);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidOperationException or TaskCanceledException)
        {
            // Offline or GitHub unreachable: try again at the next check.
            log.Information("Update check failed: {Error}", ex.Message);
        }
    }

    /// <summary>Applies the downloaded update and restarts Ghostclip. Does not return.</summary>
    public void ApplyAndRestart()
    {
        if (_ready is null) return;
        _manager.ApplyUpdatesAndRestart(_ready.TargetFullRelease);
    }
}
