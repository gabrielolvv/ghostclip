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
                using (var dialog = new FolderBrowserDialog { Description = "Where Ghostclip saves clips", UseDescriptionForTitle = true, ShowNewFolderButton = true })
                {
                    var folder = current().ResolvedClipsFolder;
                    if (Directory.Exists(folder)) dialog.InitialDirectory = folder;
                    if (dialog.ShowDialog(window) == DialogResult.OK) window.Post(new LibraryEvent("picked", Op: "clipsFolder", Path: dialog.SelectedPath));
                }
                break;
            case "pickFfmpeg":
                using (var dialog = new OpenFileDialog { Title = "Choose ffmpeg.exe", Filter = "FFmpeg|ffmpeg.exe|Programs|*.exe", CheckFileExists = true })
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
        window.Post(new LibraryEvent("settings", Config: config, Encoders: DetectedEncoders(), HotkeyLabel: KeyLabels.Describe(config.Hotkey),
            Games: games.Concat(config.Profiles.Select(p => p.Game)).Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase).ToList(),
            Status: status.Current));
    }

    private void Save(LibraryRequest r)
    {
        var config = (r.Config ?? throw new InvalidOperationException("No settings to save.")).Normalize();
        if (!HotkeyWindow.TryParse(config.Hotkey, out _, out _, out var hotkeyError))
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
                Message: $"Saving restarts recording and clears the last {before!.MaxClipSeconds} s of replay."));
            return;
        }
        ConfigStore.Save(paths.ConfigFile, config);
        saved(config);
        window.Post(new LibraryEvent("done", Op: r.Type, Config: config, HotkeyLabel: KeyLabels.Describe(config.Hotkey)));
    }

    private static string Unreadable(string? error) =>
        $"config.json can't be read ({error}). Fix the file, or delete it to start from the defaults; nothing is saved from here until then.";

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
