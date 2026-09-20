using System.Diagnostics;
using System.Text.RegularExpressions;

namespace FixVanguard;

internal enum ValorantPhase
{
    Closed,
    Menus,
    PreGame,
    InGame
}

// Lee ShooterGame.log en modo tail y traduce sessionLoopState + characterID + mapa
// a mensajes en español para la consola. El log lo abre con FileShare.ReadWrite
// para no molestar a VALORANT.
internal sealed class GameStateWatcher : IDisposable
{
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VALORANT", "Saved", "Logs", "ShooterGame.log");

    private const string ValorantProcess = "VALORANT-Win64-Shipping";

    private readonly IProgress<ActionLog> _log;
    private readonly System.Windows.Forms.Timer _timer;

    private bool _valorantOpen;
    private ValorantPhase _phase = ValorantPhase.Closed;
    private long _lastOffset;
    private string? _lastAgent;
    private string? _lastMap;
    private bool _polling;

    // sessionLoopState=MENUS / "sessionLoopState":"INGAME" / etc. cubre las 2 formas.
    private static readonly Regex StateRx = new(
        @"sessionLoopState[""']?\s*[:=]\s*[""']?(MENUS|PREGAME|INGAME)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex CharacterRx = new(
        @"characterID[""']?\s*[:=]\s*[""']?([0-9a-fA-F]{8}-?[0-9a-fA-F]{4}-?[0-9a-fA-F]{4}-?[0-9a-fA-F]{4}-?[0-9a-fA-F]{12})",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex MapRx = new(
        @"/Game/Maps/([A-Za-z0-9_]+)/[A-Za-z0-9_]+",
        RegexOptions.Compiled);

    public GameStateWatcher(IProgress<ActionLog> log)
    {
        _log = log;
        _timer = new System.Windows.Forms.Timer { Interval = 1500 };
        _timer.Tick += async (_, _) => await PollAsync();
    }

    public void Start() => _timer.Start();

    public void Dispose()
    {
        _timer.Stop();
        _timer.Dispose();
    }

    private async Task PollAsync()
    {
        if (_polling) return;
        _polling = true;
        try
        {
            var open = Process.GetProcessesByName(ValorantProcess).Length > 0;

            if (open && !_valorantOpen)
            {
                _valorantOpen = true;
                _phase = ValorantPhase.Closed;
                _lastAgent = null;
                _lastMap = null;
                // Saltar todo lo viejo: arrancamos "en directo".
                _lastOffset = SafeFileLength(LogPath);
                _log.Report(new("VALORANT detectado.", ActionLevel.Success));
            }
            else if (!open && _valorantOpen)
            {
                _valorantOpen = false;
                _phase = ValorantPhase.Closed;
                _log.Report(new("VALORANT cerrado.", ActionLevel.Info));
                return;
            }

            if (!_valorantOpen) return;

            await Task.Run(TailLog);
        }
        finally
        {
            _polling = false;
        }
    }

    private static long SafeFileLength(string path)
    {
        try { return new FileInfo(path).Exists ? new FileInfo(path).Length : 0; }
        catch { return 0; }
    }

    private void TailLog()
    {
        if (!File.Exists(LogPath)) return;

        try
        {
            using var fs = new FileStream(
                LogPath, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);

            // Si el log rotó (nueva partida, se truncó), volvemos al principio del nuevo.
            if (_lastOffset > fs.Length) _lastOffset = 0;

            fs.Seek(_lastOffset, SeekOrigin.Begin);
            using var reader = new StreamReader(fs);
            string? line;
            while ((line = reader.ReadLine()) != null)
                HandleLine(line);

            _lastOffset = fs.Position;
        }
        catch
        {
            // Si el file está bloqueado un tick, lo reintentamos en el siguiente.
        }
    }

    private void HandleLine(string line)
    {
        // Actualizamos mapa y agente aunque todavía no haya cambio de fase:
        // así al entrar a INGAME ya tenemos los últimos valores.
        var mapMatch = MapRx.Match(line);
        if (mapMatch.Success)
        {
            var name = MapNameFrom(mapMatch.Groups[1].Value);
            if (!string.IsNullOrEmpty(name)) _lastMap = name;
        }

        var charMatch = CharacterRx.Match(line);
        if (charMatch.Success)
        {
            var uuid = NormalizeUuid(charMatch.Groups[1].Value);
            var agent = AgentFrom(uuid) ?? "desconocido";
            var changed = agent != _lastAgent;
            _lastAgent = agent;
            if (changed && _phase == ValorantPhase.PreGame)
                _log.Report(new($"Agente seleccionado: {agent}.", ActionLevel.Info));
        }

        var stateMatch = StateRx.Match(line);
        if (!stateMatch.Success) return;

        var next = stateMatch.Groups[1].Value.ToUpperInvariant() switch
        {
            "MENUS" => ValorantPhase.Menus,
            "PREGAME" => ValorantPhase.PreGame,
            "INGAME" => ValorantPhase.InGame,
            _ => _phase
        };
        if (next == _phase) return;
        _phase = next;

        switch (_phase)
        {
            case ValorantPhase.Menus:
                _log.Report(new("En lobby detectado.", ActionLevel.Info));
                break;
            case ValorantPhase.PreGame:
                _lastAgent = null; // reset por si la partida anterior dejó restos
                _log.Report(new("Seleccionando agente.", ActionLevel.Info));
                break;
            case ValorantPhase.InGame:
                var map = _lastMap ?? "mapa desconocido";
                var agent = _lastAgent ?? "agente desconocido";
                _log.Report(new($"Iniciando partida en {map} con {agent}.", ActionLevel.Success));
                break;
        }
    }

    private static string NormalizeUuid(string raw)
    {
        var clean = raw.Replace("-", string.Empty).ToLowerInvariant();
        if (clean.Length != 32) return raw.ToLowerInvariant();
        return $"{clean[..8]}-{clean.Substring(8, 4)}-{clean.Substring(12, 4)}-{clean.Substring(16, 4)}-{clean.Substring(20, 12)}";
    }

    private static string? MapNameFrom(string codename) => codename.ToLowerInvariant() switch
    {
        "ascent" => "Ascent",
        "bonsai" => "Split",
        "duality" => "Bind",
        "port" => "Icebox",
        "triad" => "Haven",
        "foxtrot" => "Breeze",
        "canyon" => "Fracture",
        "pitt" => "Pearl",
        "jam" => "Lotus",
        "juliett" => "Sunset",
        "infinity" => "Abyss",
        "rook" => "Corrode",
        "poveglia" => "Poveglia",
        "hurm_alley" or "hurm_bowl" or "hurm_yard" or "hurm_helix" => "Team Deathmatch",
        "range" or "poveglia_dm" => "Range",
        _ => Capitalize(codename)
    };

    private static string Capitalize(string s) =>
        string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s[1..].ToLowerInvariant();

    private static string? AgentFrom(string uuid) => uuid switch
    {
        "add6443a-41bd-e414-f6ad-e58d267f4e95" => "Jett",
        "a3bfb853-43b2-7238-a4f1-ad90e9e46bce" => "Reyna",
        "569fdd95-4d10-43ab-ca70-79becc718b46" => "Sage",
        "eb93336a-449b-9c1b-0a54-a891f7921d69" => "Phoenix",
        "ded3520f-4264-bfed-162d-b080e2abccf9" => "Sova",
        "117ed9e3-49f3-6512-3ccf-0cada7e3823b" => "Cypher",
        "9f0d8ba9-4140-b941-57d3-a7ad57c6b417" => "Brimstone",
        "707eab51-4836-f488-046a-cda6bf494859" => "Viper",
        "8e253930-4c05-31dd-1b6c-968525494517" => "Omen",
        "1e58de9c-4950-5125-93e9-a0aee9f98746" => "Killjoy",
        "5f8d3a7f-467b-97f3-062c-13acf203c006" => "Breach",
        "6f2a04ca-43e0-be17-7f36-b3908627744d" => "Skye",
        "7f94d92c-4234-0a36-9646-3a87eb8b5c89" => "Yoru",
        "41fb69c1-4189-7b37-f117-bcaf1e96f1bf" => "Astra",
        "601dbbe7-43ce-be57-2a40-4abd24953621" => "KAY/O",
        "22697a3d-45bf-8dd7-4fec-84a9e28c69d7" => "Chamber",
        "bb2a4828-46eb-8cd1-e765-15848195d751" => "Neon",
        "dade69b4-4f5a-8528-247b-219e5a1facd6" => "Fade",
        "95b78ed7-4637-86d9-7e41-71ba8c293152" => "Harbor",
        "e370fa57-4757-3604-3648-499e1f642d3f" => "Gekko",
        "cc8b64c8-4b25-4ff9-6e7f-37b4da43d235" => "Deadlock",
        "0e38b510-41a8-5780-5e8f-568b2a4f2d6c" => "Iso",
        "1dbf2edd-4729-0984-3115-daa5eed44993" => "Clove",
        "efba5359-4016-a1e5-7626-b1ae76895940" => "Vyse",
        "f94c3b30-42be-e959-889c-5aa313dba261" => "Raze",
        _ => null
    };
}
