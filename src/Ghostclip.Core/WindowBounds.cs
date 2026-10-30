using System.Drawing;
using System.Text.Json;

namespace Ghostclip.Core;

/// <summary>Where the library window was when it last closed, in Data\library-window.json.</summary>
public sealed record WindowBounds(int X, int Y, int Width, int Height, bool Maximized)
{
    public const int MinWidth = 900;
    public const int MinHeight = 600;

    /// <summary>
    /// Whether the window can come back here: at least its minimum size, and its
    /// title bar on a screen by at least 100 px, so it can still be dragged.
    /// </summary>
    public bool FitsOn(IEnumerable<Rectangle> workingAreas)
    {
        if (Width < MinWidth || Height < MinHeight) return false;
        var titleBar = new Rectangle(X, Y, Width, 40);
        return workingAreas.Any(area =>
        {
            var visible = Rectangle.Intersect(titleBar, area);
            return visible.Width >= 100 && visible.Height >= 20;
        });
    }

    public static WindowBounds? Read(string path)
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize(File.ReadAllText(path), LibraryJson.Default.WindowBounds) : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Write(string path)
    {
        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(this, LibraryJson.Default.WindowBounds));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Opening centered next time is fine.
        }
    }
}
