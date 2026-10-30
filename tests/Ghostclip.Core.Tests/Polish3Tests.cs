using Ghostclip.Core;

namespace Ghostclip.Core.Tests;

public sealed class Polish3Tests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("ghostclip-test-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    // "Ctrl+Oemtilde" is the stored name; people should read the key their keyboard prints.
    [Fact]
    public void Hotkey_label_shows_the_key_as_printed()
    {
        string? Label(string key) => key switch { "Oemtilde" => "´", "Oemcomma" => ",", "NumPad5" => "Num 5", _ => null };

        Assert.Equal("Ctrl+´", HotkeyRules.Label("Ctrl+Oemtilde", Label));
        Assert.Equal("Alt+Shift+,", HotkeyRules.Label("Alt+Shift+Oemcomma", Label));
        Assert.Equal("Win+Num 5", HotkeyRules.Label("Win+NumPad5", Label));
        // Keys without a better name stay as they are.
        Assert.Equal("Alt+F10", HotkeyRules.Label("Alt+F10", Label));
        Assert.Equal("Ctrl+Alt+A", HotkeyRules.Label(" Ctrl + Alt + A ", Label));
    }

    // A thumbnail or meta write that was cut off leaves "<key>.jpg.<guid>.part" or "<key>.json.<pid>.<tid>.tmp".
    [Fact]
    public void Old_temporary_files_in_the_cache_are_removed()
    {
        var now = new DateTime(2026, 10, 30, 21, 0, 0, DateTimeKind.Utc);
        string Make(string name, DateTime written)
        {
            var path = Path.Combine(_dir, name);
            File.WriteAllText(path, "x");
            File.SetLastWriteTimeUtc(path, written);
            return path;
        }
        var oldPart = Make("0123456789abcdef.jpg.3f2a9c1b7e6d4a5f8b0c1d2e3f4a5b6c.part", now.AddDays(-2));
        var oldTmp = Make("0123456789abcdef.json.1234.7.tmp", now.AddDays(-2));
        var freshPart = Make("fedcba9876543210.jpg.aaaabbbbccccddddeeeeffff00001111.part", now.AddMinutes(-1));
        var unrelated = Make("notes.tmp", now.AddDays(-30));

        Assert.Equal(2, ClipMedia.PruneCache(_dir, TimeSpan.FromDays(90), now));

        Assert.False(File.Exists(oldPart));
        Assert.False(File.Exists(oldTmp));
        Assert.True(File.Exists(freshPart));
        Assert.True(File.Exists(unrelated));
    }
}
