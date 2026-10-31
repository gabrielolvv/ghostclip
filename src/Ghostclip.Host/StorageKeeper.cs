using Ghostclip.Core;
using Serilog;

namespace Ghostclip.Host;

/// <summary>
/// Applies the clips folder's space limit (<see cref="StorageLimit"/>): after
/// every save, at start and when the limit changes. Deleted for good, since the
/// Recycle Bin would free nothing. Off the UI thread, one pass at a time.
/// </summary>
internal sealed class StorageKeeper(GhostclipPaths paths, Func<GhostclipConfig> config, ILogger log)
{
    private int _running;

    public void Enforce()
    {
        if (config().StorageLimitGB <= 0) return;
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
                log.Warning("Could not apply the storage limit: {Error}", ex.Message);
            }
            finally
            {
                Volatile.Write(ref _running, 0);
            }
        });
    }

    // Under the library file's lock from reading favorites to forgetting the deleted: a star set while
    // the pass runs waits for it instead of protecting a clip already chosen for deletion.
    private void Run() => LibraryStore.Locked(RunLocked, paths.LibraryFile);

    private void RunLocked()
    {
        var cfg = config();
        var root = cfg.ResolvedClipsFolder;
        // Unknown favorites are not "no favorites": nothing goes until the file reads again.
        if (!LibraryStore.TryRead(paths.LibraryFile, out var state))
        {
            log.Warning("Storage limit paused: {File} can't be read, so favorites are unknown", paths.LibraryFile);
            return;
        }
        var clips = ClipLibrary.Scan(root);
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
