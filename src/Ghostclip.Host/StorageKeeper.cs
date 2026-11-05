using Ghostclip.Core;
using Serilog;

namespace Ghostclip.Host;

/// <summary>
/// Applies the clips folder's age and space limits (<see cref="StorageLimit"/>): after
/// every save, at start and when a limit changes. By age they go to the Recycle Bin;
/// by space they're deleted for good, since the Recycle Bin would free nothing.
/// Off the UI thread, one pass at a time.
/// </summary>
internal sealed class StorageKeeper(GhostclipPaths paths, Func<GhostclipConfig> config, Func<bool> inGame, ILogger log)
{
    private int _running;
    private volatile bool _keepOld;

    public void Enforce()
    {
        var cfg = config();
        if (cfg.StorageLimitGB <= 0 && cfg.MaxClipAgeDays <= 0) return;
        // A pass already running sees the newest save as well, or the next one does.
        if (Interlocked.Exchange(ref _running, 1) == 1) return;
        _ = Task.Run(() =>
        {
            try
            {
                Run();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                log.Warning("Could not apply the limits on the clips folder: {Error}", ex.Message);
            }
            finally
            {
                Volatile.Write(ref _running, 0);
            }
        });
    }

    // The folder is scanned first, outside any lock: it can be large or on a slow drive, and the library
    // window waits on the lock to star a clip. From reading favorites to forgetting the deleted, the pass
    // holds the library file's lock, so a star set meanwhile waits for it instead of losing its clip to it.
    // A clip saved after the scan is left for the next pass; the newest is always kept anyway.
    private void Run()
    {
        var cfg = config();
        var root = cfg.ResolvedClipsFolder;
        var clips = ClipLibrary.Scan(root);
        LibraryStore.Locked(() => Apply(cfg, root, clips), paths.LibraryFile);
    }

    // Returns what's left for the space limit to look at.
    private IReadOnlyList<ClipEntry> RecycleOld(GhostclipConfig cfg, string root, IReadOnlyList<ClipEntry> clips, LibraryState state)
    {
        // Windows can ask before destroying a clip it can't recycle, and that can't come up over a game: this waits
        // for the game to close. Told to keep them, it doesn't ask again until Ghostclip next starts.
        if (_keepOld || inGame()) return clips;
        var old = StorageLimit.SelectOlderThan(root, clips, state, cfg.MaxClipAgeDays);
        if (old.Count == 0) return clips;
        if (!Library.RecycleBin.HasOne(root))
        {
            log.Warning("Clips past {Days} days stay: {Folder} has no Recycle Bin to send them to", cfg.MaxClipAgeDays, root);
            return clips;
        }
        var files = old.Select(c => c.Path).ToList();
        var (kept, declined) = Library.RecycleBin.SendFromBackground(files);
        if (declined)
        {
            _keepOld = true;
            log.Information("Told to keep clips past {Days} days: not asking again until the next start", cfg.MaxClipAgeDays);
        }
        var moved = files.Except(kept, StringComparer.OrdinalIgnoreCase).ToList();
        if (moved.Count == 0) return clips;
        log.Information("Clips past {Days} days: sent {Count} to the Recycle Bin", cfg.MaxClipAgeDays, moved.Count);
        LibraryStore.Update(paths.LibraryFile, s =>
        {
            foreach (var path in moved) s.Forget(path);
        });
        var gone = moved.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return [.. clips.Where(c => !gone.Contains(c.Path))];
    }

    private void Apply(GhostclipConfig cfg, string root, IReadOnlyList<ClipEntry> clips)
    {
        // Unknown favorites are not "no favorites": nothing goes until the file reads again.
        if (!LibraryStore.TryRead(paths.LibraryFile, out var state))
        {
            log.Warning("Limits on the clips folder paused: {File} can't be read, so favorites are unknown", paths.LibraryFile);
            return;
        }
        clips = RecycleOld(cfg, root, clips, state);
        var victims = StorageLimit.SelectForDeletion(root, clips, state, cfg.StorageLimitGB * StorageLimit.BytesPerGB);
        if (victims.Count == 0) return;

        var deleted = new List<string>();
        long freed = 0;
        foreach (var clip in victims)
        {
            try
            {
                File.Delete(clip.Path);
                deleted.Add(clip.Path);
                freed += clip.Bytes;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Open in the library's player, or being uploaded: the next pass tries again.
                log.Debug("Could not delete {Clip} yet: {Error}", clip.Path, ex.Message);
            }
        }
        if (deleted.Count == 0) return;
        log.Information("Storage limit of {Limit} GB: deleted the {Count} oldest clips ({MB:0} MB)",
            cfg.StorageLimitGB, deleted.Count, freed / 1024.0 / 1024);
        LibraryStore.Update(paths.LibraryFile, s =>
        {
            foreach (var path in deleted) s.Forget(path);
        });
    }
}
