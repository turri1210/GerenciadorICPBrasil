using System.Diagnostics;
using System.ComponentModel;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GerenciadorIcpBrasil.Services;

public sealed class AppUpdateService
{
    private const string Repository = "turri1210/GerenciadorICPBrasil";
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public string CurrentVersion => typeof(App).Assembly.GetName().Version?.ToString() ?? "0.0.0";

    public async Task<AppUpdateCheckResult> CheckForUpdateAsync()
    {
        try
        {
            using var http = CreateGitHubClient(TimeSpan.FromSeconds(15));
            var endpoint = UpdateChannel.IsBetaMode
                ? $"https://api.github.com/repos/{Repository}/releases?per_page=20"
                : $"https://api.github.com/repos/{Repository}/releases/latest";
            using var response = await http.GetAsync(endpoint).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return AppUpdateCheckResult.NotAvailable(CurrentVersion, "GitHub",
                    $"Não foi possível consultar as releases no GitHub (HTTP {(int)response.StatusCode}). Verifique se o repositório está público.");
            }

            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            GitHubRelease? release;
            if (UpdateChannel.IsBetaMode)
            {
                release = JsonSerializer.Deserialize<List<GitHubRelease>>(json, SerializerOptions)?
                    .Where(item => !item.Draft)
                    .OrderByDescending(item => item.PublishedAt)
                    .FirstOrDefault();
            }
            else
            {
                release = JsonSerializer.Deserialize<GitHubRelease>(json, SerializerOptions);
            }

            if (release == null || release.Draft || !TryGetReleaseVersion(release.TagName, out var version))
            {
                return AppUpdateCheckResult.NotAvailable(CurrentVersion, "GitHub", "Release válida não encontrada no GitHub.");
            }

            var installerName = $"GerenciadorICPBrasilSetup-{version}.exe";
            var asset = release.Assets?.SingleOrDefault(item =>
                string.Equals(item.Name, installerName, StringComparison.OrdinalIgnoreCase));
            if (asset == null || !TryGetSha256(asset.Digest, out var sha256) ||
                !IsOfficialAssetApiUrl(asset.Url))
            {
                return AppUpdateCheckResult.NotAvailable(CurrentVersion, "GitHub",
                    "A release não contém o instalador oficial com digest SHA-256 válido.");
            }

            var hasUpdate = CompareVersions(version, CurrentVersion) > 0;
            return new AppUpdateCheckResult(hasUpdate, CurrentVersion, version, asset.Url!, sha256,
                release.Body ?? string.Empty, release.PublishedAt ?? string.Empty, "GitHub",
                hasUpdate ? null : "O aplicativo já está atualizado.");
        }
        catch (Exception ex)
        {
            return AppUpdateCheckResult.NotAvailable(CurrentVersion, "GitHub",
                $"Não foi possível consultar o GitHub: {ex.Message}");
        }
    }

    private static HttpClient CreateGitHubClient(TimeSpan timeout)
    {
        var http = new HttpClient { Timeout = timeout };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("GerenciadorICPBrasil-Updater");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return http;
    }

    private static bool TryGetReleaseVersion(string? tag, out string version)
    {
        version = string.Empty;
        if (string.IsNullOrWhiteSpace(tag) || tag[0] != 'v' ||
            !Version.TryParse(tag[1..], out var parsed) || parsed.Build < 0 || parsed.Revision >= 0)
        {
            return false;
        }
        version = tag[1..];
        return true;
    }

    private static bool TryGetSha256(string? digest, out string sha256)
    {
        sha256 = string.Empty;
        if (digest == null || !digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)) return false;
        var value = digest[7..];
        if (value.Length != 64 || value.Any(character => !Uri.IsHexDigit(character))) return false;
        sha256 = value;
        return true;
    }

    private static bool IsOfficialAssetApiUrl(string? value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
           uri.Scheme == Uri.UriSchemeHttps && uri.Host == "api.github.com" && uri.Port == 443 &&
           uri.AbsolutePath.StartsWith($"/repos/{Repository}/releases/assets/", StringComparison.Ordinal) &&
           long.TryParse(uri.Segments.LastOrDefault(), out _) &&
           string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment) &&
           string.IsNullOrEmpty(uri.UserInfo);

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")] public string? TagName { get; set; }
        [JsonPropertyName("draft")] public bool Draft { get; set; }
        [JsonPropertyName("body")] public string? Body { get; set; }
        [JsonPropertyName("published_at")] public string? PublishedAt { get; set; }
        [JsonPropertyName("assets")] public List<GitHubAsset>? Assets { get; set; }
    }

    private sealed class GitHubAsset
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("url")] public string? Url { get; set; }
        [JsonPropertyName("digest")] public string? Digest { get; set; }
    }

    public async Task<AppUpdateApplyResult> StartUpdateAsync(AppUpdateCheckResult update)
    {
        if (!update.HasUpdate)
        {
            return AppUpdateApplyResult.Fail("Nenhuma atualização disponível.");
        }

        if (string.IsNullOrWhiteSpace(update.DownloadUrl))
        {
            return AppUpdateApplyResult.Fail("A URL da atualização não foi informada.");
        }

        ValidateDownloadUri(update.DownloadUrl);

        var tempRoot = Path.Combine(Path.GetTempPath(), "gerenciador-icp-brasil", "app-update", update.LatestVersion);
        var installerPath = Path.Combine(tempRoot, $"GerenciadorICPBrasilSetup-{update.LatestVersion}.exe");
        Directory.CreateDirectory(tempRoot);

        try
        {
            using (var http = CreateGitHubClient(TimeSpan.FromMinutes(10)))
            {
                http.DefaultRequestHeaders.Accept.Clear();
                http.DefaultRequestHeaders.Accept.ParseAdd("application/octet-stream");
                using var response = await http.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                var finalUri = response.RequestMessage?.RequestUri;
                if (finalUri?.Scheme != Uri.UriSchemeHttps ||
                    !(finalUri.Host.Equals("api.github.com", StringComparison.OrdinalIgnoreCase) ||
                      finalUri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
                      finalUri.Host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase)))
                {
                    throw new InvalidOperationException("O download saiu dos endereços HTTPS do GitHub.");
                }

                await using var networkStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                await using var fileStream = new FileStream(installerPath, FileMode.Create, FileAccess.Write, FileShare.None);
                await networkStream.CopyToAsync(fileStream).ConfigureAwait(false);
            }

            using var executionLock = new FileStream(installerPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (IsZipArchive(installerPath))
            {
                return AppUpdateApplyResult.Fail("A release aponta para um ZIP em vez do instalador (.exe).");
            }

            await ValidateHashAsync(installerPath, update.Sha256).ConfigureAwait(false);
            AuthenticodeVerifier.VerifyOfficialRelease(installerPath);
            VerifyInstallerProduct(installerPath, update.LatestVersion);

            var started = Process.Start(new ProcessStartInfo
            {
                FileName = installerPath,
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = tempRoot
            });

            if (started == null)
            {
                return AppUpdateApplyResult.Fail("Falha ao iniciar o instalador da nova versão.");
            }

            return AppUpdateApplyResult.Ok("Instalador iniciado. O aplicativo será fechado para continuar a atualização.");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return AppUpdateApplyResult.Fail("Atualização cancelada pelo usuário (UAC).");
        }
        catch (Exception ex)
        {
            return AppUpdateApplyResult.Fail($"Falha ao baixar ou executar o instalador: {ex.Message}");
        }
    }

    private static bool IsZipArchive(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return false;
        }

        Span<byte> signature = stackalloc byte[4];
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length < signature.Length)
        {
            return false;
        }

        var read = stream.Read(signature);
        return read == 4
            && signature[0] == (byte)'P'
            && signature[1] == (byte)'K'
            && signature[2] == 0x03
            && signature[3] == 0x04;
    }

    private static void ValidateDownloadUri(string? value)
    {
        if (!IsOfficialAssetApiUrl(value))
        {
            throw new InvalidOperationException("O instalador não pertence à release oficial no GitHub.");
        }
    }

    private static void VerifyInstallerProduct(string filePath, string expectedVersion)
    {
        var versionInfo = FileVersionInfo.GetVersionInfo(filePath);
        if (!string.Equals(versionInfo.ProductName?.Trim(), "Gerenciador ICP Brasil", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("O instalador não pertence ao Gerenciador ICP Brasil.");
        }
        if (!string.Equals(versionInfo.ProductVersion?.Trim(), expectedVersion, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("A versão do instalador não corresponde à release.");
        }
    }

    private static async Task ValidateHashAsync(string filePath, string? expectedHash)
    {
        if (string.IsNullOrWhiteSpace(expectedHash))
        {
            throw new InvalidOperationException("A atualização não informa um hash SHA256.");
        }

        var expected = expectedHash.Replace(" ", string.Empty, StringComparison.Ordinal).Trim().ToUpperInvariant();
        if (expected.Length != 64 || expected.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new InvalidOperationException("O hash SHA256 informado para a atualização é inválido.");
        }
        await using var stream = File.OpenRead(filePath);
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(stream).ConfigureAwait(false);
        var actual = Convert.ToHexString(hash);
        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("O hash do pacote de atualização não confere.");
        }
    }

    private static int CompareVersions(string? a, string? b)
    {
        var aParts = (a ?? "0").Split('.').Select(ParsePart).ToArray();
        var bParts = (b ?? "0").Split('.').Select(ParsePart).ToArray();
        var len = Math.Max(aParts.Length, bParts.Length);
        for (var i = 0; i < len; i++)
        {
            var av = i < aParts.Length ? aParts[i] : 0;
            var bv = i < bParts.Length ? bParts[i] : 0;
            if (av > bv) return 1;
            if (av < bv) return -1;
        }
        return 0;
    }

    private static int ParsePart(string value)
    {
        var raw = value;
        var dashIndex = raw.IndexOf('-');
        if (dashIndex >= 0)
        {
            raw = raw.Substring(0, dashIndex);
        }

        return int.TryParse(raw, out var number) ? number : 0;
    }

}

public sealed record AppUpdateCheckResult(
    bool HasUpdate,
    string CurrentVersion,
    string LatestVersion,
    string DownloadUrl,
    string Sha256,
    string Notes,
    string PublishedAt,
    string Source,
    string? Message)
{
    public static AppUpdateCheckResult NotAvailable(string currentVersion, string source, string? message = null)
        => new(false, currentVersion, currentVersion, string.Empty, string.Empty, string.Empty, string.Empty, source, message ?? "Release indisponível.");
}

public sealed record AppUpdateApplyResult(bool Success, string Message)
{
    public static AppUpdateApplyResult Ok() => new(true, "Atualização iniciada. O aplicativo será fechado para concluir.");
    public static AppUpdateApplyResult Ok(string message) => new(true, message);
    public static AppUpdateApplyResult Fail(string message) => new(false, message);
}
