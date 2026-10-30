using System.Globalization;
using System.Text.Json;

namespace Ghostclip.Core;

/// <summary>One FFmpeg run, from its first segment on.</summary>
/// <param name="AudioLeadMs">How much earlier the audio stream started than the video stream.</param>
/// <param name="AudioTitles">Names of the audio tracks, in order ("Game", "Microphone").</param>
public sealed record RecordingSession(long StartSequence, int AudioLeadMs, string[]? AudioTitles = null);

/// <summary>
/// Audio/video alignment.
///
/// FFmpeg stamps each stream from its own first sample: audio from the moment it
/// starts reading the pipe, video from ddagrab's first frame, which comes later
/// (ddagrab and encoder start-up, ~170 ms measured on an Intel UHD). Left alone,
/// sound would trail the picture by that much. The worker measures the gap on
/// the QueryPerformanceCounter clock and records it per session; the save
/// shifts the audio back by it while remuxing, still by stream copy.
/// </summary>
public static class AudioSync
{
    private const string Prefix = "session_";

    public static string FileName(long startSequence) =>
        string.Create(CultureInfo.InvariantCulture, $"{Prefix}{startSequence:D8}.json");

    /// <summary>
    /// Audio lead from one progress report: time since the audio stream started,
    /// minus the video FFmpeg had produced by then, minus one frame of encoder delay.
    /// </summary>
    public static int MeasureLeadMs(double secondsSinceAudioStart, long framesProduced, int fps)
    {
        var video = (framesProduced + 1) / (double)fps;
        return (int)Math.Round(Math.Clamp(secondsSinceAudioStart - video, -1, 2) * 1000);
    }

    public static void Write(string bufferDir, RecordingSession session)
    {
        var path = Path.Combine(bufferDir, FileName(session.StartSequence));
        File.WriteAllText(path, JsonSerializer.Serialize(session, CoreJson.Default.RecordingSession));
    }

    public static IReadOnlyList<RecordingSession> Scan(string bufferDir)
    {
        if (!Directory.Exists(bufferDir)) return [];
        var sessions = new List<RecordingSession>();
        foreach (var file in Directory.EnumerateFiles(bufferDir, Prefix + "*.json"))
        {
            try
            {
                if (JsonSerializer.Deserialize(File.ReadAllText(file), CoreJson.Default.RecordingSession) is { } s) sessions.Add(s);
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                // A session file being written or damaged: treat it as unknown.
            }
        }
        sessions.Sort((a, b) => a.StartSequence.CompareTo(b.StartSequence));
        return sessions;
    }

    /// <summary>The session a segment belongs to: the newest one that started at or before it.</summary>
    public static RecordingSession? ForSequence(IReadOnlyList<RecordingSession> sessions, long sequence) =>
        sessions.LastOrDefault(s => s.StartSequence <= sequence);

    /// <summary>Sessions no remaining segment belongs to.</summary>
    public static IReadOnlyList<RecordingSession> SelectObsolete(IReadOnlyList<RecordingSession> sessions, long oldestSegment)
    {
        var owner = ForSequence(sessions, oldestSegment);
        return sessions.Where(s => s.StartSequence < oldestSegment && s != owner).ToList();
    }

    public static void Delete(string bufferDir, RecordingSession session)
    {
        try { File.Delete(Path.Combine(bufferDir, FileName(session.StartSequence))); }
        catch (IOException) { }
    }
}
