using Ghostclip.Core;

namespace Ghostclip.Core.Tests;

public class ClipNamingTests
{
    [Theory]
    [InlineData("Counter-Strike 2", "Counter-Strike 2")]
    [InlineData("  Elden   Ring  ", "Elden Ring")]
    [InlineData("A:B/C*D?E\"F<G>H|I", "A_B_C_D_E_F_G_H_I")]
    [InlineData("Trailing dots...", "Trailing dots")]
    [InlineData("", "Desktop")]
    [InlineData("   ", "Desktop")]
    [InlineData(null, "Desktop")]
    [InlineData("...", "Desktop")]
    [InlineData("CON", "CON_")]
    [InlineData("nul.game", "nul.game_")]
    [InlineData("Console", "Console")]
    public void Makes_game_names_safe(string? input, string expected) =>
        Assert.Equal(expected, ClipNaming.SafeGameName(input));

    [Fact]
    public void Long_names_are_cut()
    {
        var name = ClipNaming.SafeGameName(new string('x', 200));
        Assert.Equal(60, name.Length);
    }

    [Fact]
    public void Builds_folder_and_file_name()
    {
        var path = ClipNaming.BuildPath(@"C:\Clips", "Counter-Strike 2", new DateTime(2026, 10, 29, 21, 5, 9), _ => false);
        Assert.Equal(@"C:\Clips\Counter-Strike 2\Counter-Strike 2_2026-10-29_21-05-09.mp4", path);
    }

    [Fact]
    public void Adds_a_suffix_when_the_name_is_taken()
    {
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            @"C:\Clips\Desktop\Desktop_2026-01-02_03-04-05.mp4",
            @"C:\Clips\Desktop\Desktop_2026-01-02_03-04-05_2.mp4",
        };
        var path = ClipNaming.BuildPath(@"C:\Clips", null, new DateTime(2026, 1, 2, 3, 4, 5), taken.Contains);
        Assert.Equal(@"C:\Clips\Desktop\Desktop_2026-01-02_03-04-05_3.mp4", path);
    }

    [Fact]
    public void Date_format_ignores_the_current_culture()
    {
        var previous = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("ar-SA");
            var path = ClipNaming.BuildPath(@"C:\c", "G", new DateTime(2026, 10, 29, 1, 2, 3), _ => false);
            Assert.EndsWith("G_2026-10-29_01-02-03.mp4", path);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData("cs2", "Counter-Strike 2", null, "Counter-Strike 2")]
    [InlineData("FortniteClient-Win64-Shipping", "Unreal Engine", "", "FortniteClient")]
    [InlineData("eldenring", "", "ELDEN RING™", "ELDEN RING")]
    [InlineData("explorer", "Microsoft® Windows® Operating System", null, "Desktop")]
    [InlineData("MyGame", "Microsoft® Windows® Operating System", "Game", "MyGame")]
    [InlineData("Ghostclip", "Ghostclip", null, "Desktop")]
    [InlineData(null, null, null, "Desktop")]
    public void Resolves_game_name(string? process, string? product, string? description, string expected) =>
        Assert.Equal(expected, GameNameResolver.Resolve(process, product, description));

    [Fact]
    public void Localized_windows_product_name_is_generic()
    {
        const string os = "Sistema Operacional Microsoft® Windows®";
        Assert.Equal("Windows PowerShell", GameNameResolver.Resolve("powershell", os, "Windows PowerShell", os));
        Assert.Equal("notepad", GameNameResolver.Resolve("notepad", os, os, os));
    }
}
