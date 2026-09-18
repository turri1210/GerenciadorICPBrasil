using System.Diagnostics;
using System.ComponentModel;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GerenciadorIcpBrasil.Services;

public sealed class AppUpdateService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly string _manifestUrl;
    private readonly string _cachePath;
    private readonly string _fallbackPath;

    public AppUpdateService(string baseDirectory, string appDataRoot)
    {
        var manifestFileName = UpdateChannel.GetFileName("app-version.json");
        _manifestUrl = UpdateChannel.GetGerenciadorUrl("app-version.json");
        _cachePath = Path.Combine(appDataRoot, BuildCacheFileName(manifestFileName));
        _fallbackPath = Path.Combine(baseDirectory, manifestFileName);
    }

    public string CurrentVersion => typeof(App).Assembly.GetName().Version?.ToString() ?? "0.0.0";

    public async Task<AppUpdateCheckResult> CheckForUpdateAsync()
    {
        string? json = null;
        var source = "indisponivel";

        try
        {
            using var http = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(10),
            };
            json = await http.GetStringAsync(_manifestUrl).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(json))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_cachePath)!);
                await File.WriteAllTextAsync(_cachePath, json).ConfigureAwait(false);
                source = "online";
            }
        }
        catch
        {
            // fallback
        }

        if (string.IsNullOrWhiteSpace(json) && File.Exists(_cachePath))
        {
            json = await File.ReadAllTextAsync(_cachePath).ConfigureAwait(false);
            source = "cache";
        }

        if (string.IsNullOrWhiteSpace(json) && File.Exists(_fallbackPath))
        {
            json = await File.ReadAllTextAsync(_fallbackPath).ConfigureAwait(false);
            source = "local";
        }

        if (string.IsNullOrWhiteSpace(json))
        {
            return AppUpdateCheckResult.NotAvailable(CurrentVersion, source);
        }

        AppUpdateManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<AppUpdateManifest>(json, SerializerOptions);
        }
        catch
        {
            return AppUpdateCheckResult.NotAvailable(CurrentVersion, source);
        }

        if (manifest == null || string.IsNullOrWhiteSpace(manifest.Version))
        {
            return AppUpdateCheckResult.NotAvailable(CurrentVersion, source);
        }

        var hasUpdate = CompareVersions(manifest.Version, CurrentVersion) > 0;
        return new AppUpdateCheckResult(
            hasUpdate,
            CurrentVersion,
            manifest.Version,
            manifest.InstallerUrl ?? manifest.Url ?? string.Empty,
            manifest.Sha256 ?? string.Empty,
            manifest.Notes ?? string.Empty,
            manifest.PublishedAt ?? string.Empty,
            source,
                hasUpdate ? null : "O aplicativo já está atualizado.");
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

        var tempRoot = Path.Combine(Path.GetTempPath(), "gerenciador-icp-brasil", "app-update", update.LatestVersion);
        var installerPath = Path.Combine(tempRoot, $"GerenciadorICPBrasilSetup-{update.LatestVersion}.exe");
        Directory.CreateDirectory(tempRoot);

        try
        {
            using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) })
            {
                using var response = await http.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                await using var networkStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                await using var fileStream = new FileStream(installerPath, FileMode.Create, FileAccess.Write, FileShare.None);
                await networkStream.CopyToAsync(fileStream).ConfigureAwait(false);
            }

            if (IsZipArchive(installerPath))
            {
                return AppUpdateApplyResult.Fail("A URL da atualização aponta para um ZIP. Configure o app-version.json com o instalador (.exe).");
            }

            await ValidateHashAsync(installerPath, update.Sha256).ConfigureAwait(false);
            AuthenticodeVerifier.VerifyOfficialRelease(installerPath);

            var startInfo = new ProcessStartInfo
            {
                FileName = installerPath,
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = tempRoot
            };

            var started = Process.Start(startInfo);

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

    private static string BuildCacheFileName(string fileName)
    {
        var extension = Path.GetExtension(fileName);
        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
        return $"{fileNameWithoutExtension}-cache{extension}";
    }

    private sealed class AppUpdateManifest
    {
        [JsonPropertyName("version")]
        public string? Version { get; set; }

        [JsonPropertyName("installerUrl")]
        public string? InstallerUrl { get; set; }

        [JsonPropertyName("url")]
        public string? Url { get; set; }

        [JsonPropertyName("sha256")]
        public string? Sha256 { get; set; }

        [JsonPropertyName("publishedAt")]
        public string? PublishedAt { get; set; }

        [JsonPropertyName("notes")]
        public string? Notes { get; set; }
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
        => new(false, currentVersion, currentVersion, string.Empty, string.Empty, string.Empty, string.Empty, source, message ?? "Manifesto de atualização indisponível.");
}

public sealed record AppUpdateApplyResult(bool Success, string Message)
{
    public static AppUpdateApplyResult Ok() => new(true, "Atualização iniciada. O aplicativo será fechado para concluir.");
    public static AppUpdateApplyResult Ok(string message) => new(true, message);
    public static AppUpdateApplyResult Fail(string message) => new(false, message);
}
