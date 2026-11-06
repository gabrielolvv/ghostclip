using System.Diagnostics;
using Ghostclip.Core;

namespace Ghostclip.Host.Library;

/// <summary>
/// Checks, on this PC, what the app does to a clip: it makes a small clip of its own on the GPU encoder, with no screen
/// in it, and runs the real tools on it, in a folder of its own that goes away after. It reads and changes none of
/// people's clips. The recorder goes on as it was.
/// </summary>
internal sealed class SelfTest(GhostclipPaths paths, GhostclipConfig config, ClipTools tools)
{
    /// <param name="report">Called with every step so far as each one ends.</param>
    public async Task<IReadOnlyList<SelfTestStep>> RunAsync(Action<IReadOnlyList<SelfTestStep>> report, CancellationToken ct)
    {
        var steps = new List<SelfTestStep>();
        var folder = Path.Combine(paths.DataDir, $"SelfTest-{Guid.NewGuid():N}");
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        RemoveLeftovers();

        async Task<bool> Step(string name, Func<Task<string?>> run)
        {
            SelfTestStep step;
            try
            {
                step = new SelfTestStep(name, true, SelfTestReport.Clean(await run().ConfigureAwait(true), folder, home));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                step = new SelfTestStep(name, false, SelfTestReport.Clean(ex.Message, folder, home));
            }
            steps.Add(step);
            report(steps);
            return step.Ok;
        }

        try
        {
            Directory.CreateDirectory(folder);
            // Named as Ghostclip names its clips, in a folder as if a game's.
            var clip = Path.Combine(folder, "G_2026-01-01_00-00-00.mp4");

            await Step("FFmpeg", async () =>
            {
                var r = await ProcessRunner.RunAsync(tools.Ffmpeg(), ["-hide_banner", "-version"], TimeSpan.FromSeconds(20), ProcessPriorityClass.BelowNormal, ct).ConfigureAwait(true);
                if (!r.Succeeded) throw new InvalidOperationException(r.FirstError);
                return r.StdOut.Split('\n')[0].Trim();
            }).ConfigureAwait(true);

            var made = await Step(L("GPU encoder", "Encoder da GPU", "Encoder de la GPU"), async () =>
            {
                var encoder = tools.HardwareEncoder(config);
                var r = await ProcessRunner.RunAsync(tools.Ffmpeg(), FfmpegArgs.SelfTestClip(clip, encoder), TimeSpan.FromSeconds(60), ProcessPriorityClass.BelowNormal, ct).ConfigureAwait(true);
                if (!r.Succeeded) throw new InvalidOperationException(r.FirstError);
                return encoder;
            }).ConfigureAwait(true);

            if (made)
            {
                await Step(L("Reading a clip", "Ler um clipe", "Leer un clip"), async () =>
                {
                    var info = await tools.ProbeAsync(clip, ct).ConfigureAwait(true);
                    if (info.AudioTracks != 2 || info.AudioTitles.Count != 2) throw new InvalidOperationException($"{info.AudioTracks} tracks, {info.AudioTitles.Count} named");
                    if (Math.Abs(info.Duration - 3) > 0.5) throw new InvalidOperationException($"{info.Duration:0.0} s");
                    return null;
                }).ConfigureAwait(true);

                string? trim = null;
                await Step(L("Trim", "Corte", "Recorte"), async () =>
                {
                    trim = await tools.TrimAsync(clip, 0, 2, ct).ConfigureAwait(true);
                    return Size(trim);
                }).ConfigureAwait(true);

                await Step(L("Discord size, with volume, noise reduction and fade", "Tamanho do Discord, com volume, redução de ruído e fade", "Tamaño de Discord, con volumen, reducción de ruido y fundido"), async () =>
                    Size((await tools.CompressAsync(clip, null, [1, 0.5], [1], true, ct).ConfigureAwait(true)).Path)).ConfigureAwait(true);

                await Step(L("Vertical copy", "Cópia vertical", "Copia vertical"), async () =>
                    Size(await tools.VerticalAsync(clip, null, null, 0.25, null, false, ct).ConfigureAwait(true))).ConfigureAwait(true);

                await Step("GIF", async () =>
                {
                    var (gif, _) = await tools.GifAsync(clip, 0, 2, ct).ConfigureAwait(true);
                    return Starts(gif, "GIF8"u8.ToArray());
                }).ConfigureAwait(true);

                await Step(L("Save frame", "Salvar quadro", "Guardar fotograma"), async () =>
                    Starts(await tools.FrameAsync(clip, 1, ct).ConfigureAwait(true), [0xFF, 0xD8])).ConfigureAwait(true);

                await Step(L("Audio only", "Só o áudio", "Solo el audio"), async () =>
                    Size(await tools.AudioOnlyAsync(clip, 0, 2, [1], null, null, ct).ConfigureAwait(true))).ConfigureAwait(true);

                if (trim is not null)
                {
                    await Step(L("Join clips", "Juntar clipes", "Unir clips"), async () =>
                        Size(await tools.JoinAsync([clip, trim], ct).ConfigureAwait(true))).ConfigureAwait(true);
                }
            }

            await Step(L("Clips folder", "Pasta de clipes", "Carpeta de clips"), () => Writable(config.ResolvedClipsFolder)).ConfigureAwait(true);
            await Step(L("Replay buffer folder", "Pasta do buffer de replay", "Carpeta del búfer de repetición"), () => Writable(paths.BufferDirFor(config))).ConfigureAwait(true);
        }
        finally
        {
            try
            {
                if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Something still holds it. It has only the made-up clip, and the next test takes it away.
            }
        }
        return steps;
    }

    private void RemoveLeftovers()
    {
        if (!Directory.Exists(paths.DataDir)) return;
        foreach (var old in Directory.EnumerateDirectories(paths.DataDir, "SelfTest-*"))
        {
            try
            {
                Directory.Delete(old, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private static string Size(string file)
    {
        var bytes = new FileInfo(file).Length;
        if (bytes == 0) throw new InvalidOperationException(L("The file came out empty.", "O arquivo saiu vazio.", "El archivo salió vacío."));
        return bytes >= 1024 * 1024 ? $"{bytes / 1024.0 / 1024:0.0} MB" : $"{bytes / 1024} KB";
    }

    private static string Starts(string file, byte[] signature)
    {
        var size = Size(file);
        var head = new byte[signature.Length];
        using (var stream = File.OpenRead(file)) stream.ReadExactly(head);
        if (!head.SequenceEqual(signature)) throw new InvalidOperationException(L("The file isn't what it should be.", "O arquivo não é o que deveria ser.", "El archivo no es lo que debería ser."));
        return size;
    }

    // A real write beside people's clips, under a name of its own, taken away at once.
    private static Task<string?> Writable(string folder)
    {
        Directory.CreateDirectory(folder);
        var probe = Path.Combine(folder, $".ghostclip-test-{Guid.NewGuid():N}.tmp");
        File.WriteAllBytes(probe, [1]);
        File.Delete(probe);
        return Task.FromResult<string?>(null);
    }
}
