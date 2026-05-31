using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FlagInjector;

public sealed class UpdateManifest
{
    public string Version { get; set; } = "";
    public string Url { get; set; } = "";
    public string DownloadUrl { get; set; } = "";
    public string? Sha256 { get; set; }
    public string? Notes { get; set; }
    [JsonIgnore]
    public string ResolvedUrl => string.IsNullOrWhiteSpace(DownloadUrl) ? Url : DownloadUrl;
}

public sealed class UpdateCheckResult
{
    public bool IsUpdateAvailable { get; init; }
    public Version CurrentVersion { get; init; } = new(0, 0, 0, 0);
    public Version LatestVersion { get; init; } = new(0, 0, 0, 0);
    public UpdateManifest? Manifest { get; init; }
    public string? Error { get; init; }
}

public static class Updater
{
    static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };
    static readonly JsonSerializerOptions _jsonOpts = new() { PropertyNameCaseInsensitive = true };

    public static bool IsManifestConfigured(string manifestUrl)
    {
        manifestUrl = NormalizeManifestUrl(manifestUrl);
        return Uri.TryCreate(manifestUrl, UriKind.Absolute, out var u)
            && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps)
            && !manifestUrl.Contains("example.com", StringComparison.OrdinalIgnoreCase);
    }

    public static async Task<UpdateCheckResult> CheckAsync(string manifestUrl, CancellationToken ct = default)
    {
        Version current = GetCurrentVersion();
        manifestUrl = NormalizeManifestUrl(manifestUrl);

        try
        {
            if (!IsManifestConfigured(manifestUrl))
                return new UpdateCheckResult
                {
                    CurrentVersion = current,
                    LatestVersion = current,
                    Error = "Manifest URL is not configured."
                };

            using var req = new HttpRequestMessage(HttpMethod.Get, manifestUrl);
            using var res = await _http.SendAsync(req, ct);
            res.EnsureSuccessStatusCode();

            string json = await res.Content.ReadAsStringAsync(ct);
            var manifest = JsonSerializer.Deserialize<UpdateManifest>(json, _jsonOpts);
            if (manifest is null || string.IsNullOrWhiteSpace(manifest.Version) || string.IsNullOrWhiteSpace(manifest.ResolvedUrl))
                return new UpdateCheckResult
                {
                    CurrentVersion = current,
                    LatestVersion = current,
                    Error = "Invalid manifest format."
                };

            if (!TryParseVersion(manifest.Version, out var latest))
                return new UpdateCheckResult
                {
                    CurrentVersion = current,
                    LatestVersion = current,
                    Error = $"Invalid version in manifest: {manifest.Version}"
                };

            return new UpdateCheckResult
            {
                CurrentVersion = current,
                LatestVersion = latest,
                Manifest = manifest,
                IsUpdateAvailable = latest > current
            };
        }
        catch (Exception ex)
        {
            return new UpdateCheckResult
            {
                CurrentVersion = current,
                LatestVersion = current,
                Error = ex.Message
            };
        }
    }

    public static void OpenDownload(UpdateManifest manifest)
    {
        string url = manifest.ResolvedUrl.Trim();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new InvalidOperationException("Invalid update URL.");
        Process.Start(new ProcessStartInfo { FileName = uri.ToString(), UseShellExecute = true });
    }

    static Version GetCurrentVersion()
    {
        var asm = Assembly.GetExecutingAssembly();
        string? info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(info) && TryParseVersion(info.Split('+')[0], out var fromInfo))
            return fromInfo;
        return asm.GetName().Version ?? new Version(0, 0, 0, 0);
    }

    static bool TryParseVersion(string raw, out Version v)
    {
        raw = raw.Trim();
        if (raw.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            raw = raw[1..];
        return Version.TryParse(raw, out v!);
    }

    static string NormalizeManifestUrl(string raw)
    {
        raw = (raw ?? "").Trim().TrimEnd('.', ';', ',', ' ');
        if (raw.Length == 0) return raw;
        if (!raw.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !raw.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            raw = "https://" + raw.TrimStart('/');
        return raw;
    }
}
