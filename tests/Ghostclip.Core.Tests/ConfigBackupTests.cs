using Ghostclip.Core;

namespace Ghostclip.Core.Tests;

// Settings in a file of their own, to take to another PC or keep through a reinstall.
public class ConfigBackupTests
{
    // Made up, in the form Discord uses.
    private const string Hook = "https://discord.com/api/webhooks/123456789012345678/abc-DEF_123";

    private static GhostclipConfig Import(string json, GhostclipConfig? current = null, Func<string, bool>? exists = null) =>
        ConfigBackup.Import(json, current ?? new GhostclipConfig(), exists ?? (_ => true));

    [Fact]
    public void What_goes_out_comes_back()
    {
        var config = new GhostclipConfig
        {
            Hotkey = "Ctrl+F10",
            ClipSeconds = 90,
            StorageLimitGB = 50,
            MaxClipAgeDays = 30,
            Language = UiLanguage.Spanish,
            PrivateApps = new AppNameList(["keepass"]),
            Profiles = [new GameProfile { Game = "Quake", ClipSeconds = 30, AutoSession = true }],
        };
        var back = Import(ConfigBackup.Export(config));
        Assert.Equal("Ctrl+F10", back.Hotkey);
        Assert.Equal(90, back.ClipSeconds);
        Assert.Equal(50, back.StorageLimitGB);
        Assert.Equal(30, back.MaxClipAgeDays);
        Assert.Equal(UiLanguage.Spanish, back.Language);
        Assert.Equal(config.PrivateApps, back.PrivateApps);
        Assert.Equal(config.Profiles, back.Profiles);
    }

    [Fact]
    public void The_webhook_never_goes_in_the_file()
    {
        var file = ConfigBackup.Export(new GhostclipConfig { DiscordWebhook = Hook });
        Assert.DoesNotContain("webhooks", file);
        Assert.DoesNotContain("abc-DEF_123", file);
    }

    [Fact]
    public void Importing_keeps_the_webhook_of_this_PC()
    {
        var back = Import(ConfigBackup.Export(new GhostclipConfig()), new GhostclipConfig { DiscordWebhook = Hook });
        Assert.Equal(Hook, back.DiscordWebhook);
    }

    [Fact]
    public void A_webhook_written_into_the_file_by_hand_is_ignored()
    {
        var file = """{"ghostclipSettingsBackup":1,"config":{"discordWebhook":"https://discord.com/api/webhooks/1/evil"}}""";
        Assert.Null(Import(file).DiscordWebhook);
    }

    [Fact]
    public void FFmpeg_stays_as_this_PC_has_it()
    {
        var file = ConfigBackup.Export(new GhostclipConfig { FfmpegPath = @"C:\Other\ffmpeg.exe" });
        Assert.DoesNotContain("Other", file);
        Assert.Equal(@"D:\Mine\ffmpeg.exe", Import(file, new GhostclipConfig { FfmpegPath = @"D:\Mine\ffmpeg.exe" }).FfmpegPath);
    }

    [Fact]
    public void A_folder_this_PC_has_is_taken()
    {
        var file = ConfigBackup.Export(new GhostclipConfig { ClipsFolder = @"E:\Clips", BufferFolder = @"E:\Buffer" });
        var back = Import(file, new GhostclipConfig { ClipsFolder = @"C:\Mine" });
        Assert.Equal(@"E:\Clips", back.ClipsFolder);
        Assert.Equal(@"E:\Buffer", back.BufferFolder);
    }

    [Fact]
    public void A_folder_this_PC_doesnt_have_stays_as_it_is_here()
    {
        var file = ConfigBackup.Export(new GhostclipConfig { ClipsFolder = @"E:\Clips", BufferFolder = @"E:\Buffer" });
        var back = Import(file, new GhostclipConfig { ClipsFolder = @"C:\Mine", BufferFolder = @"C:\Buf" }, _ => false);
        Assert.Equal(@"C:\Mine", back.ClipsFolder);
        Assert.Equal(@"C:\Buf", back.BufferFolder);
    }

    [Fact]
    public void No_folder_in_the_file_means_the_default()
    {
        var back = Import(ConfigBackup.Export(new GhostclipConfig()), new GhostclipConfig { ClipsFolder = @"C:\Mine" });
        Assert.Null(back.ClipsFolder);
    }

    [Theory]
    [InlineData("")]                                                     // Empty.
    [InlineData("not json")]
    [InlineData("[1,2]")]                                                // Not an object.
    [InlineData("{}")]                                                   // Someone else's file.
    [InlineData("""{"hotkey":"Alt+F1"}""")]                              // A config.json, not a backup.
    [InlineData("""{"ghostclipSettingsBackup":"x","config":{}}""")]
    [InlineData("""{"ghostclipSettingsBackup":1}""")]                    // No settings in it.
    [InlineData("""{"ghostclipSettingsBackup":1,"config":[]}""")]
    [InlineData("""{"ghostclipSettingsBackup":1,"config":{"fps":"lots"}}""")]
    public void Anything_else_is_refused(string file)
    {
        Assert.Throws<InvalidOperationException>(() => Import(file));
    }

    [Fact]
    public void A_backup_from_a_newer_version_is_refused()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Import("""{"ghostclipSettingsBackup":2,"config":{}}"""));
        Assert.Contains(new[] { "newer", "mais novo", "más nuevo" }, error.Message.Contains);
    }

    [Fact]
    public void Settings_it_doesnt_know_are_ignored()
    {
        Assert.Equal(45, Import("""{"ghostclipSettingsBackup":1,"config":{"clipSeconds":45,"fromTheFuture":true}}""").ClipSeconds);
    }
}
