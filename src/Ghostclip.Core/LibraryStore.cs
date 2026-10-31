using System.Text.Json;

namespace Ghostclip.Core;

/// <summary>
/// What the library knows about files beyond the files themselves: which are
/// favorites, and the bookmarks a session recording left in its clip. Keyed by
/// full path; renaming or deleting through the library carries them along.
/// </summary>
public sealed record LibraryState
{
    public List<string> Favorites { get; set; } = [];

    /// <summary>Seconds from the start of the clip, ascending.</summary>
    public Dictionary<string, List<double>> Bookmarks { get; set; } = [];

    public bool IsFavorite(string path) => Favorites.Contains(path, StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<double> BookmarksFor(string path) =>
        Bookmarks.FirstOrDefault(b => string.Equals(b.Key, path, StringComparison.OrdinalIgnoreCase)).Value ?? [];

    public void SetFavorite(string path, bool favorite)
    {
        Favorites.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        if (favorite) Favorites.Add(path);
    }

    public void SetBookmarks(string path, IEnumerable<double> seconds)
    {
        RemoveBookmarks(path);
        var list = seconds.Where(s => s >= 0).Order().ToList();
        if (list.Count > 0) Bookmarks[path] = list;
    }

    /// <summary>A file renamed: whatever was known about it now belongs to the new name.</summary>
    public void Move(string from, string to)
    {
        var favorite = IsFavorite(from);
        var bookmarks = BookmarksFor(from).ToList();
        Forget(from);
        SetFavorite(to, favorite);
        SetBookmarks(to, bookmarks);
    }

    public void Forget(string path)
    {
        SetFavorite(path, false);
        RemoveBookmarks(path);
    }

    private void RemoveBookmarks(string path)
    {
        foreach (var key in Bookmarks.Keys.Where(k => string.Equals(k, path, StringComparison.OrdinalIgnoreCase)).ToList())
        {
            Bookmarks.Remove(key);
        }
    }
}

/// <summary>Reads and changes Data\library.json. The tray and the library both write it, so changes take a lock across processes.</summary>
public static class LibraryStore
{
    private const string MutexName = @"Local\Ghostclip.LibraryStore";

    /// <summary>The file as it is now; empty when it's missing or unreadable (it only holds conveniences).</summary>
    public static LibraryState Read(string file)
    {
        try
        {
            if (!File.Exists(file)) return new LibraryState();
            using var stream = File.OpenRead(file);
            return JsonSerializer.Deserialize(stream, CoreJson.Default.LibraryState) ?? new LibraryState();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new LibraryState();
        }
    }

    /// <summary>Reads, applies <paramref name="change"/> and writes back atomically; returns the new state.</summary>
    public static LibraryState Update(string file, Action<LibraryState> change)
    {
        using var mutex = new Mutex(false, MutexName);
        var owned = false;
        try
        {
            try
            {
                owned = mutex.WaitOne(TimeSpan.FromSeconds(5));
            }
            catch (AbandonedMutexException)
            {
                // The other process died holding it; the file is still whole thanks to the atomic write.
                owned = true;
            }
            var state = Read(file);
            change(state);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            var temp = $"{file}.{Environment.ProcessId}.tmp";
            using (var stream = File.Create(temp))
            {
                JsonSerializer.Serialize(stream, state, CoreJson.Indented.LibraryState);
            }
            // The lock keeps writers apart, not readers: the other process listing clips can hold the file
            // for a moment. Up to about a second, as ConfigStore.Save does.
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    File.Move(temp, file, overwrite: true);
                    return state;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && attempt < 20)
                {
                    Thread.Sleep(50);
                }
                catch
                {
                    try { File.Delete(temp); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                    throw;
                }
            }
        }
        finally
        {
            if (owned) mutex.ReleaseMutex();
        }
    }
}
