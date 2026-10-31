using System.Drawing.Drawing2D;
using System.Media;

namespace Ghostclip.Host;

/// <summary>Tray icons drawn at startup, so the app ships no image files.</summary>
internal static class TrayIcons
{
    public static readonly Icon Recording = Draw(Color.FromArgb(239, 68, 68));
    public static readonly Icon Waiting = Draw(Color.FromArgb(245, 158, 11));
    public static readonly Icon Stopped = Draw(Color.FromArgb(120, 120, 120));

    // A small ghost with a status dot.
    private static Icon Draw(Color status)
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var body = new GraphicsPath();
            body.AddArc(4, 2, 22, 22, 180, 180);
            body.AddLine(26, 13, 26, 28);
            body.AddLine(26, 28, 22, 24);
            body.AddLine(22, 24, 18, 28);
            body.AddLine(18, 28, 15, 24);
            body.AddLine(15, 24, 12, 28);
            body.AddLine(12, 28, 8, 24);
            body.AddLine(8, 24, 4, 28);
            body.CloseFigure();
            using var fill = new SolidBrush(Color.FromArgb(235, 235, 240));
            using var outline = new Pen(Color.FromArgb(40, 40, 50), 1.5f);
            g.FillPath(fill, body);
            g.DrawPath(outline, body);
            using var eyes = new SolidBrush(Color.FromArgb(40, 40, 50));
            g.FillEllipse(eyes, 10, 10, 4, 5);
            g.FillEllipse(eyes, 17, 10, 4, 5);
            using var dot = new SolidBrush(status);
            g.FillEllipse(dot, 20, 20, 11, 11);
            g.DrawEllipse(outline, 20, 20, 11, 11);
        }
        // Three icons for the app's lifetime: their handles are never freed.
        return Icon.FromHandle(bmp.GetHicon());
    }
}

/// <summary>
/// The tray menu in the library's look: dark, rounded corners on Windows 11, a
/// cyan wash on the item under the pointer, and a touch of the desktop showing
/// through. WinForms menus can't take the system's blurred acrylic, so the glass
/// here is the layered window's opacity.
/// </summary>
internal static class TrayMenuStyle
{
    private static readonly Color Panel = Color.FromArgb(20, 23, 28);
    private static readonly Color Text = Color.FromArgb(232, 234, 237);
    private static readonly Color Muted = Color.FromArgb(138, 145, 156);
    private static readonly Color Accent = Color.FromArgb(92, 225, 230);
    private static readonly Color Line = Color.FromArgb(44, 48, 56);

    public static void Apply(ContextMenuStrip menu)
    {
        menu.Renderer = new Renderer();
        menu.BackColor = Panel;
        menu.ForeColor = Text;
        menu.ShowImageMargin = false;
        menu.Padding = new Padding(4, 6, 4, 6);
        menu.Opacity = 0.96;
        menu.HandleCreated += (_, _) =>
        {
            // Windows 10 ignores it and keeps square corners.
            var round = Native.DWMWCP_ROUND;
            _ = Native.DwmSetWindowAttribute(menu.Handle, Native.DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
        };
    }

    private sealed class Renderer() : ToolStripProfessionalRenderer(new Palette())
    {
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) =>
            e.Graphics.Clear(Panel);

        // The rounded window draws its own edge on Windows 11; a square line would show inside it.
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected || !e.Item.Enabled) return;
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new Rectangle(4, 1, e.Item.Width - 8, e.Item.Height - 2);
            using var path = Rounded(r, 6);
            using var wash = new SolidBrush(Color.FromArgb(38, Accent));
            g.FillPath(wash, path);
            using var bar = new SolidBrush(Accent);
            g.FillRectangle(bar, r.Left, r.Top + 6, 2, r.Height - 12);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            // The restart-to-update item in the accent color: it's the one asking for something.
            e.TextColor = !e.Item.Enabled ? Muted : e.Item.Selected || e.Item.Name == UpdateItemName ? Accent : Text;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            var y = e.Item.Height / 2;
            using var pen = new Pen(Line);
            e.Graphics.DrawLine(pen, 10, y, e.Item.Width - 10, y);
        }

        private static GraphicsPath Rounded(Rectangle r, int radius)
        {
            var d = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(r.Left, r.Top, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    /// <summary>The name to give the "Restart to update" item so it stands out.</summary>
    public const string UpdateItemName = "update";

    // What the base renderer still paints itself (check marks, the overflow arrow), in the same colors.
    private sealed class Palette : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Panel;
        public override Color MenuBorder => Line;
        public override Color MenuItemBorder => Color.Transparent;
        public override Color MenuItemSelected => Color.FromArgb(38, Accent);
        public override Color ImageMarginGradientBegin => Panel;
        public override Color ImageMarginGradientMiddle => Panel;
        public override Color ImageMarginGradientEnd => Panel;
        public override Color SeparatorDark => Line;
        public override Color SeparatorLight => Panel;
    }
}

/// <summary>A short two-note chime, synthesized once into a WAV in memory.</summary>
internal sealed class ConfirmationSound : IDisposable
{
    private readonly MemoryStream _wav = Synthesize();
    private readonly SoundPlayer _player;

    public ConfirmationSound()
    {
        _player = new SoundPlayer(_wav);
        _player.Load();
    }

    public void Play() => _player.Play();

    public void Dispose()
    {
        _player.Dispose();
        _wav.Dispose();
    }

    private static MemoryStream Synthesize()
    {
        const int rate = 44100;
        var samples = new short[rate * 140 / 1000];
        for (var i = 0; i < samples.Length; i++)
        {
            var t = i / (double)rate;
            var freq = t < 0.06 ? 880.0 : 1318.5;
            var envelope = Math.Min(1, i / 200.0) * Math.Min(1, (samples.Length - i) / 1500.0);
            samples[i] = (short)(Math.Sin(2 * Math.PI * freq * t) * envelope * 9000);
        }
        var stream = new MemoryStream();
        using (var w = new BinaryWriter(stream, System.Text.Encoding.ASCII, leaveOpen: true))
        {
            var dataBytes = samples.Length * 2;
            w.Write("RIFF"u8);
            w.Write(36 + dataBytes);
            w.Write("WAVEfmt "u8);
            w.Write(16);
            w.Write((short)1);
            w.Write((short)1);
            w.Write(rate);
            w.Write(rate * 2);
            w.Write((short)2);
            w.Write((short)16);
            w.Write("data"u8);
            w.Write(dataBytes);
            foreach (var s in samples) w.Write(s);
        }
        stream.Position = 0;
        return stream;
    }
}
