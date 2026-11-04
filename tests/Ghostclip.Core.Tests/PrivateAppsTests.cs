using System.Text.Json;
using Ghostclip.Core;

namespace Ghostclip.Core.Tests;

// Programs that pause recording by themselves while they are in front: a password manager, a bank's app.
public class PrivateAppsTests
{
    private static GhostclipConfig With(params string[] apps) => new GhostclipConfig { PrivateApps = new(apps) }.Normalize();

    [Fact]
    public void There_are_none_by_default()
    {
        var config = new GhostclipConfig().Normalize();
        Assert.Empty(config.PrivateApps);
        Assert.False(config.IsPrivateApp("KeePass"));
    }

    [Theory]
    [InlineData("KeePass")]
    [InlineData("keepass")]
    [InlineData("KEEPASS.EXE")]
    public void A_program_matches_by_name_however_it_is_written(string process)
    {
        Assert.True(With("KeePass").IsPrivateApp(process));
    }

    [Theory]
    [InlineData("KeePassXC")]
    [InlineData("Pass")]
    [InlineData("")]
    [InlineData(null)]
    public void Only_the_whole_name_matches(string? process)
    {
        Assert.False(With("KeePass").IsPrivateApp(process));
    }

    [Fact]
    public void The_list_is_cleaned_up()
    {
        Assert.Equal(["KeePass", "Bitwarden"], With(" KeePass.exe ", "", "  ", "keepass", "Bitwarden.EXE").PrivateApps);
    }

    [Fact]
    public void A_config_from_before_it_has_an_empty_list()
    {
        Assert.Empty(JsonSerializer.Deserialize("""{"hotkey":"Alt+F10"}""", CoreJson.Default.GhostclipConfig)!.Normalize().PrivateApps);
    }

    [Fact]
    public void A_null_list_in_a_hand_edited_file_is_an_empty_one()
    {
        Assert.Empty(JsonSerializer.Deserialize("""{"privateApps":null}""", CoreJson.Default.GhostclipConfig)!.Normalize().PrivateApps);
    }

    [Fact]
    public void It_is_saved_with_the_settings()
    {
        var json = JsonSerializer.Serialize(With("KeePass"), CoreJson.Default.GhostclipConfig);
        Assert.Equal(["KeePass"], JsonSerializer.Deserialize(json, CoreJson.Default.GhostclipConfig)!.PrivateApps);
    }

    [Fact]
    public void Two_configs_with_the_same_list_are_equal()
    {
        Assert.Equal(With("KeePass").PrivateApps, With("KeePass").PrivateApps);
    }

    [Fact]
    public void A_program_in_front_pauses_by_its_process_or_its_own_name()
    {
        var config = With("KeePass", "Secret Vault");
        Assert.Equal("KeePass", config.PausingApp(null, "KeePass", "KeePass Password Safe"));
        Assert.Equal("Secret Vault", config.PausingApp(null, "vault64", "Secret Vault"));
        Assert.Null(config.PausingApp(null, "chrome", "Google Chrome"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("explorer")]
    [InlineData("EXPLORER")]
    [InlineData("Ghostclip")]
    public void The_taskbar_or_the_tray_menu_taking_the_focus_changes_nothing(string? process)
    {
        Assert.Equal("KeePass", With("KeePass").PausingApp("KeePass", process, null));
    }

    [Fact]
    public void Another_program_in_front_goes_on()
    {
        Assert.Null(With("KeePass").PausingApp("KeePass", "chrome", "Google Chrome"));
    }

    [Fact]
    public void The_taskbar_doesn_t_start_a_pause_by_itself()
    {
        Assert.Null(With("KeePass").PausingApp(null, "explorer", "Windows Explorer"));
    }

    [Fact]
    public void Taking_the_program_off_the_list_goes_on_even_from_the_taskbar()
    {
        Assert.Null(new GhostclipConfig().Normalize().PausingApp("KeePass", "explorer", null));
    }

    [Fact]
    public void Changing_it_keeps_recording()
    {
        var a = new GhostclipConfig();
        Assert.True(a.SameCaptureAs(a with { PrivateApps = new(["KeePass"]) }));
    }
}
