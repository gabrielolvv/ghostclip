namespace Ghostclip.Host;

/// <summary>Picks the part of a clip to keep. The cut itself is a stream copy.</summary>
internal sealed class TrimForm : Form
{
    private readonly NumericUpDown _start;
    private readonly NumericUpDown _end;

    public TrimForm(string clip, double duration)
    {
        Text = "Trim clip";
        Icon = TrayIcons.Recording;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(12);

        var max = (decimal)Math.Round(duration, 1);
        _start = new NumericUpDown { DecimalPlaces = 1, Increment = 0.5m, Minimum = 0, Maximum = max, Value = 0, Width = 90 };
        _end = new NumericUpDown { DecimalPlaces = 1, Increment = 0.5m, Minimum = 0, Maximum = max, Value = max, Width = 90 };

        var layout = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Fill };
        layout.Controls.Add(new Label
        {
            Text = $"{Path.GetFileName(clip)} ({duration:0.0} s)\n" +
                   "The cut starts at the keyframe at or before the start time (one every 2 s),\nso it may begin up to 2 s earlier.",
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 10),
        });
        layout.SetColumnSpan(layout.Controls[0], 2);
        layout.Controls.Add(new Label { Text = "Start (s)", AutoSize = true, Anchor = AnchorStyles.Left });
        layout.Controls.Add(_start);
        layout.Controls.Add(new Label { Text = "End (s)", AutoSize = true, Anchor = AnchorStyles.Left });
        layout.Controls.Add(_end);

        var ok = new Button { Text = "Save trimmed copy", AutoSize = true, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 10, 0, 0) };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);
        layout.Controls.Add(buttons);
        layout.SetColumnSpan(buttons, 2);
        Controls.Add(layout);
        AcceptButton = ok;
        CancelButton = cancel;

        void Validate() => ok.Enabled = _end.Value - _start.Value >= 0.5m;
        _start.ValueChanged += (_, _) => Validate();
        _end.ValueChanged += (_, _) => Validate();
    }

    public double StartSeconds => (double)_start.Value;

    public double EndSeconds => (double)_end.Value;
}
