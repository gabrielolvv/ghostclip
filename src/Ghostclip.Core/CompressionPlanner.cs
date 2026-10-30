namespace Ghostclip.Core;

/// <param name="Height">Output height; 0 keeps the source's.</param>
/// <param name="Fps">Output frame rate; 0 keeps the source's.</param>
public sealed record CompressionPlan(int VideoKbps, int AudioKbps, int Height, int Fps);

public static class CompressionPlanner
{
    public const int AudioKbps = 128;

    // Hardware encoders overshoot a little and MP4 adds overhead: aim below the limit.
    private const double FirstTry = 0.92;

    /// <summary>Below this, even 360p30 looks like soup: better to trim the clip first.</summary>
    public const int MinVideoKbps = 250;

    /// <summary>
    /// Picks a bitrate that fits <paramref name="limitMB"/>, then the largest
    /// resolution and frame rate that bitrate can carry. Rough targets for H.264:
    /// 1080p60 wants ~6 Mbps, 720p ~2.5 Mbps; under ~2.5 Mbps 30 fps looks better than 60.
    /// </summary>
    /// <param name="safety">Fraction of the limit to aim for; a retry passes a smaller one.</param>
    /// <returns>Null when the clip is too long to fit at any watchable quality.</returns>
    public static CompressionPlan? Plan(double durationSeconds, int sourceHeight, double sourceFps, int limitMB, double safety = FirstTry)
    {
        if (durationSeconds <= 0) return null;
        var totalKbps = limitMB * 8.0 * 1024 * safety / durationSeconds;
        var videoKbps = (int)(totalKbps - AudioKbps);
        if (videoKbps < MinVideoKbps) return null;

        var height = videoKbps switch
        {
            >= 6000 => 0,
            >= 2500 => 720,
            >= 1000 => 480,
            _ => 360,
        };
        if (height >= sourceHeight) height = 0;
        var fps = videoKbps < 2500 && sourceFps > 31 ? 30 : 0;
        return new CompressionPlan(videoKbps, AudioKbps, height, fps);
    }

    /// <summary>Safety factor for the second attempt, after the first came out too big.</summary>
    public static double RetrySafety(long gotBytes, int limitMB) =>
        FirstTry * (limitMB * 1024.0 * 1024 / gotBytes) * 0.95;
}
