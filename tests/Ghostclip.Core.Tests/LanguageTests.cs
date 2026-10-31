using System.Globalization;
using Ghostclip.Core;

namespace Ghostclip.Core.Tests;

// Only UiText.Resolve here: UiText.Use changes the language for the whole process, and tests run in parallel.
public sealed class LanguageTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("ghostclip-test-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Theory]
    [InlineData("pt-BR", "pt")]
    [InlineData("pt-PT", "pt")]
    [InlineData("en-US", "en")]
    [InlineData("fr-FR", "en")]
    [InlineData("", "en")]
    public void Auto_follows_windows_and_falls_back_to_english(string culture, string code)
    {
        Assert.Equal(code, UiText.Resolve(UiLanguage.Auto, CultureInfo.GetCultureInfo(culture)));
    }

    [Fact]
    public void A_chosen_language_wins_over_windows()
    {
        Assert.Equal("en", UiText.Resolve(UiLanguage.English, CultureInfo.GetCultureInfo("pt-BR")));
        Assert.Equal("pt", UiText.Resolve(UiLanguage.Portuguese, CultureInfo.GetCultureInfo("en-US")));
    }

    [Fact]
    public void Language_is_saved_as_text_and_defaults_to_auto()
    {
        var path = Path.Combine(_dir, "config.json");
        Assert.Equal(UiLanguage.Auto, new GhostclipConfig().Language);
        ConfigStore.Save(path, new GhostclipConfig { Language = UiLanguage.Portuguese });
        Assert.Contains("\"language\": \"Portuguese\"", File.ReadAllText(path));
        Assert.Equal(UiLanguage.Portuguese, ConfigStore.Load(path, out _).Language);

        // A config.json from before this setting existed.
        File.WriteAllText(path, "{ \"clipSeconds\": 30 }");
        Assert.Equal(UiLanguage.Auto, ConfigStore.Load(path, out _).Language);
    }

    [Fact]
    public void Changing_the_language_never_restarts_recording()
    {
        var a = new GhostclipConfig();
        Assert.True(a.SameCaptureAs(a with { Language = UiLanguage.Portuguese }));
    }
}
