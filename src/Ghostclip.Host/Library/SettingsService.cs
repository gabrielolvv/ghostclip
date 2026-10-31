using System.Text.Json;
using Ghostclip.Core;
using Serilog;

namespace Ghostclip.Host.Library;

/// <summary>
/// The settings page's requests: read the settings, save them, pick a folder
/// or FFmpeg. Saving only writes config.json; the tray applies it from there.
/// </summary>
internal sealed class SettingsService(LibraryWindow window, GhostclipPaths paths, Func<GhostclipConfig> current,
    Func<string> clipsRoot, Action<GhostclipConfig> saved, StatusWatcher status)
{
    public static bool Handles(string type) => type is "settings" or "saveSettings" or "pickFolder" or "pickFfmpeg";

    public async Task HandleAsync(LibraryRequest r)
    {
        switch (r.Type)
        {
            case "settings":
                await SendSettingsAsync().ConfigureAwait(true);
                break;
            case "saveSettings":
                Save(r);
                break;
            case "pickFolder":
                using (var dialog = new FolderBrowserDialog { Description = L("Where Ghostclip saves clips", "Onde o Ghostclip salva os clipes"), UseDescriptionForTitle = true, ShowNewFolderButton = true })
                {
                    var folder = current().ResolvedClipsFolder;
                    if (Directory.Exists(folder)) dialog.InitialDirectory = folder;
                    if (dialog.ShowDialog(window) == DialogResult.OK) window.Post(new LibraryEvent("picked", Op: "clipsFolder", Path: dialog.SelectedPath));
                }
                break;
            case "pickFfmpeg":
                using (var dialog = new OpenFileDialog
                {
                    Title = L("Choose ffmpeg.exe", "Escolha o ffmpeg.exe"),
                    Filter = L("FFmpeg|ffmpeg.exe|Programs|*.exe", "FFmpeg|ffmpeg.exe|Programas|*.exe"),
                    CheckFileExists = true,
                })
                {
                    if (dialog.ShowDialog(window) == DialogResult.OK) window.Post(new LibraryEvent("picked", Op: "ffmpegPath", Path: dialog.FileName));
                }
                break;
        }
    }

    private async Task SendSettingsAsync()
    {
        // The file as it is now (a hand edit, the tray's defaults), not what this window started with.
        // One that can't be read is shown as such: showing defaults instead and saving them would
        // overwrite every setting in it.
        if (!ConfigStore.TryRead(paths.ConfigFile, out var read, out var error))
        {
            window.Post(new LibraryEvent("error", Op: "settings", Message: Unreadable(error)));
            return;
        }
        var config = read!;
        saved(config);
        var root = clipsRoot();
        var games = await Task.Run(() => ClipLibrary.Scan(root).Select(c => c.Game)).ConfigureAwait(true);
        window.Post(new LibraryEvent("settings", Config: config, Encoders: DetectedEncoders(), HotkeyLabels: HotkeyLabels(config),
            Games: games.Concat(config.Profiles.Select(p => p.Game)).Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase).ToList(),
            Status: status.Current));
    }

    private void Save(LibraryRequest r)
    {
        var raw = r.Config ?? throw new InvalidOperationException(L("No settings to save.", "Nenhuma configuração para salvar."));
        var config = raw.Normalize();
        // The hotkeys as typed: normalizing would quietly swap a common shortcut for the default.
        if ((HotkeyProblem(raw) ?? HotkeyProblem(config)) is { } hotkeyError)
        {
            window.Post(new LibraryEvent("error", Op: r.Type, Message: hotkeyError));
            return;
        }
        // What the tray is recording with is what the file says now, not what this window opened with.
        if (!ConfigStore.TryRead(paths.ConfigFile, out var before, out var error))
        {
            window.Post(new LibraryEvent("error", Op: r.Type, Message: Unreadable(error)));
            return;
        }
        if (!r.Confirmed && !config.SameCaptureAs(before!))
        {
            // Segments in two formats can't be joined: the buffer starts over.
            window.Post(new LibraryEvent("confirm", Op: r.Type,
                Message: L($"Saving restarts recording and clears the last {before!.MaxClipSeconds} s of replay.",
                    $"Salvar reinicia a gravação e apaga os últimos {before!.MaxClipSeconds} s de replay.")));
            return;
        }
        ConfigStore.Save(paths.ConfigFile, config);
        saved(config);
        window.Post(new LibraryEvent("done", Op: r.Type, Config: config, HotkeyLabels: HotkeyLabels(config)));
    }

    // Keyed like the config fields the page edits.
    private static Dictionary<string, string> HotkeyLabels(GhostclipConfig config) => new()
    {
        ["hotkey"] = KeyLabels.Describe(config.Hotkey),
        ["screenshotHotkey"] = KeyLabels.Describe(config.ScreenshotHotkey),
        ["sessionHotkey"] = KeyLabels.Describe(config.SessionHotkey),
    };

    // Each hotkey must be one Windows can register, and two actions can't share one: only the first would ever run.
    private static string? HotkeyProblem(GhostclipConfig config)
    {
        foreach (var gesture in config.Hotkeys)
        {
            if (!HotkeyWindow.TryParse(gesture, out _, out _, out var error)) return error;
        }
        var keys = config.Hotkeys.Select(g => HotkeyWindow.TryParse(g, out var m, out var k, out _) ? (m, k) : default).ToList();
        if (keys.Distinct().Count() < keys.Count)
        {
            return L("Saving a clip, taking a screenshot and recording a session each need a hotkey of their own.",
                "Salvar clipe, tirar print e gravar sessão precisam de uma hotkey diferente cada.");
        }
        return null;
    }

    private static string Unreadable(string? error) => L(
        $"config.json can't be read ({error}). Fix the file, or delete it to start from the defaults; nothing is saved from here until then.",
        $"Não foi possível ler o config.json ({error}). Corrija o arquivo ou apague-o para começar dos padrões; nada é salvo daqui até lá.");

    // What the recorder measured working on this PC, for marking the encoder choices.
    private IReadOnlyList<string> DetectedEncoders()
    {
        try
        {
            if (!File.Exists(paths.ProbeFile)) return [];
            using var stream = File.OpenRead(paths.ProbeFile);
            var cache = JsonSerializer.Deserialize(stream, CoreJson.Default.ProbeCache);
            return cache?.Results.Where(x => x.Ok).Select(x => x.Encoder.ToString()).Distinct().ToList() ?? [];
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
