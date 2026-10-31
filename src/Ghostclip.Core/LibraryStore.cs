using System.Text.Json;

namespace Ghostclip.Core;

/// <summary>
/// What the library knows about files beyond the files themselves: which are
/// favorites, and the bookmarks a session recording left in its clip. Keyed by
/// full path; renaming or deleting through the library carries them along. A
/// clip found under another clips folder (the folder moved, or Settings points
/// elsewhere) is still recognized by its game folder and name, but only as a
/// fallback: changes go to the exact entry when there is one, so deleting or
/// renaming a same-named copy never takes the original's star.
/// </summary>
public sealed record LibraryState
{
    public List<string> Favorites { get; set; } = [];

    /// <summary>Seconds from the start of the clip, ascending.</summary>
    public Dictionary<string, List<double>> Bookmarks { get; set; } = [];

    public bool IsFavorite(string path) => Favorites.Any(p => SameClip(p, path));

    public IReadOnlyList<double> BookmarksFor(string path) =>
        (Bookmarks.FirstOrDefault(b => string.Equals(b.Key, path, StringComparison.OrdinalIgnoreCase)).Value
            ?? Bookmarks.FirstOrDefault(b => SameClip(b.Key, path)).Value) ?? [];

    public void SetFavorite(string path, bool favorite)
    {
        // Unstarring a clip known only by its old place takes that entry; otherwise only its own.
        if (favorite || Favorites.Any(p => SamePath(p, path))) Favorites.RemoveAll(p => SamePath(p, path));
        else Favorites.RemoveAll(p => SameClip(p, path));
        if (favorite) Favorites.Add(path);
    }

    private static bool SamePath(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>The same path, or the same game folder and file name under another clips folder.</summary>
    public static bool SameClip(string a, string b) =>
        string.Equals(a, b, StringComparison.OrdinalIgnoreCase) || string.Equals(Tail(a), Tail(b), StringComparison.OrdinalIgnoreCase);

    // Game\Game_2026-10-01_12-00-00.mp4: the part of a clip's path that stays when the clips folder moves.
    private static string Tail(string path)
    {
        var name = Path.GetFileName(path);
        var game = Path.GetFileName(Path.GetDirectoryName(path) ?? "");
        return game.Length == 0 ? path : game + Path.DirectorySeparatorChar + name;
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
        if (favorite) SetFavorite(to, true);
        SetBookmarks(to, bookmarks);
    }

    /// <summary>A file deleted: its own entries go; another clip's, matched only by name, stay.</summary>
    public void Forget(string path)
    {
        Favorites.RemoveAll(p => SamePath(p, path));
        RemoveBookmarks(path);
    }

    private void RemoveBookmarks(string path)
    {
        foreach (var key in Bookmarks.Keys.Where(k => SamePath(k, path)).ToList())
        {
            Bookmarks.Remove(key);
        }
    }
}

/// <summary>Reads and changes Data\library.json. The tray and the library both write it, so changes take a lock across processes.</summary>
public static class LibraryStore
{
    private const string MutexName = @"Local\Ghostclip.LibraryStore";

    /// <summary>The file as it is now, for showing; empty when it's missing or unreadable.</summary>
    public static LibraryState Read(string file) => TryRead(file, out var state) ? state : new LibraryState();

    /// <summary>
    /// The file as it is now. False when it exists but can't be read: its favorites are unknown,
    /// which is not the same as none, so nothing may be deleted or written over on the strength of it.
    /// </summary>
    public static bool TryRead(string file, out LibraryState state)
    {
        state = new LibraryState();
        // Only a file that isn't there means no favorites; File.Exists also says false when it can't tell.
        // A lock (antivirus, the indexer, the other process writing) gets about a second, as writing does.
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var stream = File.OpenRead(file);
                state = JsonSerializer.Deserialize(stream, CoreJson.Default.LibraryState) ?? new LibraryState();
                return true;
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
            {
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && attempt < 20)
            {
                Thread.Sleep(50);
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Runs <paramref name="action"/> holding the lock every change takes, across processes: for a pass that
    /// reads favorites and then acts on them (the space limit), so a star set in between waits for it.
    /// Changes made inside take the lock again, which the same thread may. Throws <see cref="IOException"/>
    /// when another Ghostclip process keeps it longer than five seconds.
    /// </summary>
    public static void Locked(Action action, string? file = null)
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
            var name = file is null ? "library.json" : Path.GetFileName(file);
            if (!owned) throw new IOException(UiText.L($"{name} is busy in another Ghostclip window. Try again.", $"O {name} está ocupado em outra janela do Ghostclip. Tente de novo."));
            action();
        }
        finally
        {
            if (owned) mutex.ReleaseMutex();
        }
    }

    /// <summary>
    /// Reads, applies <paramref name="change"/> and writes back atomically; returns the new state.
    /// Throws <see cref="IOException"/> rather than write over a file it can't read, or write without the lock.
    /// </summary>
    public static LibraryState Update(string file, Action<LibraryState> change)
    {
        LibraryState? result = null;
        Locked(() => result = Write(file, change), file);
        return result!;
    }

    private static LibraryState Write(string file, Action<LibraryState> change)
    {
        if (!TryRead(file, out var state))
        {
            throw new IOException(UiText.L($"{Path.GetFileName(file)}, where favorites are kept, can't be read. Until it can, favorites can't change and the space limit deletes nothing.", $"Não foi possível ler o {Path.GetFileName(file)}, onde ficam os favoritos. Até que seja possível, os favoritos não mudam e o limite de espaço não apaga nada."));
        }
        change(state);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var temp = $"{file}.{Environment.ProcessId}.tmp";
        using (var stream = File.Create(temp))
        {
            JsonSerializer.Serialize(stream, state, CoreJson.Indented.LibraryState);
            // On disk before the rename: a power cut must leave the old file or the new one, never a hole.
            stream.Flush(flushToDisk: true);
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
}
