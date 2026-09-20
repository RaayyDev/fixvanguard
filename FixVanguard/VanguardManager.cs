using System.Diagnostics;
using System.ServiceProcess;
using System.Text.RegularExpressions;

namespace FixVanguard;

internal enum ServiceRunState
{
    Missing,
    Running,
    Stopped,
    Pending,
    Unknown
}

internal sealed class VanguardStatus
{
    public bool Installed { get; init; }
    public ServiceRunState Kernel { get; init; }
    public ServiceRunState Client { get; init; }
    public bool TrayRunning { get; init; }
    public bool KernelRunning => Kernel == ServiceRunState.Running;
    public bool ClientRunning => Client == ServiceRunState.Running;
    public bool Online => KernelRunning && ClientRunning;

    public static string Label(ServiceRunState state) => state switch
    {
        ServiceRunState.Running => "En ejecución",
        ServiceRunState.Stopped => "Detenido",
        ServiceRunState.Pending => "En proceso",
        ServiceRunState.Missing => "No encontrado",
        _ => "Desconocido"
    };
}

internal static class VanguardManager
{
    public const string KernelService = "vgk";
    public const string ClientService = "vgc";
    public const string TrayProcess = "vgtray";

    private static readonly string[] RiotProcessNames =
    [
        "RiotClientServices",
        "RiotClientUx",
        "RiotClientUxRender",
        "RiotClientCrashHandler",
        "LeagueClient",
        "LeagueClientUx",
        "LeagueClientUxRender",
        "VALORANT-Win64-Shipping",
        "RiotClient"
    ];

    public static string InstallPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        "Riot Vanguard");

    public static string TrayPath => Path.Combine(InstallPath, "vgtray.exe");
    public static string UninstallerPath => Path.Combine(InstallPath, "uninstall.exe");

    public static VanguardStatus GetStatus()
    {
        var kernel = QueryService(KernelService);
        var client = QueryService(ClientService);

        return new VanguardStatus
        {
            Installed = Directory.Exists(InstallPath) || kernel != ServiceRunState.Missing || client != ServiceRunState.Missing,
            Kernel = kernel,
            Client = client,
            TrayRunning = Process.GetProcessesByName(TrayProcess).Length > 0
        };
    }

    public static async Task StartAsync(IProgress<ActionLog> log, CancellationToken cancellationToken = default)
    {
        if (!GetStatus().Installed)
            throw new InvalidOperationException("Vanguard no está instalado en este equipo.");

        var riotPath = FindRiotClientPath();
        CloseRiot(log);

        log.Report(new("Preparando servicios de Vanguard.", ActionLevel.Info));
        await ConfigureServiceAsync(KernelService, "system", "con el sistema", log, cancellationToken);
        await ConfigureServiceAsync(ClientService, "demand", "bajo demanda", log, cancellationToken);

        await StartServiceAsync(KernelService, log, cancellationToken);
        await StartServiceAsync(ClientService, log, cancellationToken);
        StartTray(log);

        var status = GetStatus();
        if (status.Online)
        {
            log.Report(new("Vanguard en línea: vgk, vgc y bandeja listos.", ActionLevel.Success));
            OpenRiot(log, riotPath);
        }
        else if (!status.KernelRunning)
        {
            log.Report(new("vgk no arrancó. Si se paró antes, Windows suele pedir un reinicio.", ActionLevel.Warning));
            log.Report(new("No abro Riot para que arregles Vanguard primero.", ActionLevel.Info));
        }
        else
        {
            log.Report(new("vgc no arrancó. Revisa vgk o reinicia el PC.", ActionLevel.Warning));
            log.Report(new("No abro Riot para que arregles Vanguard primero.", ActionLevel.Info));
        }
    }

    public static async Task UninstallAsync(IProgress<ActionLog> log, CancellationToken cancellationToken = default)
    {
        if (!GetStatus().Installed)
            throw new InvalidOperationException("Vanguard no está instalado.");

        var riotPath = FindRiotClientPath();
        CloseRiot(log);
        KillTray(log);

        log.Report(new("Deteniendo servicios de Vanguard.", ActionLevel.Info));
        await StopServiceAsync(ClientService, log, cancellationToken);
        await StopServiceAsync(KernelService, log, cancellationToken);

        if (File.Exists(UninstallerPath))
        {
            log.Report(new("Abriendo el desinstalador oficial de Riot.", ActionLevel.Info));
            var exit = await RunSilentAsync(UninstallerPath, string.Empty, cancellationToken, useShell: true);
            log.Report(exit == 0
                ? new("El desinstalador oficial terminó bien.", ActionLevel.Success)
                : new($"El desinstalador cerró con código {exit}.", ActionLevel.Warning));
        }
        else
        {
            log.Report(new("No hay desinstalador oficial. Limpio servicios y carpeta a mano.", ActionLevel.Warning));
        }

        await DeleteServiceAsync(ClientService, log, cancellationToken);
        await DeleteServiceAsync(KernelService, log, cancellationToken);
        RemoveLeftoverFiles(log);

        if (GetStatus().Installed)
            log.Report(new("Quedan restos. Reinicia el PC y vuelve a desinstalar.", ActionLevel.Warning));
        else
            log.Report(new("Vanguard quedó desinstalado por completo.", ActionLevel.Success));

        OpenRiot(log, riotPath);
    }

    public static string? FindRiotClientPath()
    {
        var jsonPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Riot Games",
            "RiotClientInstalls.json");

        if (File.Exists(jsonPath))
        {
            var json = File.ReadAllText(jsonPath);
            foreach (var key in new[] { "rc_default", "rc_live", "rc_beta" })
            {
                var match = Regex.Match(json, $"\"{key}\"\\s*:\\s*\"([^\"]+)\"");
                if (!match.Success)
                    continue;

                var path = match.Groups[1].Value.Replace('/', Path.DirectorySeparatorChar);
                if (File.Exists(path))
                    return path;
            }
        }

        foreach (var candidate in new[]
        {
            @"C:\Riot Games\Riot Client\RiotClientServices.exe",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Riot Games", "Riot Client", "RiotClientServices.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Riot Games", "Riot Client", "RiotClientServices.exe")
        })
        {
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    private static void CloseRiot(IProgress<ActionLog> log)
    {
        var closed = 0;
        foreach (var name in RiotProcessNames)
        {
            foreach (var process in Process.GetProcessesByName(name))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(4000);
                    closed++;
                }
                catch (Exception ex)
                {
                    log.Report(new($"No pude cerrar {name}: {ex.Message}", ActionLevel.Warning));
                }
                finally
                {
                    process.Dispose();
                }
            }
        }

        log.Report(closed > 0
            ? new($"Riot cerrado. Se pararon {closed} procesos.", ActionLevel.Success)
            : new("Riot no estaba abierto.", ActionLevel.Info));
    }

    private static void OpenRiot(IProgress<ActionLog> log, string? path)
    {
        path ??= FindRiotClientPath();
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            log.Report(new("No encontré Riot Client para reabrirlo.", ActionLevel.Warning));
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });
        log.Report(new("Riot Client abierto otra vez.", ActionLevel.Success));
    }

    private static void StartTray(IProgress<ActionLog> log)
    {
        if (Process.GetProcessesByName(TrayProcess).Length > 0)
        {
            log.Report(new("La bandeja de Vanguard ya estaba abierta.", ActionLevel.Info));
            return;
        }

        if (!File.Exists(TrayPath))
        {
            log.Report(new("No está vgtray.exe. La bandeja no se pudo abrir.", ActionLevel.Warning));
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = TrayPath,
            WorkingDirectory = InstallPath,
            UseShellExecute = true
        });
        log.Report(new("Bandeja de Vanguard iniciada.", ActionLevel.Success));
    }

    private static void KillTray(IProgress<ActionLog> log)
    {
        var processes = Process.GetProcessesByName(TrayProcess);
        if (processes.Length == 0)
        {
            log.Report(new("La bandeja no estaba en ejecución.", ActionLevel.Info));
            return;
        }

        foreach (var process in processes)
        {
            try
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(4000);
            }
            catch (Exception ex)
            {
                log.Report(new($"No pude cerrar la bandeja: {ex.Message}", ActionLevel.Warning));
            }
            finally
            {
                process.Dispose();
            }
        }

        log.Report(new("Bandeja de Vanguard cerrada.", ActionLevel.Success));
    }

    private static async Task ConfigureServiceAsync(string name, string startType, string label, IProgress<ActionLog> log, CancellationToken cancellationToken)
    {
        if (QueryService(name) == ServiceRunState.Missing)
            return;

        var code = await RunSilentAsync("sc.exe", $"config {name} start= {startType}", cancellationToken);
        log.Report(code == 0
            ? new($"Servicio {name} configurado para arrancar {label}.", ActionLevel.Success)
            : new($"No pude configurar {name}.", ActionLevel.Warning));
    }

    private static async Task StartServiceAsync(string name, IProgress<ActionLog> log, CancellationToken cancellationToken)
    {
        if (QueryService(name) == ServiceRunState.Missing)
        {
            log.Report(new($"El servicio {name} no existe.", ActionLevel.Error));
            return;
        }

        if (QueryService(name) == ServiceRunState.Running)
        {
            log.Report(new($"El servicio {name} ya estaba en ejecución.", ActionLevel.Info));
            return;
        }

        // sc.exe start solo pone el servicio en START_PENDING y vuelve enseguida:
        // hay que esperar activamente hasta RUNNING, si no la UI dice "vgc no arranco".
        var started = await WaitForStateAsync(name, ServiceControllerStatus.Running,
                                              TimeSpan.FromSeconds(25), autoStart: true, cancellationToken);

        if (started)
            log.Report(new($"Servicio {name} iniciado.", ActionLevel.Success));
        else if (QueryService(name) == ServiceRunState.Pending)
            log.Report(new($"{name} sigue arrancando. Suele terminar solo en unos segundos.", ActionLevel.Warning));
        else
            log.Report(new($"No pude iniciar {name}.", ActionLevel.Warning));
    }

    private static async Task StopServiceAsync(string name, IProgress<ActionLog> log, CancellationToken cancellationToken)
    {
        if (QueryService(name) == ServiceRunState.Missing)
        {
            log.Report(new($"El servicio {name} no existe.", ActionLevel.Info));
            return;
        }

        var stopped = await WaitForStateAsync(name, ServiceControllerStatus.Stopped,
                                              TimeSpan.FromSeconds(20), autoStart: false, cancellationToken);

        if (stopped)
            log.Report(new($"Servicio {name} detenido.", ActionLevel.Success));
        else
            log.Report(new($"No pude detener {name}.", ActionLevel.Warning));
    }

    // Arranca (o para, segun 'autoStart') y luego hace polling hasta el estado destino con timeout.
    private static async Task<bool> WaitForStateAsync(string name, ServiceControllerStatus target,
                                                     TimeSpan timeout, bool autoStart,
                                                     CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var sc = new ServiceController(name);
                sc.Refresh();

                if (autoStart)
                {
                    if (sc.Status != ServiceControllerStatus.Running &&
                        sc.Status != ServiceControllerStatus.StartPending)
                    {
                        try { sc.Start(); }
                        catch (InvalidOperationException) { /* ya arrancando */ }
                    }
                }
                else
                {
                    if (sc.Status != ServiceControllerStatus.Stopped &&
                        sc.Status != ServiceControllerStatus.StopPending &&
                        sc.CanStop)
                    {
                        try { sc.Stop(); }
                        catch (InvalidOperationException) { /* ya parando */ }
                    }
                }

                var deadline = DateTime.UtcNow + timeout;
                while (DateTime.UtcNow < deadline)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    sc.Refresh();
                    if (sc.Status == target) return true;
                    Thread.Sleep(300);
                }
                sc.Refresh();
                return sc.Status == target;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return false;
            }
        }, cancellationToken);
    }

    private static async Task DeleteServiceAsync(string name, IProgress<ActionLog> log, CancellationToken cancellationToken)
    {
        if (QueryService(name) == ServiceRunState.Missing)
            return;

        var code = await RunSilentAsync("sc.exe", $"delete {name}", cancellationToken);
        log.Report(code == 0
            ? new($"Servicio {name} eliminado.", ActionLevel.Success)
            : new($"No pude borrar {name}. Suele hacer falta un reinicio.", ActionLevel.Warning));
    }

    private static void RemoveLeftoverFiles(IProgress<ActionLog> log)
    {
        if (!Directory.Exists(InstallPath))
        {
            log.Report(new("La carpeta de Riot Vanguard ya no está.", ActionLevel.Info));
            return;
        }

        try
        {
            Directory.Delete(InstallPath, recursive: true);
            log.Report(new("Carpeta de Riot Vanguard eliminada.", ActionLevel.Success));
        }
        catch (Exception ex)
        {
            log.Report(new($"Quedaron archivos de Vanguard: {ex.Message}", ActionLevel.Warning));
        }
    }

    private static ServiceRunState QueryService(string name)
    {
        try
        {
            using var controller = new ServiceController(name);
            controller.Refresh();
            return Map(controller.Status);
        }
        catch (InvalidOperationException)
        {
            return QueryServiceFromSc(name);
        }
        catch
        {
            return QueryServiceFromSc(name);
        }
    }

    private static ServiceRunState Map(ServiceControllerStatus status) => status switch
    {
        ServiceControllerStatus.Running => ServiceRunState.Running,
        ServiceControllerStatus.Stopped => ServiceRunState.Stopped,
        ServiceControllerStatus.StartPending or ServiceControllerStatus.StopPending
            or ServiceControllerStatus.ContinuePending or ServiceControllerStatus.PausePending => ServiceRunState.Pending,
        _ => ServiceRunState.Unknown
    };

    private static ServiceRunState QueryServiceFromSc(string name)
    {
        var result = Run("sc.exe", $"query {name}");
        var output = result.Output;

        if (output.Contains("RUNNING", StringComparison.OrdinalIgnoreCase))
            return ServiceRunState.Running;
        if (output.Contains("STOPPED", StringComparison.OrdinalIgnoreCase))
            return ServiceRunState.Stopped;
        if (output.Contains("PENDING", StringComparison.OrdinalIgnoreCase))
            return ServiceRunState.Pending;
        if (result.ExitCode != 0 || output.Contains("1060") ||
            output.Contains("does not exist", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("no existe", StringComparison.OrdinalIgnoreCase))
            return ServiceRunState.Missing;

        return string.IsNullOrWhiteSpace(output) ? ServiceRunState.Unknown : ServiceRunState.Unknown;
    }

    private static (int ExitCode, string Output) Run(string fileName, string arguments)
    {
        var info = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.Default,
            StandardErrorEncoding = System.Text.Encoding.Default
        };

        using var process = Process.Start(info);
        if (process is null)
            return (-1, string.Empty);

        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output);
    }

    private static async Task<int> RunSilentAsync(string fileName, string arguments, CancellationToken cancellationToken, bool useShell = false)
    {
        var info = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = useShell,
            RedirectStandardOutput = !useShell,
            RedirectStandardError = !useShell,
            CreateNoWindow = !useShell
        };

        using var process = Process.Start(info);
        if (process is null)
            return -1;

        if (!useShell)
        {
            await process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.StandardError.ReadToEndAsync(cancellationToken);
        }

        await process.WaitForExitAsync(cancellationToken);
        return process.ExitCode;
    }
}
