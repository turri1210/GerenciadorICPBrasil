using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GerenciadorIcpBrasil.Modules.InstallerLauncher.Services;

/// <summary>
/// Mantém uma cópia local da licença biométrica para o ciclo de reinstalação.
/// O conteúdo é protegido pelo Windows para o usuário atual e nunca é enviado pela rede.
/// </summary>
public sealed class BiometricLicenseBackupService
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("GerenciadorICPBrasil.BiometricLicenseBackup.v1");
    private static readonly byte[] LegacyEntropy = Encoding.UTF8.GetBytes("AssistenteICP.BiometricLicenseBackup.v1");

    private static readonly string[] CandidateDirectories =
    {
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Certibio", "BiometricLocalServicePlataform"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Certibio", "BiometricLocalServicePlatform"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "BiometricLocalServicePlataform"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "BiometricLocalServicePlatform"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BiometricLocalServicePlataform"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BiometricLocalServicePlatform"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BiometricLocalServicePlataform"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BiometricLocalServicePlatform"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Biometric Local Service Plataform"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Biometric Local Service Platform"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Biometric Local Service Plataform"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Biometric Local Service Platform"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Biometric Local Service Plataform"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Biometric Local Service Platform"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Innovatrics")
    };

    private static readonly string[] KnownLicenseFilePaths =
    {
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Innovatrics", "iengine.lic")
    };

    private static readonly string[] LicenseFileTokens = { "license", "licenca", "licença", "lic" };

    private static readonly HashSet<string> AllowedLicenseExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".lic", ".key", ".dat", ".json", ".xml", ".txt", ".config"
    };

    private readonly string _backupPath;

    public BiometricLicenseBackupService()
    {
        var backupDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Gerenciador ICP Brasil",
            "backups");
        _backupPath = Path.Combine(backupDirectory, "biometric-license.json");
    }

    public async Task<BackupResult> BackupLocalAsync(CancellationToken cancellationToken = default)
    {
        var detectedPath = TryGetDetectedLicenseFilePath();
        if (string.IsNullOrWhiteSpace(detectedPath) || !File.Exists(detectedPath))
        {
            return new BackupResult(false, "Nenhum arquivo de licença foi encontrado para backup.");
        }

        try
        {
            var bytes = await File.ReadAllBytesAsync(detectedPath, cancellationToken).ConfigureAwait(false);
            var protectedBytes = ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser);
            var envelope = new BackupEnvelope
            {
                Version = 1,
                FileName = Path.GetFileName(detectedPath),
                ProtectedContentBase64 = Convert.ToBase64String(protectedBytes),
                OriginalSha256 = Convert.ToHexString(SHA256.HashData(bytes)),
                CreatedAtUtc = DateTimeOffset.UtcNow
            };

            Directory.CreateDirectory(Path.GetDirectoryName(_backupPath)!);
            var json = JsonSerializer.Serialize(envelope, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_backupPath, json, cancellationToken).ConfigureAwait(false);
            return new BackupResult(true, "Backup local protegido realizado com sucesso.");
        }
        catch (Exception ex)
        {
            return new BackupResult(false, $"Falha ao realizar backup local: {ex.Message}");
        }
    }

    public async Task<RestoreResult> RestoreLocalAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_backupPath))
        {
            return new RestoreResult(false, "Nenhum backup local foi encontrado para este usuário do Windows.");
        }

        try
        {
            var json = await File.ReadAllTextAsync(_backupPath, cancellationToken).ConfigureAwait(false);
            var envelope = JsonSerializer.Deserialize<BackupEnvelope>(json);
            if (envelope is null || envelope.Version != 1 || string.IsNullOrWhiteSpace(envelope.ProtectedContentBase64))
            {
                return new RestoreResult(false, "O backup local está inválido.");
            }

            var protectedBytes = Convert.FromBase64String(envelope.ProtectedContentBase64);
            var bytes = UnprotectCompatibleBackup(protectedBytes);
            var actualHash = Convert.ToHexString(SHA256.HashData(bytes));
            if (!actualHash.Equals(envelope.OriginalSha256, StringComparison.OrdinalIgnoreCase))
            {
                return new RestoreResult(false, "Falha de integridade: checksum SHA256 divergente.");
            }

            var targetPath = ResolveRestoreTargetPath(envelope.FileName);
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            await File.WriteAllBytesAsync(targetPath, bytes, cancellationToken).ConfigureAwait(false);
            return new RestoreResult(true, $"Backup restaurado com sucesso em: {targetPath}");
        }
        catch (CryptographicException)
        {
            return new RestoreResult(false, "O backup pertence a outro usuário ou instalação do Windows e não pôde ser descriptografado.");
        }
        catch (Exception ex)
        {
            return new RestoreResult(false, $"Falha ao restaurar backup local: {ex.Message}");
        }
    }

    public bool HasLocalBackup() => File.Exists(_backupPath);

    private static byte[] UnprotectCompatibleBackup(byte[] protectedBytes)
    {
        try
        {
            return ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
        }
        catch (CryptographicException)
        {
            return ProtectedData.Unprotect(protectedBytes, LegacyEntropy, DataProtectionScope.CurrentUser);
        }
    }

    public bool IsLicenseInstalled()
    {
        try
        {
            return KnownLicenseFilePaths.Any(File.Exists)
                || CandidateDirectories.Distinct(StringComparer.OrdinalIgnoreCase)
                    .Any(directory => Directory.Exists(directory) && EnumerateLikelyLicenseFiles(directory).Any());
        }
        catch
        {
            return false;
        }
    }

    public string? TryGetDetectedLicenseFilePath()
    {
        try
        {
            var explicitPath = KnownLicenseFilePaths.FirstOrDefault(File.Exists);
            if (!string.IsNullOrWhiteSpace(explicitPath))
            {
                return explicitPath;
            }

            return CandidateDirectories
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(Directory.Exists)
                .SelectMany(EnumerateLikelyLicenseFiles)
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    public DateTime? TryGetDetectedLicenseLastWriteTime()
    {
        var path = TryGetDetectedLicenseFilePath();
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            return File.GetLastWriteTime(path);
        }
        catch
        {
            return null;
        }
    }

    private static IEnumerable<string> EnumerateLikelyLicenseFiles(string root)
    {
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var extension = Path.GetExtension(file);
            var normalizedName = Path.GetFileName(file).ToLowerInvariant();
            if (AllowedLicenseExtensions.Contains(extension)
                && (extension.Equals(".lic", StringComparison.OrdinalIgnoreCase)
                    || extension.Equals(".key", StringComparison.OrdinalIgnoreCase)
                    || LicenseFileTokens.Any(normalizedName.Contains)))
            {
                yield return file;
            }
        }
    }

    private static string ResolveRestoreTargetPath(string? fileName)
    {
        var defaultPath = KnownLicenseFilePaths.First();
        if (!string.IsNullOrWhiteSpace(defaultPath))
        {
            return defaultPath;
        }

        var safeName = string.IsNullOrWhiteSpace(fileName) ? "biometric-license.lic" : Path.GetFileName(fileName);
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Innovatrics", safeName);
    }

    public sealed record BackupResult(bool Success, string Message);
    public sealed record RestoreResult(bool Success, string Message);

    private sealed class BackupEnvelope
    {
        public int Version { get; set; }
        public string FileName { get; set; } = "biometric-license.lic";
        public string ProtectedContentBase64 { get; set; } = string.Empty;
        public string OriginalSha256 { get; set; } = string.Empty;
        public DateTimeOffset CreatedAtUtc { get; set; }
    }
}
