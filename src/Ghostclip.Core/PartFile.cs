namespace Ghostclip.Core;

/// <summary>
/// Clips are written as "&lt;name&gt;.part" and moved to their name once complete,
/// so nothing (the library, Explorer, Discord) ever opens a half-written MP4.
/// </summary>
public static class PartFile
{
    public static string For(string output) => output + ".part";

    /// <summary>Moves the finished part file to <paramref name="output"/>. Never replaces an existing file.</summary>
    public static void Commit(string output) => File.Move(For(output), output, overwrite: false);

    public static void Discard(string output)
    {
        try
        {
            File.Delete(For(output));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
