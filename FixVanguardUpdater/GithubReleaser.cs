using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FixVanguard;

namespace FixVanguardUpdater;

// Cliente minimo de la GitHub REST API para releases + contents.
// Se autentica con un Personal Access Token (fine-grained o classic con scope 'repo').
internal sealed class GithubReleaser
{
    private const string ApiBase = "https://api.github.com";
    private const string UploadsBase = "https://uploads.github.com";
    private const string ApiVersion = "2022-11-28";

    private readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromMinutes(10)
    };
    private readonly IProgress<ActionLog> _log;

    public GithubReleaser(string token, IProgress<ActionLog> log)
    {
        _log = log;
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("FixVanguardUpdater/1.0");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        _http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", ApiVersion);
    }

    // Sube todo: crea/obtiene release, sube asset, publica manifest.json en el repo.
    public async Task<Uri> PublishAsync(PublishRequest req, CancellationToken ct = default)
    {
        _log.Report(new($"Autenticando en GitHub como owner={req.Owner}, repo={req.Repo}", ActionLevel.Info));

        // 1) Comprobar permisos con GET /repos/{owner}/{repo}
        await EnsureRepoAccessAsync(req.Owner, req.Repo, ct);

        // 2) SHA-256 del binario nuevo
        var sha256 = await ComputeSha256Async(req.ExePath, ct);
        _log.Report(new($"SHA-256 del .exe: {sha256}", ActionLevel.Info));

        // 3) Crear u obtener el release por tag
        var tag = $"v{req.Version}";
        var release = await GetReleaseByTagAsync(req.Owner, req.Repo, tag, ct)
                      ?? await CreateReleaseAsync(req.Owner, req.Repo, tag, req.Version, req.Notes, ct);

        // 4) Borrar asset previo con el mismo nombre si existe, para poder subir el nuevo
        await DeleteExistingAssetAsync(req.Owner, req.Repo, release.Id, req.AssetName, ct);

        // 5) Subir el .exe como asset
        var assetUrl = await UploadAssetAsync(release.UploadUrl, req.ExePath, req.AssetName, ct);
        _log.Report(new($"Asset subido: {assetUrl}", ActionLevel.Success));

        // 6) Construir manifest.json y publicarlo en la rama principal del repo
        var manifest = new UpdateManifest
        {
            LatestVersion = req.Version,
            MinSupportedVersion = string.IsNullOrWhiteSpace(req.MinSupportedVersion) ? req.Version : req.MinSupportedVersion,
            DownloadUrl = assetUrl.ToString(),
            Sha256 = sha256,
            Notes = string.IsNullOrWhiteSpace(req.Notes) ? null : req.Notes,
            KillSwitch = req.KillSwitch,
            Message = string.IsNullOrWhiteSpace(req.Message) ? null : req.Message,
            DiscordUrl = string.IsNullOrWhiteSpace(req.DiscordUrl) ? null : req.DiscordUrl
        };

        var manifestJson = JsonSerializer.Serialize(manifest,
            new JsonSerializerOptions { WriteIndented = true });

        var manifestCommit = $"Publish FixVanguard {req.Version}";
        var rawUrl = await PutContentAsync(req.Owner, req.Repo, req.Branch, req.ManifestPath,
                                            manifestJson, manifestCommit, ct);
        _log.Report(new($"manifest.json publicado: {rawUrl}", ActionLevel.Success));
        return rawUrl;
    }

    // ---------- HTTP helpers ----------

    private async Task EnsureRepoAccessAsync(string owner, string repo, CancellationToken ct)
    {
        using var resp = await _http.GetAsync($"{ApiBase}/repos/{owner}/{repo}", ct);
        if (resp.StatusCode == HttpStatusCode.NotFound)
            throw new InvalidOperationException($"Repo {owner}/{repo} no existe o el token no tiene acceso.");
        if (resp.StatusCode == HttpStatusCode.Unauthorized)
            throw new InvalidOperationException("Token de GitHub inválido o caducado.");
        resp.EnsureSuccessStatusCode();
    }

    private async Task<ReleaseInfo?> GetReleaseByTagAsync(string owner, string repo, string tag, CancellationToken ct)
    {
        using var resp = await _http.GetAsync($"{ApiBase}/repos/{owner}/{repo}/releases/tags/{tag}", ct);
        if (resp.StatusCode == HttpStatusCode.NotFound) return null;
        resp.EnsureSuccessStatusCode();
        var raw = await resp.Content.ReadFromJsonAsync<RawRelease>(cancellationToken: ct)
                  ?? throw new InvalidOperationException("Respuesta vacia al leer release.");
        _log.Report(new($"Release {tag} ya existía (id={raw.Id}).", ActionLevel.Info));
        return raw.ToInfo();
    }

    private async Task<ReleaseInfo> CreateReleaseAsync(string owner, string repo, string tag, string name, string body, CancellationToken ct)
    {
        var payload = new
        {
            tag_name = tag,
            name = $"FixVanguard {name}",
            body,
            draft = false,
            prerelease = false
        };
        using var resp = await _http.PostAsJsonAsync($"{ApiBase}/repos/{owner}/{repo}/releases", payload, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var err = await resp.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"No pude crear el release: {(int)resp.StatusCode} {err}");
        }
        var raw = await resp.Content.ReadFromJsonAsync<RawRelease>(cancellationToken: ct)
                  ?? throw new InvalidOperationException("Respuesta vacia al crear release.");
        _log.Report(new($"Release {tag} creado (id={raw.Id}).", ActionLevel.Success));
        return raw.ToInfo();
    }

    private async Task DeleteExistingAssetAsync(string owner, string repo, long releaseId, string assetName, CancellationToken ct)
    {
        using var resp = await _http.GetAsync($"{ApiBase}/repos/{owner}/{repo}/releases/{releaseId}/assets?per_page=100", ct);
        resp.EnsureSuccessStatusCode();
        var assets = await resp.Content.ReadFromJsonAsync<RawAsset[]>(cancellationToken: ct);
        if (assets is null) return;
        foreach (var a in assets)
        {
            if (!string.Equals(a.Name, assetName, StringComparison.OrdinalIgnoreCase)) continue;
            using var del = await _http.DeleteAsync($"{ApiBase}/repos/{owner}/{repo}/releases/assets/{a.Id}", ct);
            del.EnsureSuccessStatusCode();
            _log.Report(new($"Asset previo borrado: {a.Name} (id={a.Id}).", ActionLevel.Info));
        }
    }

    private async Task<Uri> UploadAssetAsync(string uploadUrlTemplate, string filePath, string assetName, CancellationToken ct)
    {
        // uploadUrlTemplate es de la forma "https://uploads.github.com/repos/o/r/releases/123/assets{?name,label}"
        var cleanTemplate = uploadUrlTemplate;
        var braceIdx = cleanTemplate.IndexOf('{');
        if (braceIdx > 0) cleanTemplate = cleanTemplate[..braceIdx];
        if (!cleanTemplate.StartsWith(UploadsBase, StringComparison.OrdinalIgnoreCase))
            cleanTemplate = UploadsBase + new Uri(cleanTemplate).PathAndQuery;

        var uri = $"{cleanTemplate}?name={Uri.EscapeDataString(assetName)}";

        _log.Report(new($"Subiendo {new FileInfo(filePath).Length / 1024 / 1024} MB → {assetName}", ActionLevel.Info));

        await using var fs = File.OpenRead(filePath);
        using var content = new StreamContent(fs);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        content.Headers.ContentLength = fs.Length;

        using var req = new HttpRequestMessage(HttpMethod.Post, uri) { Content = content };
        using var resp = await _http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var err = await resp.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"Falló la subida del asset: {(int)resp.StatusCode} {err}");
        }
        var raw = await resp.Content.ReadFromJsonAsync<RawAsset>(cancellationToken: ct)
                  ?? throw new InvalidOperationException("Respuesta vacia tras subir asset.");
        return new Uri(raw.BrowserDownloadUrl);
    }

    private async Task<Uri> PutContentAsync(string owner, string repo, string branch, string path,
                                            string text, string commitMessage, CancellationToken ct)
    {
        // Primero obtenemos el SHA existente (si lo hay) para actualizar en vez de intentar crear.
        string? existingSha = null;
        var getUri = $"{ApiBase}/repos/{owner}/{repo}/contents/{path}?ref={Uri.EscapeDataString(branch)}";
        using (var getResp = await _http.GetAsync(getUri, ct))
        {
            if (getResp.IsSuccessStatusCode)
            {
                var existing = await getResp.Content.ReadFromJsonAsync<RawContent>(cancellationToken: ct);
                existingSha = existing?.Sha;
            }
            else if (getResp.StatusCode != HttpStatusCode.NotFound)
            {
                getResp.EnsureSuccessStatusCode();
            }
        }

        var payload = new Dictionary<string, object?>
        {
            ["message"] = commitMessage,
            ["content"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(text)),
            ["branch"] = branch
        };
        if (existingSha is not null)
            payload["sha"] = existingSha;

        using var resp = await _http.PutAsJsonAsync($"{ApiBase}/repos/{owner}/{repo}/contents/{path}", payload, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var err = await resp.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"No pude escribir {path}: {(int)resp.StatusCode} {err}");
        }
        var body = await resp.Content.ReadFromJsonAsync<RawContentEnvelope>(cancellationToken: ct);
        var rawUrl = body?.Content?.DownloadUrl
                     ?? $"https://raw.githubusercontent.com/{owner}/{repo}/{branch}/{path}";
        return new Uri(rawUrl);
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken ct)
    {
        await using var fs = File.OpenRead(path);
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(fs, ct);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    // ---------- DTOs ----------

    public sealed record PublishRequest(
        string ExePath,
        string Version,
        string MinSupportedVersion,
        string Notes,
        bool KillSwitch,
        string DiscordUrl,
        string Message,
        string Owner,
        string Repo,
        string Branch,
        string ManifestPath,
        string AssetName);

    public sealed record ReleaseInfo(long Id, string UploadUrl, string HtmlUrl);

    private sealed class RawRelease
    {
        [JsonPropertyName("id")]         public long Id { get; set; }
        [JsonPropertyName("upload_url")] public string UploadUrl { get; set; } = string.Empty;
        [JsonPropertyName("html_url")]   public string HtmlUrl { get; set; } = string.Empty;
        public ReleaseInfo ToInfo() => new(Id, UploadUrl, HtmlUrl);
    }

    private sealed class RawAsset
    {
        [JsonPropertyName("id")]                   public long Id { get; set; }
        [JsonPropertyName("name")]                 public string Name { get; set; } = string.Empty;
        [JsonPropertyName("browser_download_url")] public string BrowserDownloadUrl { get; set; } = string.Empty;
    }

    private sealed class RawContent
    {
        [JsonPropertyName("sha")]          public string? Sha { get; set; }
        [JsonPropertyName("download_url")] public string? DownloadUrl { get; set; }
    }

    private sealed class RawContentEnvelope
    {
        [JsonPropertyName("content")] public RawContent? Content { get; set; }
    }
}
