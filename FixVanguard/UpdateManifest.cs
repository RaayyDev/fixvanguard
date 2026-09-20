using System.Text.Json.Serialization;

namespace FixVanguard;

// JSON que subes al hosting. Ejemplo:
// {
//   "latest_version": "1.2.0",
//   "min_supported_version": "1.1.0",
//   "download_url": "https://dominio/fixvanguard/FixVanguard.exe",
//   "sha256": "abcd1234...",
//   "notes": "Se corrige X y Y",
//   "kill_switch": false,
//   "message": "Descarga la nueva versión antes de seguir"
// }
internal sealed class UpdateManifest
{
    [JsonPropertyName("latest_version")]
    public string LatestVersion { get; set; } = "0.0.0";

    [JsonPropertyName("min_supported_version")]
    public string MinSupportedVersion { get; set; } = "0.0.0";

    [JsonPropertyName("download_url")]
    public string DownloadUrl { get; set; } = string.Empty;

    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = string.Empty;

    [JsonPropertyName("notes")]
    public string? Notes { get; set; }

    [JsonPropertyName("kill_switch")]
    public bool KillSwitch { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("discord_url")]
    public string? DiscordUrl { get; set; }
}
