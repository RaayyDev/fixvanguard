using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace FixVanguard;

internal sealed class ConsolePanel : Panel
{
    private const int LineHeight = 26;
    private readonly List<ActionLog> _lines = [];
    private readonly PurpleScrollBar _scroll;

    public ConsolePanel()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Console;
        _scroll = new PurpleScrollBar
        {
            Dock = DockStyle.Right,
            Width = 8,
            Visible = false,
            LargeChange = 4
        };
        _scroll.Scroll += (_, _) => Invalidate();
        Controls.Add(_scroll);
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Count => _lines.Count;

    public void Write(ActionLog entry)
    {
        _lines.Add(entry);
        if (_lines.Count > 200)
            _lines.RemoveAt(0);

        UpdateScroll();
        _scroll.Value = Math.Max(_scroll.Minimum, _scroll.Maximum);
        Invalidate();
    }

    public void Write(string message, ActionLevel level = ActionLevel.Info) =>
        Write(new ActionLog(message, level));

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        UpdateScroll();
        Invalidate();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if (!_scroll.Visible)
            return;

        var next = Math.Clamp(_scroll.Value - Math.Sign(e.Delta), _scroll.Minimum, _scroll.Maximum);
        _scroll.Value = next;
        Invalidate();
        base.OnMouseWheel(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Theme.Console);

        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
        using var pen = new Pen(Theme.Line, 1f);
        g.DrawRectangle(pen, bounds);

        using var header = new SolidBrush(Theme.Card);
        g.FillRectangle(header, 1, 1, Width - 2, 32);
        g.DrawLine(pen, 1, 33, Width - 2, 33);

        TextRenderer.DrawText(g, Glyphs.Terminal, Glyphs.Small, new Rectangle(12, 6, 22, 22), Theme.Accent, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        TextRenderer.DrawText(g, "Actividad", Theme.BodyStrongFont, new Rectangle(34, 6, 200, 22), Theme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);

        var viewportTop = 40;
        var viewportHeight = Height - viewportTop - 8;
        var visible = Math.Max(1, viewportHeight / LineHeight);
        var start = _scroll.Visible ? _scroll.Value : 0;
        var textWidth = Width - (_scroll.Visible ? _scroll.Width : 0) - 56;

        for (var i = 0; i < visible && start + i < _lines.Count; i++)
        {
            var entry = _lines[start + i];
            var y = viewportTop + i * LineHeight;
            var color = entry.Level switch
            {
                ActionLevel.Success => Theme.Success,
                ActionLevel.Warning => Theme.Warning,
                ActionLevel.Error => Theme.Danger,
                _ => Theme.Accent
            };

            using var glow = new SolidBrush(Color.FromArgb(50, color));
            using var core = new SolidBrush(color);
            g.FillEllipse(glow, 14, y + 6, 12, 12);
            g.FillEllipse(core, 17, y + 9, 6, 6);

            TextRenderer.DrawText(
                g,
                entry.Time.ToString("HH:mm"),
                Theme.ConsoleTimeFont,
                new Rectangle(32, y, 46, LineHeight),
                Theme.Muted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);

            TextRenderer.DrawText(
                g,
                entry.Message,
                Theme.ConsoleFont,
                new Rectangle(76, y, textWidth, LineHeight),
                Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    private void UpdateScroll()
    {
        var visible = Math.Max(1, (Height - 48) / LineHeight);
        var max = Math.Max(0, _lines.Count - visible);
        _scroll.Minimum = 0;
        _scroll.Maximum = max;
        _scroll.LargeChange = Math.Max(1, visible / 2);
        _scroll.Visible = max > 0;
        if (_scroll.Value > max)
            _scroll.Value = max;
    }
}
