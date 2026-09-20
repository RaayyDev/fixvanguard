using System.Diagnostics;
using System.Drawing.Drawing2D;

namespace FixVanguard;

public partial class Form1 : Form
{
    private ParticleCanvas _particles = null!;
    private Panel _card = null!;
    private StatusRow _installedRow = null!;
    private StatusRow _kernelRow = null!;
    private StatusRow _clientRow = null!;
    private StatusRow _trayRow = null!;
    private DarkButton _startButton = null!;
    private DarkButton _uninstallButton = null!;
    private ConsolePanel _console = null!;
    private IProgress<ActionLog> _log = null!;
    private System.Windows.Forms.Timer _statusTimer = null!;
    private GameStateWatcher _gameWatcher = null!;
    private Point _dragOffset;
    private bool _dragging;
    private bool _busy;
    private bool _blockedByUpdate;

    public Form1()
    {
        InitializeComponent();
        LoadAppIcon();
        BuildUi();
        RefreshStatus();
        Updater.CleanupOldBinary();
        _console.Write($"Fix Vanguard {UpdateChecker.CurrentVersion} listo. Leyendo el estado de Vanguard.", ActionLevel.Info);
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        await CheckForUpdatesAsync();
    }

    private async Task CheckForUpdatesAsync()
    {
        _console.Write("Comprobando actualizaciones.", ActionLevel.Info);
        var result = await UpdateChecker.CheckAsync();

        switch (result.Status)
        {
            case UpdateStatus.UpToDate:
                _console.Write($"Estás en la última versión ({result.Current}).", ActionLevel.Success);
                break;

            case UpdateStatus.Optional:
            case UpdateStatus.Required:
            case UpdateStatus.Blocked:
                ShowOutdatedAndBlock(result);
                break;

            case UpdateStatus.Failed:
                _console.Write($"No pude comprobar actualizaciones: {result.Error}", ActionLevel.Warning);
                break;
        }
    }

    private void BlockUi(string? reason)
    {
        _blockedByUpdate = true;
        _startButton.Enabled = false;
        _uninstallButton.Enabled = false;
        _statusTimer.Stop();
        if (!string.IsNullOrWhiteSpace(reason))
            _console.Write(reason, ActionLevel.Warning);
    }

    private void ShowOutdatedAndBlock(UpdateResult result)
    {
        var manifest = result.Manifest;
        var latest = result.Latest?.ToString() ?? manifest?.LatestVersion ?? "?";
        var current = result.Current.ToString();
        var discord = manifest?.DiscordUrl;

        var line = $"Loader version {current} — new version {latest} — go to Discord for new download.";
        _console.Write(line, ActionLevel.Error);
        if (!string.IsNullOrWhiteSpace(discord))
            _console.Write(discord!, ActionLevel.Info);
        if (!string.IsNullOrWhiteSpace(manifest?.Message))
            _console.Write(manifest!.Message!, ActionLevel.Warning);

        BlockUi(null);

        var body = $"Loader version {current}\nNew version {latest}\n\nGo to Discord for the new download.";
        if (!string.IsNullOrWhiteSpace(manifest?.Notes))
            body += $"\n\n{manifest!.Notes}";

        var buttons = string.IsNullOrWhiteSpace(discord)
            ? MessageBoxButtons.OK
            : MessageBoxButtons.OKCancel;
        var answer = MessageBox.Show(this, body, "Loader desactualizado", buttons, MessageBoxIcon.Warning);

        if (!string.IsNullOrWhiteSpace(discord) && answer == DialogResult.OK)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = discord,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                _console.Write($"No pude abrir el enlace: {ex.Message}", ActionLevel.Warning);
            }
        }
    }

    private void LoadAppIcon()
    {
        try
        {
            using var stream = typeof(Form1).Assembly.GetManifestResourceStream("FixVanguard.VANGUARD.ico");
            if (stream is not null)
                Icon = new Icon(stream);
        }
        catch
        {
            // Si por lo que sea no se carga el icono, la app sigue funcionando.
        }
    }

    private void BuildUi()
    {
        DoubleBuffered = true;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(720, 700);
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Font = Theme.BodyFont;
        Text = "Fix Vanguard Fragment";

        _particles = new ParticleCanvas();
        _particles.MouseDown += BeginDrag;
        _particles.MouseMove += DragWindow;
        _particles.MouseUp += (_, _) => _dragging = false;

        var minimize = MakeChrome("–", new Point(Width - 64, 0));
        minimize.Click += (_, _) => WindowState = FormWindowState.Minimized;

        var close = MakeChrome("×", new Point(Width - 32, 0));
        close.Click += (_, _) => Close();

        _card = new DoubleBufferedPanel
        {
            Location = new Point(28, 88),
            Size = new Size(Width - 56, 208),
            BackColor = Theme.Card
        };
        _card.Paint += PaintCard;

        var statusTitle = new Label
        {
            Text = "Estado",
            Font = Theme.BodyStrongFont,
            ForeColor = Theme.Text,
            BackColor = Theme.Card,
            Location = new Point(18, 12),
            AutoSize = true
        };
        _card.Controls.Add(statusTitle);

        _installedRow = AddStatusRow(_card, 46, Glyphs.Shield, "Instalación");
        _kernelRow = AddStatusRow(_card, 84, Glyphs.Chip, "Kernel vgk");
        _clientRow = AddStatusRow(_card, 122, Glyphs.Cloud, "Cliente vgc");
        _trayRow = AddStatusRow(_card, 160, Glyphs.Window, "Bandeja vgtray");

        _startButton = new DarkButton
        {
            Text = "Iniciar Vanguard",
            Glyph = Glyphs.Play,
            Location = new Point(28, 312),
            Size = new Size(Width - 56, 44)
        };
        _startButton.Click += async (_, _) => await RunStartAsync();

        _uninstallButton = new DarkButton
        {
            Text = "Desinstalar Vanguard",
            Glyph = Glyphs.Delete,
            Location = new Point(28, 364),
            Size = new Size(Width - 56, 44)
        };
        _uninstallButton.Click += async (_, _) => await RunUninstallAsync();

        _console = new ConsolePanel
        {
            Location = new Point(28, 424),
            Size = new Size(Width - 56, 248)
        };

        _log = new Progress<ActionLog>(entry => _console.Write(entry));

        _statusTimer = new System.Windows.Forms.Timer { Interval = 2000 };
        _statusTimer.Tick += (_, _) =>
        {
            if (!_busy)
                RefreshStatus();
        };

        _gameWatcher = new GameStateWatcher(_log);

        Controls.Add(_particles);
        Controls.Add(_card);
        Controls.Add(_startButton);
        Controls.Add(_uninstallButton);
        Controls.Add(_console);
        Controls.Add(minimize);
        Controls.Add(close);
        _particles.SendToBack();
        _particles.Start();
        _statusTimer.Start();
        _gameWatcher.Start();

        MouseDown += BeginDrag;
        MouseMove += DragWindow;
        MouseUp += (_, _) => _dragging = false;
        _card.MouseDown += BeginDrag;
        _card.MouseMove += DragWindow;
        _card.MouseUp += (_, _) => _dragging = false;
    }

    private static StatusRow AddStatusRow(Control parent, int y, string glyph, string label)
    {
        var row = new StatusRow(glyph, label)
        {
            Location = new Point(12, y),
            Size = new Size(parent.Width - 24, 34)
        };
        parent.Controls.Add(row);
        return row;
    }

    private DarkButton MakeChrome(string text, Point location)
    {
        return new DarkButton
        {
            Text = text,
            Location = location,
            Size = new Size(32, 28),
            Font = new Font("Segoe UI", 11f),
            FillColor = Theme.Background,
            HoverColor = Theme.CardHover,
            PressedColor = Theme.Card,
            BorderColor = Theme.Background,
            HoverBorderColor = Theme.Line,
            TextColor = Theme.Muted,
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };
    }

    private void PaintCard(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        var bounds = new Rectangle(0, 0, _card.Width - 1, _card.Height - 1);
        using var fill = new SolidBrush(Theme.Card);
        using var pen = new Pen(Theme.Line, 1f);
        g.FillRectangle(fill, bounds);
        g.DrawRectangle(pen, bounds);
        g.DrawLine(pen, 16, 40, _card.Width - 16, 40);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _statusTimer.Stop();
        _statusTimer.Dispose();
        _gameWatcher.Dispose();
        base.OnFormClosed(e);
    }

    private void BeginDrag(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
            return;

        _dragging = true;
        _dragOffset = e.Location;
        if (sender is Control control && control != this)
            _dragOffset = PointToClient(control.PointToScreen(e.Location));
    }

    private void DragWindow(object? sender, MouseEventArgs e)
    {
        if (!_dragging)
            return;

        var screen = sender is Control control && control != this
            ? control.PointToScreen(e.Location)
            : PointToScreen(e.Location);

        Location = new Point(screen.X - _dragOffset.X, screen.Y - _dragOffset.Y);
    }

    private void RefreshStatus()
    {
        var status = VanguardManager.GetStatus();
        _installedRow.SetValue(status.Installed ? "Instalado" : "No instalado", status.Installed);
        _kernelRow.SetValue(VanguardStatus.Label(status.Kernel), status.KernelRunning);
        _clientRow.SetValue(VanguardStatus.Label(status.Client), status.ClientRunning);
        _trayRow.SetValue(status.TrayRunning ? "En ejecución" : "Detenido", status.TrayRunning);
        _particles.Caption = status.Online
            ? "Vanguard en línea"
            : status.Installed
                ? "Instalado · servicios parados"
                : "No está en este equipo";

        // Si Vanguard no está instalado no tiene sentido dejar pulsar "Iniciar".
        // Y si el update-checker bloqueó la app, los botones quedan muertos.
        if (!_busy && !_blockedByUpdate)
        {
            _startButton.Enabled = status.Installed;
            _uninstallButton.Enabled = status.Installed;
        }
    }

    private async Task RunStartAsync()
    {
        if (_busy)
            return;

        if (!VanguardManager.GetStatus().Installed)
        {
            _console.Write("Vanguard no está instalado, no hay nada que iniciar.", ActionLevel.Warning);
            return;
        }

        await RunActionAsync(async () =>
        {
            _console.Write("Iniciando Vanguard y sus servicios.", ActionLevel.Info);
            await VanguardManager.StartAsync(_log);
        });
    }

    private async Task RunUninstallAsync()
    {
        if (_busy)
            return;

        var confirm = MessageBox.Show(
            this,
            "Se va a cerrar Riot, desinstalar Vanguard por completo y volver a abrir Riot cuando termine.\n\n¿Seguimos?",
            "Desinstalar Vanguard",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);

        if (confirm != DialogResult.Yes)
        {
            _console.Write("Desinstalación cancelada.", ActionLevel.Info);
            return;
        }

        await RunActionAsync(async () =>
        {
            _console.Write("Cerrando Riot y desinstalando Vanguard.", ActionLevel.Info);
            await VanguardManager.UninstallAsync(_log);
        });
    }

    private async Task RunActionAsync(Func<Task> action)
    {
        _busy = true;
        _startButton.Enabled = false;
        _uninstallButton.Enabled = false;
        UseWaitCursor = true;

        try
        {
            await action();
        }
        catch (Exception ex)
        {
            _console.Write(ex.Message, ActionLevel.Error);
        }
        finally
        {
            UseWaitCursor = false;
            _busy = false;
            if (!_blockedByUpdate)
            {
                _startButton.Enabled = true;
                _uninstallButton.Enabled = true;
            }
            RefreshStatus();
        }
    }

    private sealed class DoubleBufferedPanel : Panel
    {
        public DoubleBufferedPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }
    }

    private sealed class StatusRow : Panel
    {
        private readonly Label _icon;
        private readonly Label _value;
        private readonly StatusDot _dot;
        private bool _hover;

        public StatusRow(string glyph, string label)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Card;
            Cursor = Cursors.Default;

            _icon = new Label
            {
                Text = glyph,
                Font = Glyphs.Small,
                ForeColor = Theme.Accent,
                BackColor = Theme.Card,
                Location = new Point(10, 4),
                Size = new Size(24, 24),
                TextAlign = ContentAlignment.MiddleCenter
            };

            var key = new Label
            {
                Text = label,
                Font = Theme.BodyFont,
                ForeColor = Theme.Muted,
                BackColor = Theme.Card,
                Location = new Point(38, 6),
                AutoSize = true
            };

            _value = new Label
            {
                Text = "—",
                Font = Theme.BodyStrongFont,
                ForeColor = Theme.Text,
                BackColor = Theme.Card,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleRight,
                Location = new Point(Width - 150, 6),
                Size = new Size(112, 22),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };

            _dot = new StatusDot
            {
                Location = new Point(Width - 30, 8),
                Size = new Size(16, 16),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                BackColor = Theme.Card
            };

            Controls.Add(_icon);
            Controls.Add(key);
            Controls.Add(_value);
            Controls.Add(_dot);

            Resize += (_, _) =>
            {
                _value.Location = new Point(Width - 150, 6);
                _dot.Location = new Point(Width - 30, 8);
            };

            BindHover(this);
            BindHover(_icon);
            BindHover(key);
            BindHover(_value);
            BindHover(_dot);
        }

        public void SetValue(string text, bool active)
        {
            _value.Text = text;
            _value.ForeColor = active ? Theme.Success : Theme.Danger;
            _dot.Active = active;
            _icon.ForeColor = active ? Theme.Success : Theme.Danger;
        }

        private void BindHover(Control control)
        {
            control.MouseEnter += (_, _) => SetHover(true);
            control.MouseLeave += (_, _) =>
            {
                var cursor = PointToClient(Cursor.Position);
                if (!ClientRectangle.Contains(cursor))
                    SetHover(false);
            };
        }

        private void SetHover(bool hover)
        {
            _hover = hover;
            var color = _hover ? Theme.CardHover : Theme.Card;
            BackColor = color;
            foreach (Control control in Controls)
                control.BackColor = color;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            using var fill = new SolidBrush(BackColor);
            g.FillRectangle(fill, ClientRectangle);
            if (_hover)
            {
                using var pen = new Pen(Theme.Accent, 1f);
                g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            }
        }
    }

    private sealed class StatusDot : Control
    {
        private bool _active;

        public StatusDot()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            Size = new Size(16, 16);
        }

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool Active
        {
            get => _active;
            set
            {
                _active = value;
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);

            var color = _active ? Theme.Success : Theme.Danger;
            using var glow = new SolidBrush(Color.FromArgb(70, color));
            using var core = new SolidBrush(color);
            g.FillEllipse(glow, 0, 0, 15, 15);
            g.FillEllipse(core, 3, 3, 9, 9);
        }
    }
}
