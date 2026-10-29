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
