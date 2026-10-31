using System.Text.Json;

namespace Ghostclip.Core;

/// <summary>
/// What the library's badge shows about the recorder: REC · QSV · 60 FPS. The
/// tray writes it to status.json only when one of these values changes, so a
/// recording session costs a handful of tiny writes, not one a second.
/// </summary>
/// <param name="ProcessId">The tray's process: a status file left by a crash names a process that is gone.</param>
/// <param name="Encoder">The encoder family (QSV, NVENC, AMF), or null before one is chosen.</param>
/// <param name="Problem">Why the recorder stopped (no hardware encoder, FFmpeg missing), while it is stopped; the library shows it.</param>
public sealed record RecorderStatus(int ProcessId, WorkerState State, string? Encoder, int Fps, bool Audio, string? Problem = null)
{
    /// <param name="captureFps">The configured rate: the measured one wanders and would mean a write every few seconds.</param>
    /// <param name="problem">The recorder's last fatal error; kept only while it is stopped.</param>
    public static RecorderStatus From(int processId, WorkerStatus s, int captureFps, string? problem = null) =>
        new(processId, s.State, Family(s.Encoder), s.State == WorkerState.Recording ? captureFps : 0, s.Audio,
            s.State == WorkerState.Stopped ? problem : null);

    // "h264_qsv (direct)" -> "QSV".
    private static string? Family(string? encoder)
    {
        if (string.IsNullOrEmpty(encoder)) return null;
        var name = encoder.Split(' ')[0];
        var underscore = name.LastIndexOf('_');
        return (underscore >= 0 ? name[(underscore + 1)..] : name).ToUpperInvariant();
    }
}

public static class StatusFile
{
    public static void Write(string path, RecorderStatus status)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(status, LibraryJson.Indented.RecorderStatus));
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>The last status written, or null when there is none or it can't be read.</summary>
    public static RecorderStatus? Read(string path)
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize(File.ReadAllText(path), LibraryJson.Default.RecorderStatus) : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static void Delete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
