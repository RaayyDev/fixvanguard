using System.Text.Json;
using System.Text.Json.Serialization;

namespace FixVanguardUpdater;

// Persistimos todos los campos del formulario en %APPDATA%\FixVanguardUpdater\settings.json
// asi Sam no tiene que reescribir owner/repo/token cada vez que sube una version.
internal sealed class Settings
{
    [JsonPropertyName("exe_path")]              public string ExePath { get; set; } = string.Empty;
    [JsonPropertyName("version")]               public string Version { get; set; } = "1.0.0";
    [JsonPropertyName("min_supported_version")] public string MinSupportedVersion { get; set; } = "1.0.0";
    [JsonPropertyName("notes")]                 public string Notes { get; set; } = string.Empty;
    [JsonPropertyName("kill_switch")]           public bool KillSwitch { get; set; }
    [JsonPropertyName("discord_url")]           public string DiscordUrl { get; set; } = string.Empty;
    [JsonPropertyName("message")]               public string Message { get; set; } = string.Empty;
    [JsonPropertyName("owner")]                 public string Owner { get; set; } = string.Empty;
    [JsonPropertyName("repo")]                  public string Repo { get; set; } = string.Empty;
    [JsonPropertyName("branch")]                public string Branch { get; set; } = "main";
    [JsonPropertyName("manifest_path")]         public string ManifestPath { get; set; } = "manifest.json";
    [JsonPropertyName("asset_name")]            public string AssetName { get; set; } = "FixVanguard.exe";
    [JsonPropertyName("token")]                 public string Token { get; set; } = string.Empty;

    private static string SettingsFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "FixVanguardUpdater", "settings.json");

    public static Settings Load()
    {
        try
        {
            if (!File.Exists(SettingsFile)) return new Settings();
            var json = File.ReadAllText(SettingsFile);
            return JsonSerializer.Deserialize<Settings>(json) ?? new Settings();
        }
        catch
        {
            return new Settings();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile)!);
            var opts = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(SettingsFile, JsonSerializer.Serialize(this, opts));
        }
        catch
        {
            // No pasa nada si no se puede guardar; solo pierde persistencia.
        }
    }
}
