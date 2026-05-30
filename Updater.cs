using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace FlagInjector;

public sealed class UpdateManifest
{
    public string Version { get; set; } = "";
    public string DownloadUrl { get; set; } = "";
    public string? Sha256 { get; set; }
    public string? Notes { get; set; }
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
            if (manifest is null || string.IsNullOrWhiteSpace(manifest.Version) || string.IsNullOrWhiteSpace(manifest.DownloadUrl))
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

    public static async Task<(bool ok, string message)> DownloadAndApplyUpdateAsync(UpdateManifest manifest, CancellationToken ct = default)
    {
        try
        {
            string ext = Path.GetExtension(manifest.DownloadUrl);
            if (!ext.Equals(".exe", StringComparison.OrdinalIgnoreCase) &&
                !ext.Equals(".msi", StringComparison.OrdinalIgnoreCase))
                return (false, "Use an .exe or .msi package URL in manifest.");

            string tempDir = Path.Combine(Path.GetTempPath(), "FlagBrowser", "updates");
            Directory.CreateDirectory(tempDir);

            string installerPath = Path.Combine(tempDir, $"FlagBrowser-{manifest.Version}{ext}");

            using var req = new HttpRequestMessage(HttpMethod.Get, manifest.DownloadUrl);
            using var res = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            res.EnsureSuccessStatusCode();

            await using (var source = await res.Content.ReadAsStreamAsync(ct))
            await using (var dest = File.Create(installerPath))
                await source.CopyToAsync(dest, ct);

            if (!string.IsNullOrWhiteSpace(manifest.Sha256))
            {
                string actual = ComputeSha256(installerPath);
                if (!actual.Equals(NormalizeSha(manifest.Sha256), StringComparison.OrdinalIgnoreCase))
                    return (false, "SHA256 mismatch. Update aborted.");
            }

            if (ext.Equals(".msi", StringComparison.OrdinalIgnoreCase))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "msiexec",
                    Arguments = $"/i \"{installerPath}\"",
                    UseShellExecute = true
                });
                return (true, "MSI installer launched.");
            }

            string currentExe = Environment.ProcessPath
                ?? throw new InvalidOperationException("Cannot resolve current executable path.");
            string targetDir = Path.GetDirectoryName(currentExe)
                ?? throw new InvalidOperationException("Cannot resolve executable directory.");

            string stageExe = Path.Combine(targetDir, "FlagBrowser.update.exe");
            File.Copy(installerPath, stageExe, overwrite: true);

            string scriptPath = Path.Combine(tempDir, $"apply-update-{Guid.NewGuid():N}.cmd");
            string script = $@"@echo off
setlocal
set tries=0
:retry
set /a tries+=1
copy /Y ""{stageExe}"" ""{currentExe}"" > nul
if errorlevel 1 (
  if %tries% geq 15 exit /b 1
  ping 127.0.0.1 -n 2 > nul
  goto retry
)
del /F /Q ""{stageExe}"" > nul 2>&1
start """" ""{currentExe}""
del /F /Q ""%~f0""";
            File.WriteAllText(scriptPath, script);

            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"{scriptPath}\"",
                CreateNoWindow = true,
                UseShellExecute = false
            });
            return (true, "Update staged. App will restart on new version.");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
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

    static string ComputeSha256(string path)
    {
        using var sha = SHA256.Create();
        using var fs = File.OpenRead(path);
        return Convert.ToHexString(sha.ComputeHash(fs));
    }

    static string NormalizeSha(string sha) =>
        sha.Replace(" ", "").Replace("-", "").Trim().ToUpperInvariant();

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
