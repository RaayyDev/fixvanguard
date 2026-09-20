using System.ComponentModel;

namespace FixVanguard;

internal sealed class DarkButton : Button
{
    private bool _hover;
    private bool _pressed;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color FillColor { get; set; } = Theme.Card;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color HoverColor { get; set; } = Theme.CardHover;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color PressedColor { get; set; } = Color.FromArgb(28, 22, 40);

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color BorderColor { get; set; } = Theme.Line;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color HoverBorderColor { get; set; } = Theme.Accent;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color TextColor { get; set; } = Theme.Text;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Glyph { get; set; } = string.Empty;

    public DarkButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        FlatAppearance.MouseOverBackColor = Color.Transparent;
        FlatAppearance.MouseDownBackColor = Color.Transparent;
        BackColor = Color.Transparent;
        ForeColor = Theme.Text;
        Cursor = Cursors.Hand;
        Font = Theme.ButtonFont;
        Size = new Size(220, 44);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hover = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = false;
        _pressed = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        _pressed = true;
        Invalidate();
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _pressed = false;
        Invalidate();
        base.OnMouseUp(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Background);

        var fill = !Enabled
            ? Color.FromArgb(30, 24, 42)
            : _pressed ? PressedColor : _hover ? HoverColor : FillColor;
        var border = !Enabled
            ? Theme.Line
            : _hover || _pressed ? HoverBorderColor : BorderColor;

        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
        using var brush = new SolidBrush(fill);
        using var pen = new Pen(border, 1f);
        g.FillRectangle(brush, bounds);
        g.DrawRectangle(pen, bounds);

        if (_hover && Enabled)
        {
            using var accent = new SolidBrush(Theme.Accent);
            g.FillRectangle(accent, 0, 0, 3, Height);
        }

        var textColor = Enabled ? TextColor : Theme.Muted;
        if (!string.IsNullOrEmpty(Glyph))
        {
            TextRenderer.DrawText(g, Glyph, Glyphs.Medium, new Rectangle(16, 0, 28, Height), textColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(g, Text, Font, new Rectangle(46, 0, Width - 60, Height), textColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            return;
        }

        TextRenderer.DrawText(
            g,
            Text,
            Font,
            ClientRectangle,
            textColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}
