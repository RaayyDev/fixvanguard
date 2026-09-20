using System.Diagnostics;
using FixVanguard;

namespace FixVanguardUpdater;

public partial class Form1 : Form
{
    private readonly Settings _settings = Settings.Load();

    private TextBox _exePath = null!;
    private TextBox _version = null!;
    private TextBox _minVersion = null!;
    private TextBox _notes = null!;
    private CheckBox _killSwitch = null!;
    private TextBox _discord = null!;
    private TextBox _message = null!;
    private TextBox _owner = null!;
    private TextBox _repo = null!;
    private TextBox _branch = null!;
    private TextBox _manifestPath = null!;
    private TextBox _assetName = null!;
    private TextBox _token = null!;

    private DarkButton _browseButton = null!;
    private DarkButton _uploadButton = null!;
    private ConsolePanel _console = null!;
    private IProgress<ActionLog> _log = null!;

    private bool _busy;

    public Form1()
    {
        Text = "Fix Vanguard Updater";
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(760, 720);
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Font = Theme.BodyFont;
        DoubleBuffered = true;

        LoadIcon();
        BuildUi();
        BindSettings();
    }

    private void LoadIcon()
    {
        try
        {
            var ico = Path.Combine(AppContext.BaseDirectory, "VANGUARD.ico");
            if (File.Exists(ico)) Icon = new Icon(ico);
        }
        catch { }
    }

    private void BuildUi()
    {
        var title = new Label
        {
            Text = "Fix Vanguard Updater",
            Font = Theme.TitleFont,
            ForeColor = Theme.Text,
            Location = new Point(24, 16),
            AutoSize = true
        };

        var subtitle = new Label
        {
            Text = "Sube el nuevo .exe a GitHub Releases y publica manifest.json en el repo.",
            Font = Theme.CaptionFont,
            ForeColor = Theme.Muted,
            Location = new Point(24, 50),
            AutoSize = true
        };

        Controls.Add(title);
        Controls.Add(subtitle);

        var y = 84;
        y = AddField("Ruta del FixVanguard.exe nuevo", 24, y, out _exePath, width: 560);
        _browseButton = new DarkButton
        {
            Text = "Buscar",
            Location = new Point(596, y - 32),
            Size = new Size(140, 30),
            Font = Theme.BodyStrongFont
        };
        _browseButton.Click += (_, _) => BrowseExe();
        Controls.Add(_browseButton);

        y = AddField("Versión (p. ej. 1.1.0)", 24, y, out _version, width: 200);
        y -= 60; // meter min-supported en la misma linea
        y = AddField("Versión mínima soportada", 260, y + 60, out _minVersion, width: 200);
        y = AddField("URL de Discord (opcional, se abre desde el aviso)", 24, y, out _discord, width: 712);
        y = AddField("Mensaje corto para el aviso", 24, y, out _message, width: 712);
        y = AddField("Notas del release (multi-linea)", 24, y, out _notes, width: 712, multiline: true, height: 60);

        _killSwitch = new CheckBox
        {
            Text = "Kill-switch: bloquea TODAS las versiones actuales (incluso la nueva) hasta que subas otra.",
            Location = new Point(24, y),
            ForeColor = Theme.Muted,
            BackColor = Theme.Background,
            AutoSize = true
        };
        Controls.Add(_killSwitch);
        y += 32;

        // Bloque GitHub
        var githubHeader = new Label
        {
            Text = "GitHub",
            Font = Theme.BodyStrongFont,
            ForeColor = Theme.Accent,
            Location = new Point(24, y),
            AutoSize = true
        };
        Controls.Add(githubHeader);
        y += 22;

        y = AddField("Owner", 24, y, out _owner, width: 220);
        y -= 60;
        y = AddField("Repo", 260, y + 60, out _repo, width: 220);
        y -= 60;
        y = AddField("Branch", 496, y + 60, out _branch, width: 240);

        y = AddField("Ruta del manifest en el repo", 24, y, out _manifestPath, width: 340);
        y -= 60;
        y = AddField("Nombre del asset", 380, y + 60, out _assetName, width: 356);

        y = AddField("GitHub token (PAT con scope 'repo')", 24, y, out _token, width: 712, password: true);

        _uploadButton = new DarkButton
        {
            Text = "Publicar release + manifest",
            Glyph = Glyphs.Cloud,
            Location = new Point(24, y),
            Size = new Size(712, 44)
        };
        _uploadButton.Click += async (_, _) => await UploadAsync();
        Controls.Add(_uploadButton);
        y += 56;

        _console = new ConsolePanel
        {
            Location = new Point(24, y),
            Size = new Size(712, 720 - y - 20)
        };
        Controls.Add(_console);
        _log = new Progress<ActionLog>(entry => _console.Write(entry));
    }

    private int AddField(string label, int x, int y, out TextBox box, int width = 300,
                         bool multiline = false, int height = 26, bool password = false)
    {
        var lbl = new Label
        {
            Text = label,
            Font = Theme.CaptionFont,
            ForeColor = Theme.Muted,
            BackColor = Theme.Background,
            Location = new Point(x, y),
            AutoSize = true
        };
        Controls.Add(lbl);

        box = new TextBox
        {
            Location = new Point(x, y + 20),
            Size = new Size(width, multiline ? height : 26),
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Theme.Card,
            ForeColor = Theme.Text,
            Font = Theme.BodyFont,
            Multiline = multiline,
            UseSystemPasswordChar = password
        };
        if (multiline)
            box.ScrollBars = ScrollBars.Vertical;
        Controls.Add(box);
        return y + 20 + (multiline ? height : 26) + 12;
    }

    private void BindSettings()
    {
        _exePath.Text = _settings.ExePath;
        _version.Text = _settings.Version;
        _minVersion.Text = _settings.MinSupportedVersion;
        _notes.Text = _settings.Notes;
        _killSwitch.Checked = _settings.KillSwitch;
        _discord.Text = _settings.DiscordUrl;
        _message.Text = _settings.Message;
        _owner.Text = _settings.Owner;
        _repo.Text = _settings.Repo;
        _branch.Text = string.IsNullOrWhiteSpace(_settings.Branch) ? "main" : _settings.Branch;
        _manifestPath.Text = string.IsNullOrWhiteSpace(_settings.ManifestPath) ? "manifest.json" : _settings.ManifestPath;
        _assetName.Text = string.IsNullOrWhiteSpace(_settings.AssetName) ? "FixVanguard.exe" : _settings.AssetName;
        _token.Text = _settings.Token;
    }

    private void PersistSettings()
    {
        _settings.ExePath = _exePath.Text.Trim();
        _settings.Version = _version.Text.Trim();
        _settings.MinSupportedVersion = _minVersion.Text.Trim();
        _settings.Notes = _notes.Text.Trim();
        _settings.KillSwitch = _killSwitch.Checked;
        _settings.DiscordUrl = _discord.Text.Trim();
        _settings.Message = _message.Text.Trim();
        _settings.Owner = _owner.Text.Trim();
        _settings.Repo = _repo.Text.Trim();
        _settings.Branch = _branch.Text.Trim();
        _settings.ManifestPath = _manifestPath.Text.Trim();
        _settings.AssetName = _assetName.Text.Trim();
        _settings.Token = _token.Text.Trim();
        _settings.Save();
    }

    private void BrowseExe()
    {
        using var dlg = new OpenFileDialog
        {
            Filter = "Ejecutables (*.exe)|*.exe|Todos los archivos (*.*)|*.*",
            Title = "Selecciona el FixVanguard.exe nuevo"
        };
        if (!string.IsNullOrWhiteSpace(_exePath.Text) && Directory.Exists(Path.GetDirectoryName(_exePath.Text)))
            dlg.InitialDirectory = Path.GetDirectoryName(_exePath.Text);

        if (dlg.ShowDialog(this) == DialogResult.OK)
            _exePath.Text = dlg.FileName;
    }

    private async Task UploadAsync()
    {
        if (_busy) return;
        var errors = ValidateInputs();
        if (errors.Count > 0)
        {
            foreach (var err in errors) _console.Write(err, ActionLevel.Error);
            MessageBox.Show(this, string.Join("\n", errors), "Faltan datos",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        PersistSettings();
        _busy = true;
        _uploadButton.Enabled = false;
        UseWaitCursor = true;

        var req = new GithubReleaser.PublishRequest(
            ExePath: _settings.ExePath,
            Version: _settings.Version,
            MinSupportedVersion: string.IsNullOrWhiteSpace(_settings.MinSupportedVersion) ? _settings.Version : _settings.MinSupportedVersion,
            Notes: _settings.Notes,
            KillSwitch: _settings.KillSwitch,
            DiscordUrl: _settings.DiscordUrl,
            Message: _settings.Message,
            Owner: _settings.Owner,
            Repo: _settings.Repo,
            Branch: string.IsNullOrWhiteSpace(_settings.Branch) ? "main" : _settings.Branch,
            ManifestPath: string.IsNullOrWhiteSpace(_settings.ManifestPath) ? "manifest.json" : _settings.ManifestPath,
            AssetName: string.IsNullOrWhiteSpace(_settings.AssetName) ? "FixVanguard.exe" : _settings.AssetName);

        try
        {
            _console.Write($"Publicando FixVanguard {req.Version} en {req.Owner}/{req.Repo}", ActionLevel.Info);
            var releaser = new GithubReleaser(_settings.Token, _log);
            var manifestUrl = await releaser.PublishAsync(req);
            _console.Write("Todo listo. Los loaders viejos verán el aviso al abrir.", ActionLevel.Success);
            var rawUrl = manifestUrl.ToString();
            _console.Write($"URL del manifest.json (pon esta en UpdateChecker.ManifestUrl): {rawUrl}", ActionLevel.Info);

            var open = MessageBox.Show(this,
                $"Manifest publicado:\n{rawUrl}\n\n¿Copiar al portapapeles?",
                "Publicado", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
            if (open == DialogResult.Yes)
                Clipboard.SetText(rawUrl);
        }
        catch (Exception ex)
        {
            _console.Write($"Falló la publicación: {ex.Message}", ActionLevel.Error);
        }
        finally
        {
            UseWaitCursor = false;
            _uploadButton.Enabled = true;
            _busy = false;
        }
    }

    private List<string> ValidateInputs()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(_exePath.Text) || !File.Exists(_exePath.Text))
            errors.Add("Selecciona un .exe válido.");
        if (string.IsNullOrWhiteSpace(_version.Text) || !Version.TryParse(_version.Text.Trim(), out _))
            errors.Add("Versión inválida (usa formato X.Y.Z).");
        if (!string.IsNullOrWhiteSpace(_minVersion.Text) && !Version.TryParse(_minVersion.Text.Trim(), out _))
            errors.Add("Versión mínima inválida (usa formato X.Y.Z o déjalo vacío).");
        if (string.IsNullOrWhiteSpace(_owner.Text)) errors.Add("Falta owner de GitHub.");
        if (string.IsNullOrWhiteSpace(_repo.Text))  errors.Add("Falta repo de GitHub.");
        if (string.IsNullOrWhiteSpace(_token.Text)) errors.Add("Falta el token de GitHub.");
        return errors;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        PersistSettings();
        base.OnFormClosing(e);
    }
}
