using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace FixVanguard;

// Scrollbar vertical, morada y fina, sin las flechas nativas ni el estilo Windows.
internal sealed class PurpleScrollBar : Control
{
    private int _min;
    private int _max;
    private int _value;
    private int _large = 1;
    private bool _hover;
    private bool _dragging;
    private int _dragStartY;
    private int _dragStartValue;

    public event EventHandler? Scroll;

    public PurpleScrollBar()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint
               | ControlStyles.UserPaint
               | ControlStyles.OptimizedDoubleBuffer
               | ControlStyles.ResizeRedraw
               | ControlStyles.SupportsTransparentBackColor, true);
        Width = 8;
        Cursor = Cursors.Hand;
        BackColor = Theme.Console;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Minimum
    {
        get => _min;
        set { _min = value; Clamp(); Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Maximum
    {
        get => _max;
        set { _max = value; Clamp(); Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int LargeChange
    {
        get => _large;
        set { _large = Math.Max(1, value); Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Value
    {
        get => _value;
        set
        {
            var clamped = Math.Clamp(value, _min, _max);
            if (clamped == _value) return;
            _value = clamped;
            Scroll?.Invoke(this, EventArgs.Empty);
            Invalidate();
        }
    }

    private void Clamp()
    {
        if (_max < _min) _max = _min;
        _value = Math.Clamp(_value, _min, _max);
    }

    // Devuelve la posicion Y y altura del thumb dentro del canal.
    private (int y, int height) ThumbGeometry()
    {
        var channel = Math.Max(1, Height - 8);
        var range = Math.Max(1, _max - _min + _large);
        var thumbHeight = Math.Max(24, channel * _large / range);
        thumbHeight = Math.Min(thumbHeight, channel);

        int y = 4;
        if (_max > _min)
            y += (int)((double)(_value - _min) / (_max - _min) * (channel - thumbHeight));

        return (y, thumbHeight);
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
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        var (thumbY, thumbH) = ThumbGeometry();
        if (e.Y >= thumbY && e.Y <= thumbY + thumbH)
        {
            _dragging = true;
            _dragStartY = e.Y;
            _dragStartValue = _value;
        }
        else if (_max > _min)
        {
            // Salto directo: centrar el thumb bajo el cursor.
            var channel = Math.Max(1, Height - 8 - thumbH);
            var target = _min + (int)((double)(e.Y - 4 - thumbH / 2.0) / channel * (_max - _min));
            Value = target;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!_dragging || _max == _min) return;
        var (_, thumbH) = ThumbGeometry();
        var channel = Math.Max(1, Height - 8 - thumbH);
        var dy = e.Y - _dragStartY;
        var range = _max - _min;
        Value = _dragStartValue + (int)((double)dy / channel * range);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _dragging = false;
        base.OnMouseUp(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(BackColor);

        // Canal muy sutil, solo si hace falta scroll.
        if (_max > _min)
        {
            using var track = new SolidBrush(Color.FromArgb(28, Theme.Accent));
            g.FillRectangle(track, Width / 2 - 1, 4, 2, Height - 8);
        }

        var (thumbY, thumbH) = ThumbGeometry();
        var thumbColor = _hover || _dragging ? Theme.AccentBright : Theme.Accent;
        using var thumb = new SolidBrush(thumbColor);
        g.FillRectangle(thumb, 1, thumbY, Width - 2, thumbH);
    }
}
