using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Ghostclip.Core;
using Serilog;

namespace Ghostclip.Host;

/// <param name="AudioTitles">Track names saving wrote ("Game", "Microphone", "Discord"); empty for clips with unnamed tracks.</param>
internal sealed record ClipInfo(double Duration, int Height, double Fps, int AudioTracks, string VideoCodec, IReadOnlyList<string> AudioTitles);

/// <summary>
/// Trimming, "compress for Discord", the vertical copy and joining. All run at below-normal
/// priority, so a game in the foreground keeps the CPU.
/// </summary>
internal sealed class ClipTools(GhostclipPaths paths, Func<GhostclipConfig> config, ILogger log)
{
    public async Task<ClipInfo> ProbeAsync(string clip, CancellationToken ct = default)
    {
        var r = await ProcessRunner.RunAsync(Ffprobe(), [
            "-v", "error", "-show_entries", "format=duration:stream=codec_type,codec_name,height,r_frame_rate:stream_tags=handler_name", "-of", "json", clip,
        ], TimeSpan.FromSeconds(20), ProcessPriorityClass.BelowNormal, ct).ConfigureAwait(false);
        if (!r.Succeeded) throw new InvalidOperationException(L($"Could not read the clip: {r.FirstError}", $"Não foi possível ler o clipe: {r.FirstError}", $"No se pudo leer el clip: {r.FirstError}"));

        using var json = JsonDocument.Parse(r.StdOut);
        var duration = double.Parse(json.RootElement.GetProperty("format").GetProperty("duration").GetString()!, CultureInfo.InvariantCulture);
        int height = 0, audio = 0;
        double fps = 0;
        var codec = "";
        var titles = new List<string>();
        foreach (var s in json.RootElement.GetProperty("streams").EnumerateArray())
        {
            var type = s.GetProperty("codec_type").GetString();
            if (type == "audio")
            {
                audio++;
                titles.Add(s.TryGetProperty("tags", out var tags) && tags.TryGetProperty("handler_name", out var handler) ? handler.GetString() ?? "" : "");
            }
            if (type == "video" && height == 0)
            {
                height = s.TryGetProperty("height", out var h) ? h.GetInt32() : 0;
                codec = s.TryGetProperty("codec_name", out var c) ? c.GetString() ?? "" : "";
                var rate = s.TryGetProperty("r_frame_rate", out var f) ? f.GetString() ?? "" : "";
                var parts = rate.Split('/');
                if (parts.Length == 2 && double.TryParse(parts[0], CultureInfo.InvariantCulture, out var n) &&
                    double.TryParse(parts[1], CultureInfo.InvariantCulture, out var d) && d > 0)
                {
                    fps = n / d;
                }
            }
        }
        // MP4 names an unnamed track "SoundHandler": only names saving gave count.
        return new ClipInfo(duration, height, fps, audio, codec,
            titles.Any(t => t is "Game" or "Microphone" or "Discord") ? titles : []);
    }

    /// <summary>Cuts by stream copy into Game_..._trim.mp4. Takes about as long as copying the file.</summary>
    public async Task<string> TrimAsync(string clip, double start, double end, CancellationToken ct = default)
    {
        var output = ClipNaming.DerivedPath(clip, "trim", File.Exists);
        ProcessResult r;
        try
        {
            r = await ProcessRunner.RunAsync(Ffmpeg(), FfmpegArgs.Trim(clip, PartFile.For(output), start, end),
                TimeSpan.FromMinutes(2), ProcessPriorityClass.BelowNormal, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // FFmpeg was stopped (the library closed): nothing half-written stays behind.
            PartFile.Discard(output);
            throw;
        }
        if (!r.Succeeded)
        {
            PartFile.Discard(output);
            throw new InvalidOperationException(L($"Could not trim the clip: {r.FirstError}", $"Não foi possível cortar o clipe: {r.FirstError}", $"No se pudo recortar el clip: {r.FirstError}"));
        }
        PartFile.Commit(output);
        log.Information("Trimmed {Clip} to {Start:0.0}-{End:0.0} s: {Output}", clip, start, end, output);
        return output;
    }

    /// <summary>
    /// Re-encodes into Game_..._discord.mp4 under the size limit, on the hardware
    /// encoder, always as H.264 (Discord doesn't embed HEVC). Retries once,
    /// lower, if the encoder overshoots.
    /// </summary>
    public async Task<(string Path, long Bytes)> CompressAsync(string clip, IReadOnlyList<int>? tracks = null, IReadOnlyList<double>? volumes = null, CancellationToken ct = default)
    {
        var cfg = config();
        var info = await ProbeAsync(clip, ct).ConfigureAwait(false);
        var limit = cfg.DiscordLimitMB;
        var silent = ClipMedia.TracksToKeep(info.AudioTracks, tracks).Count == 0;
        var plan = CompressionPlanner.Plan(info.Duration, info.Height, info.Fps, limit, silent: silent);
        if (plan is null)
        {
            var max = limit * 8.0 * 1024 * 0.92 / (CompressionPlanner.MinVideoKbps + (silent ? 0 : CompressionPlanner.AudioKbps));
            throw new InvalidOperationException(L($"{info.Duration:0} s is too long for {limit} MB. Trim it to {max:0} s or less first.",
                $"{info.Duration:0} s é longo demais para {limit} MB. Corte para {max:0} s ou menos antes.", $"{info.Duration:0} s es demasiado largo para {limit} MB. Recórtalo antes a {max:0} s o menos."));
        }

        var encoder = HardwareEncoder(cfg);
        var output = ClipNaming.DerivedPath(clip, "discord", File.Exists);
        var size = await EncodeAsync(clip, output, plan, encoder, info.AudioTracks, tracks, volumes, ct).ConfigureAwait(false);
        var limitBytes = limit * 1024L * 1024;
        if (size > limitBytes)
        {
            var retry = CompressionPlanner.Plan(info.Duration, info.Height, info.Fps, limit, CompressionPlanner.RetrySafety(size, limit), silent);
            if (retry is not null)
            {
                log.Information("Compressed clip came out at {Size} bytes, over {Limit} MB; retrying at {Kbps} kbps", size, limit, retry.VideoKbps);
                size = await EncodeAsync(clip, output, retry, encoder, info.AudioTracks, tracks, volumes, ct).ConfigureAwait(false);
            }
        }
        PartFile.Commit(output);
        log.Information("Compressed {Clip} to {Output} ({Size} bytes, {Kbps} kbps, {Height}p)", clip, output, size, plan.VideoKbps, plan.Height);
        return (output, size);
    }

    /// <summary>Re-encodes a part of the picture into Game_..._vertical.mp4, on the hardware encoder, always as H.264.</summary>
    public async Task<string> VerticalAsync(string clip, IReadOnlyList<int>? tracks = null, IReadOnlyList<double>? volumes = null, double cropAt = 0.5, CancellationToken ct = default)
    {
        var info = await ProbeAsync(clip, ct).ConfigureAwait(false);
        var encoder = HardwareEncoder(config());
        var output = ClipNaming.DerivedPath(clip, "vertical", File.Exists);
        ProcessResult r;
        try
        {
            r = await ProcessRunner.RunAsync(Ffmpeg(), FfmpegArgs.Vertical(clip, PartFile.For(output), encoder, info.AudioTracks, info.Fps, tracks, volumes, cropAt),
                FfmpegArgs.VerticalTimeout(info.Duration), ProcessPriorityClass.BelowNormal, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            PartFile.Discard(output);
            throw;
        }
        if (!r.Succeeded)
        {
            PartFile.Discard(output);
            throw new InvalidOperationException(L($"Could not make the vertical copy: {r.FirstError}", $"Não foi possível fazer a cópia vertical: {r.FirstError}", $"No se pudo hacer la copia vertical: {r.FirstError}"));
        }
        PartFile.Commit(output);
        log.Information("Vertical copy of {Clip}: {Output}", clip, output);
        return output;
    }

    /// <summary>Saves the frame the player is showing as Game_..._frame.jpg, next to the clip.</summary>
    public async Task<string> FrameAsync(string clip, double at, CancellationToken ct = default)
    {
        var info = await ProbeAsync(clip, ct).ConfigureAwait(false);
        var output = ClipNaming.DerivedPath(clip, "frame", File.Exists, ".jpg");
        ProcessResult r;
        try
        {
            r = await ProcessRunner.RunAsync(Ffmpeg(), FfmpegArgs.Frame(clip, PartFile.For(output), FfmpegArgs.FrameSeek(at, info.Fps)),
                TimeSpan.FromMinutes(2), ProcessPriorityClass.BelowNormal, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            PartFile.Discard(output);
            throw;
        }
        if (!r.Succeeded || !File.Exists(PartFile.For(output)))
        {
            PartFile.Discard(output);
            throw new InvalidOperationException(L($"Could not save the frame: {r.FirstError}", $"Não foi possível salvar o quadro: {r.FirstError}", $"No se pudo guardar el fotograma: {r.FirstError}"));
        }
        PartFile.Commit(output);
        log.Information("Frame of {Clip} at {At:0.000} s: {Output}", clip, at, output);
        return output;
    }

    private static readonly HttpClient Discord = new() { Timeout = TimeSpan.FromMinutes(10) };

    /// <summary>
    /// Posts a clip or picture to the Discord webhook, by way of a Discord size copy when it won't do as it is.
    /// Only when people ask, on each clip. Returns the copy made, or null.
    /// </summary>
    public async Task<string?> SendToDiscordAsync(string clip, IReadOnlyList<int>? tracks, IReadOnlyList<double>? volumes, CancellationToken ct = default)
    {
        var cfg = config();
        if (!DiscordWebhook.IsValid(cfg.DiscordWebhook)) throw new InvalidOperationException(DiscordWebhook.NotAWebhook());
        var limit = cfg.DiscordLimitMB * 1024L * 1024;
        var bytes = new FileInfo(clip).Length;
        string? copy = null;
        if (ClipLibrary.IsImage(clip))
        {
            if (bytes > limit)
            {
                throw new InvalidOperationException(L($"This is over the {cfg.DiscordLimitMB} MB limit for Discord.", $"Isto passa do limite de {cfg.DiscordLimitMB} MB do Discord.", $"Esto supera el límite de {cfg.DiscordLimitMB} MB de Discord."));
            }
        }
        else
        {
            var info = await ProbeAsync(clip, ct).ConfigureAwait(false);
            if (DiscordWebhook.NeedsCopy(bytes, limit, info.AudioTracks, info.VideoCodec, tracks is not null || volumes is not null))
            {
                (copy, _) = await CompressAsync(clip, tracks, volumes, ct).ConfigureAwait(false);
            }
        }
        await DiscordWebhook.SendAsync(Discord, cfg.DiscordWebhook!, copy ?? clip, ct).ConfigureAwait(false);
        log.Information("Sent {Clip} to Discord", copy ?? clip);
        return copy;
    }

    /// <summary>Makes Game_..._gif.gif from the trim marks, up to their last <see cref="FfmpegArgs.GifMaxSeconds"/> seconds.</summary>
    public async Task<(string Output, long Bytes)> GifAsync(string clip, double start, double end, CancellationToken ct = default)
    {
        var info = await ProbeAsync(clip, ct).ConfigureAwait(false);
        var (from, length) = FfmpegArgs.GifRange(start, end, info.Duration);
        var output = ClipNaming.DerivedPath(clip, "gif", File.Exists, ".gif");
        ProcessResult r;
        try
        {
            r = await ProcessRunner.RunAsync(Ffmpeg(), FfmpegArgs.Gif(clip, PartFile.For(output), from, length),
                TimeSpan.FromMinutes(10), ProcessPriorityClass.BelowNormal, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            PartFile.Discard(output);
            throw;
        }
        if (!r.Succeeded)
        {
            PartFile.Discard(output);
            throw new InvalidOperationException(L($"Could not make the GIF: {r.FirstError}", $"Não foi possível fazer o GIF: {r.FirstError}", $"No se pudo hacer el GIF: {r.FirstError}"));
        }
        PartFile.Commit(output);
        var bytes = new FileInfo(output).Length;
        log.Information("GIF of {Clip}: {Output} ({Size} bytes, {Length:0.0} s)", clip, output, bytes, length);
        return (output, bytes);
    }

    /// <summary>Cuts a clip around each of a session's bookmarks, by stream copy. Returns the clips made.</summary>
    public async Task<IReadOnlyList<string>> BookmarkClipsAsync(string session, IReadOnlyList<double> bookmarks, ClipMeta meta, CancellationToken ct = default)
    {
        var game = Path.GetFileName(Path.GetDirectoryName(session));
        var ranges = BookmarkClips.Ranges(bookmarks, meta.Duration, config().ClipSecondsFor(game), meta.Keyframes);
        if (ranges.Count == 0)
        {
            throw new InvalidOperationException(L("This session has no bookmarks to cut around.", "Esta sessão não tem marcadores para cortar.", "Esta sesión no tiene marcadores para cortar."));
        }
        var made = new List<string>();
        foreach (var (start, end) in ranges)
        {
            try
            {
                made.Add(await TrimAsync(session, start, end, ct).ConfigureAwait(false));
            }
            // Those already made are good, and stay: they're in the library, and the message says how far it got.
            catch (InvalidOperationException ex) when (made.Count > 0)
            {
                throw new InvalidOperationException(L($"Saved {made.Count} of {ranges.Count}, then: {ex.Message}", $"Salvos {made.Count} de {ranges.Count}, e então: {ex.Message}", $"Guardados {made.Count} de {ranges.Count}, y luego: {ex.Message}"), ex);
            }
        }
        return made;
    }

    /// <summary>Joins clips by stream copy into Game_..._joined.mp4 next to the oldest, in the order they were saved.</summary>
    public async Task<string> JoinAsync(IReadOnlyList<string> clips, CancellationToken ct = default)
    {
        var ordered = ClipJoiner.InOrder(clips.Where(c => !ClipLibrary.IsImage(c)), File.GetLastWriteTime);
        if (ordered.Count < 2) throw new InvalidOperationException(L("Select at least two clips to join.", "Selecione pelo menos dois clipes para juntar.", "Selecciona al menos dos clips para unir."));

        var sources = new List<JoinSource>();
        foreach (var clip in ordered) sources.Add(await JoinSourceAsync(clip, ct).ConfigureAwait(false));
        if (ClipJoiner.FirstMismatch(sources) is { } other)
        {
            var name = Path.GetFileName(other.Path);
            throw new InvalidOperationException(L($"{name} was recorded with other settings, so it can't join the rest without re-encoding.",
                $"{name} foi gravado com outros ajustes, então não dá para juntar com os demais sem recodificar.", $"{name} se grabó con otros ajustes, así que no se puede unir con los demás sin recodificar."));
        }

        // A join still running on these clips holds the .part: this one takes the next name.
        var output = ClipNaming.DerivedPath(ordered[0], "joined", p => File.Exists(p) || File.Exists(PartFile.For(p)));
        var list = Path.Combine(paths.DataDir, $"join-{Guid.NewGuid():N}.ffconcat");
        ProcessResult r;
        try
        {
            await File.WriteAllTextAsync(list, FfmpegArgs.ConcatList(ordered), ct).ConfigureAwait(false);
            // Stream copy runs at about the speed of the disk.
            r = await ProcessRunner.RunAsync(Ffmpeg(), FfmpegArgs.Join(list, PartFile.For(output)),
                TimeSpan.FromMinutes(30), ProcessPriorityClass.BelowNormal, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            PartFile.Discard(output);
            throw;
        }
        finally
        {
            File.Delete(list);
        }
        if (!r.Succeeded || ClipJoiner.LostAClip(r.StdErr))
        {
            PartFile.Discard(output);
            throw new InvalidOperationException(L($"Could not join the clips: {r.FirstError}", $"Não foi possível juntar os clipes: {r.FirstError}", $"No se pudieron unir los clips: {r.FirstError}"));
        }
        PartFile.Commit(output);
        log.Information("Joined {Count} clips into {Output}", ordered.Count, output);
        return output;
    }

    private async Task<JoinSource> JoinSourceAsync(string clip, CancellationToken ct)
    {
        var r = await ProcessRunner.RunAsync(Ffprobe(), [
            "-v", "error", "-show_data_hash", "md5", "-show_entries", "stream=codec_type,codec_name,width,height,sample_rate,channels,extradata_hash:stream_tags=handler_name", "-of", "json", clip,
        ], TimeSpan.FromSeconds(20), ProcessPriorityClass.BelowNormal, ct).ConfigureAwait(false);
        if (!r.Succeeded) throw new InvalidOperationException(L($"Could not read the clip: {r.FirstError}", $"Não foi possível ler o clipe: {r.FirstError}", $"No se pudo leer el clip: {r.FirstError}"));

        using var json = JsonDocument.Parse(r.StdOut);
        var video = "";
        var audio = new List<string>();
        foreach (var s in json.RootElement.GetProperty("streams").EnumerateArray())
        {
            // Everything the probe said about the track, in one text to compare. The name is in there: the result
            // keeps the first clip's, so a Discord track must not end up called Microphone.
            var format = string.Join(' ', s.EnumerateObject().Where(p => p.Name != "codec_type").Select(p => $"{p.Name}={p.Value}"));
            if (s.GetProperty("codec_type").GetString() == "audio") audio.Add(format);
            else if (video.Length == 0) video = format;
        }
        return new JoinSource(clip, video, audio);
    }

    // Writes the .part file; the caller moves it into place once the size is right.
    private async Task<long> EncodeAsync(string clip, string output, CompressionPlan plan, string encoder, int audioTracks, IReadOnlyList<int>? tracks, IReadOnlyList<double>? volumes, CancellationToken ct)
    {
        ProcessResult r;
        try
        {
            r = await ProcessRunner.RunAsync(Ffmpeg(), FfmpegArgs.Compress(clip, PartFile.For(output), plan, encoder, audioTracks, tracks, volumes),
                TimeSpan.FromMinutes(15), ProcessPriorityClass.BelowNormal, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            PartFile.Discard(output);
            throw;
        }
        if (!r.Succeeded)
        {
            PartFile.Discard(output);
            throw new InvalidOperationException(L($"Could not compress the clip: {r.FirstError}", $"Não foi possível comprimir o clipe: {r.FirstError}", $"No se pudo comprimir el clip: {r.FirstError}"));
        }
        return new FileInfo(PartFile.For(output)).Length;
    }

    // The encoder the recorder probed as working. Never a CPU encoder.
    private string HardwareEncoder(GhostclipConfig cfg)
    {
        ProbeCache? cache = null;
        if (File.Exists(paths.ProbeFile))
        {
            using var stream = File.OpenRead(paths.ProbeFile);
            cache = JsonSerializer.Deserialize(stream, CoreJson.Default.ProbeCache);
        }
        var choice = cache?.Choose(cfg.Encoder, EncoderInput.Auto)
            ?? throw new InvalidOperationException(L("No hardware encoder has been detected yet. Let Ghostclip record for a moment first.",
                "Nenhum encoder de hardware foi detectado ainda. Deixe o Ghostclip gravar por um momento antes.", "Aún no se ha detectado ningún encoder de hardware. Deja que Ghostclip grabe un momento antes."));
        return CaptureSettings.From(cfg with { Codec = VideoCodec.H264 }, choice.Encoder, choice.Input).EncoderName;
    }

    internal string Ffmpeg() => FfmpegLocator.Find(config().FfmpegPath, AppContext.BaseDirectory)
        ?? throw new InvalidOperationException(L("FFmpeg was not found.", "O FFmpeg não foi encontrado.", "No se encontró FFmpeg."));

    internal string Ffprobe()
    {
        var probe = Path.Combine(Path.GetDirectoryName(Ffmpeg())!, "ffprobe.exe");
        return File.Exists(probe) ? probe : throw new InvalidOperationException(L("ffprobe.exe was not found next to FFmpeg.", "O ffprobe.exe não foi encontrado ao lado do FFmpeg.", "No se encontró ffprobe.exe junto a FFmpeg."));
    }
}
