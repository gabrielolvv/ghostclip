using System.Diagnostics;
using Ghostclip.Core;
using Serilog;

namespace Ghostclip.Host;

/// <summary>
/// The screenshot hotkey: one frame of the recorded monitor, saved as
/// &lt;Game&gt;\&lt;Game&gt;_YYYY-MM-DD_HH-MM-SS.jpg next to the clips, so the library
/// lists it with them. About half a second, and nothing touches the game.
/// </summary>
internal sealed class ScreenshotTaker(Func<GhostclipConfig> config, ILogger log)
{
    public async Task<string> TakeAsync(string game, CancellationToken ct = default)
    {
        var cfg = config();
        var ffmpeg = FfmpegLocator.Find(cfg.FfmpegPath, AppContext.BaseDirectory)
            ?? throw new InvalidOperationException(L("FFmpeg was not found.", "O FFmpeg não foi encontrado."));
        var output = ClipNaming.BuildPath(cfg.ResolvedClipsFolder, game, DateTime.Now, File.Exists, ".jpg");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        // Normal priority: the moment is now, and it's one frame.
        var r = await ProcessRunner.RunAsync(ffmpeg, FfmpegArgs.Screenshot(cfg.Monitor, PartFile.For(output)),
            TimeSpan.FromSeconds(10), ProcessPriorityClass.Normal, ct).ConfigureAwait(false);
        if (!r.Succeeded || !File.Exists(PartFile.For(output)))
        {
            PartFile.Discard(output);
            throw new InvalidOperationException(L($"Could not take the screenshot: {(r.TimedOut ? "timed out" : r.FirstError)}",
                $"Não foi possível tirar o print: {(r.TimedOut ? "tempo esgotado" : r.FirstError)}"));
        }
        PartFile.Commit(output);
        log.Information("Screenshot saved: {Path} ({Ms} ms)", output, (long)r.Elapsed.TotalMilliseconds);
        return output;
    }
}
