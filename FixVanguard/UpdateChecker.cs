using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace FixVanguard;

internal enum UpdateStatus
{
    UpToDate,   // versión actual >= latest
    Optional,   // hay nueva pero la vieja sigue soportada
    Required,   // versión actual < min_supported_version → hay que actualizar
    Blocked,    // kill_switch = true → la vieja queda inutilizada
    Failed      // no se pudo comprobar (red caída, JSON roto, etc.)
}

internal sealed class UpdateResult
{
    public UpdateStatus Status { get; init; }
    public UpdateManifest? Manifest { get; init; }
    public Version Current { get; init; } = new(0, 0, 0);
    public Version? Latest { get; init; }
    public string? Error { get; init; }
}

internal static class UpdateChecker
{
    // URL raw del manifest.json en el repo de GitHub.
    public const string ManifestUrl = "https://raw.githubusercontent.com/RaayyDev/fixvanguard/main/manifest.json";

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    public static Version CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);

    public static async Task<UpdateResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        var current = CurrentVersion;

        try
        {
            Http.DefaultRequestHeaders.UserAgent.Clear();
            Http.DefaultRequestHeaders.UserAgent.ParseAdd($"FixVanguard/{current}");

            var json = await Http.GetStringAsync(ManifestUrl, cancellationToken);
            var manifest = JsonSerializer.Deserialize<UpdateManifest>(json)
                           ?? throw new InvalidOperationException("Manifest vacío.");

            var latest = ParseVersion(manifest.LatestVersion);
            var minSupported = ParseVersion(manifest.MinSupportedVersion);

            UpdateStatus status;
            if (manifest.KillSwitch)
                status = UpdateStatus.Blocked;
            else if (current < minSupported)
                status = UpdateStatus.Required;
            else if (current < latest)
                status = UpdateStatus.Optional;
            else
                status = UpdateStatus.UpToDate;

            return new UpdateResult
            {
                Status = status,
                Manifest = manifest,
                Current = current,
                Latest = latest
            };
        }
        catch (Exception ex)
        {
            return new UpdateResult
            {
                Status = UpdateStatus.Failed,
                Current = current,
                Error = ex.Message
            };
        }
    }

    private static Version ParseVersion(string raw) =>
        Version.TryParse(raw, out var v) ? v : new Version(0, 0, 0);
}
