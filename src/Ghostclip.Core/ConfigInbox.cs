using System.Threading.Channels;

namespace Ghostclip.Core;

/// <summary>
/// The configs the tray sends, in the order they were saved, for one reader: the recorder loop.
/// Its read stays open across recorder runs. A channel answers reads in the order they were made,
/// so a read left behind by a run whose FFmpeg exited would take the next config to nobody.
/// </summary>
public sealed class ConfigInbox
{
    private readonly Channel<GhostclipConfig> _configs = Channel.CreateUnbounded<GhostclipConfig>();
    private Task<GhostclipConfig>? _read;

    public void Post(GhostclipConfig config) => _configs.Writer.TryWrite(config);

    /// <summary>Completes when a config is waiting. It stays waiting, for this call or a later one, until <see cref="TakeAsync"/>.</summary>
    public Task<GhostclipConfig> NextAsync(CancellationToken ct) => _read ??= _configs.Reader.ReadAsync(ct).AsTask();

    /// <summary>The next config, gone from the inbox once this returns.</summary>
    public async Task<GhostclipConfig> TakeAsync(CancellationToken ct)
    {
        var config = await NextAsync(ct).ConfigureAwait(false);
        _read = null;
        return config;
    }
}
