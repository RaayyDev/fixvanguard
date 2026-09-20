using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace FixVanguard;

internal sealed class ParticleCanvas : Control
{
    private readonly Particle[] _particles = new Particle[72];
    private readonly System.Windows.Forms.Timer _timer;
    private readonly Random _random = new(19);
    private float _time;

    private string _caption = string.Empty;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Heading { get; set; } = "Fix Vanguard Fragment";

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Caption
    {
        get => _caption;
        set
        {
            _caption = value;
            Invalidate();
        }
    }

    public ParticleCanvas()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Dock = DockStyle.Fill;
        TabStop = false;

        _timer = new System.Windows.Forms.Timer { Interval = 33 };
        _timer.Tick += (_, _) =>
        {
            _time += 0.033f;
            Step();
            Invalidate();
        };
    }

    public void Start()
    {
        Seed();
        _timer.Start();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _timer.Dispose();
        base.Dispose(disposing);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        Seed();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Theme.Background);

        using (var wash = new LinearGradientBrush(
                   ClientRectangle,
                   Theme.Background,
                   Theme.BackgroundLift,
                   LinearGradientMode.ForwardDiagonal))
        {
            g.FillRectangle(wash, ClientRectangle);
        }

        DrawLinks(g);
        DrawParticles(g);
        DrawHeading(g);
    }

    private void DrawHeading(Graphics g)
    {
        TextRenderer.DrawText(
            g,
            Heading,
            Theme.TitleFont,
            new Rectangle(28, 22, Width - 120, 36),
            Theme.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter);

        TextRenderer.DrawText(
            g,
            Caption,
            Theme.CaptionFont,
            new Rectangle(28, 54, Width - 120, 22),
            Theme.Muted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
    }

    private void DrawParticles(Graphics g)
    {
        foreach (var particle in _particles)
        {
            var pulse = 0.55f + (float)Math.Sin(_time * particle.Twinkle + particle.Phase) * 0.45f;
            var alpha = (int)(particle.Alpha * pulse);
            if (alpha < 8)
                continue;

            using var brush = new SolidBrush(Color.FromArgb(alpha, Theme.Particle));
            var size = particle.Size;
            g.FillEllipse(brush, particle.X - size / 2f, particle.Y - size / 2f, size, size);
        }
    }

    private void DrawLinks(Graphics g)
    {
        const float maxDistance = 92f;
        for (var i = 0; i < _particles.Length; i++)
        {
            for (var j = i + 1; j < _particles.Length; j++)
            {
                var dx = _particles[i].X - _particles[j].X;
                var dy = _particles[i].Y - _particles[j].Y;
                var distance = MathF.Sqrt(dx * dx + dy * dy);
                if (distance > maxDistance)
                    continue;

                var alpha = (int)((1f - distance / maxDistance) * 28);
                if (alpha < 4)
                    continue;

                using var pen = new Pen(Color.FromArgb(alpha, Theme.Particle), 1f);
                g.DrawLine(pen, _particles[i].X, _particles[i].Y, _particles[j].X, _particles[j].Y);
            }
        }
    }

    private void Step()
    {
        var width = Math.Max(1, Width);
        var height = Math.Max(1, Height);

        for (var i = 0; i < _particles.Length; i++)
        {
            ref var particle = ref _particles[i];
            particle.X += particle.Vx;
            particle.Y += particle.Vy;

            if (particle.X < -10) particle.X = width + 10;
            if (particle.X > width + 10) particle.X = -10;
            if (particle.Y < -10) particle.Y = height + 10;
            if (particle.Y > height + 10) particle.Y = -10;
        }
    }

    private void Seed()
    {
        var width = Math.Max(1, Width);
        var height = Math.Max(1, Height);

        for (var i = 0; i < _particles.Length; i++)
        {
            _particles[i] = new Particle
            {
                X = _random.NextSingle() * width,
                Y = _random.NextSingle() * height,
                Vx = (_random.NextSingle() - 0.5f) * 0.28f,
                Vy = (_random.NextSingle() - 0.5f) * 0.18f,
                Size = 1.2f + _random.NextSingle() * 2.6f,
                Alpha = 40 + _random.Next(70),
                Phase = _random.NextSingle() * MathF.Tau,
                Twinkle = 0.4f + _random.NextSingle() * 0.9f
            };
        }
    }

    private struct Particle
    {
        public float X;
        public float Y;
        public float Vx;
        public float Vy;
        public float Size;
        public int Alpha;
        public float Phase;
        public float Twinkle;
    }
}
