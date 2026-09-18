using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using GerenciadorIcpBrasil.Modules.InstallerLauncher.Models;

namespace GerenciadorIcpBrasil.Modules.InstallerLauncher.Services;

public sealed class InstallerManager
{
    private const string BiometricPlatformPackageId = "bio-platform";
    private static readonly HttpClient HttpClient = new();
    private static readonly Regex JavaDesktopDownloadRegex = new(@"https://javadl\.oracle\.com/webapps/download/AutoDL\?BundleId=[A-Za-z0-9_]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly string[] JavaDesktopIndicators = { "64-bit", "64 bits" };
    private static readonly HashSet<string> KnownPackageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe",
        ".msi",
        ".zip",
        ".msix",
        ".msixbundle",
        ".appx",
        ".appxbundle",
        ".cab",
        ".msu"
    };
    private static readonly Dictionary<string, string> ContentTypeExtensionMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["application/x-msdownload"] = ".exe",
        ["application/vnd.microsoft.portable-executable"] = ".exe",
        ["application/x-msdos-program"] = ".exe",
        ["application/x-msi"] = ".msi",
        ["application/zip"] = ".zip",
        ["application/x-zip-compressed"] = ".zip",
        ["application/vnd.ms-cab-compressed"] = ".cab"
    };

    private readonly string _workingDirectory;
    private readonly InstalledProgramDetector _detector = new();
    private readonly BiometricLicenseBackupService _biometricLicenseBackupService = new();

    public InstallerManager()
    {
        _workingDirectory = Path.Combine(Path.GetTempPath(), "RedeICPLauncher");
        Directory.CreateDirectory(_workingDirectory);
    }

    public InstallerPackageState GetState(InstallerPackage package) => _detector.GetState(package);

    public async Task<bool> InstallAsync(InstallerPackage package, IProgress<DownloadProgressInfo>? progress, CancellationToken cancellationToken)
    {
        var installerPath = await EnsurePackageFileAsync(package, progress, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(installerPath) && string.IsNullOrWhiteSpace(package.AutomaticInstallerPath))
        {
            return false;
        }

        return await ExecuteInstallerAsync(package, installerPath, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> DownloadOnlyAsync(
        InstallerPackage package,
        string targetDirectory,
        IProgress<DownloadProgressInfo>? progress,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(targetDirectory))
        {
            return false;
        }

        Directory.CreateDirectory(targetDirectory);
        var downloadedFilePath = await EnsureManualPackageFileAsync(package, targetDirectory, progress, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(downloadedFilePath) || !File.Exists(downloadedFilePath))
        {
            return false;
        }

        OpenFileDirectory(downloadedFilePath);
        return true;
    }

    public async Task<bool> UninstallAsync(InstallerPackage package, InstallerPackageState state, CancellationToken cancellationToken)
    {
        var uninstall = !string.IsNullOrWhiteSpace(state.QuietUninstallString)
            ? state.QuietUninstallString
            : state.UninstallString;

        if (string.IsNullOrWhiteSpace(uninstall))
        {
            return false;
        }

        var (fileName, args) = SplitCommandLine(uninstall);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        if (fileName.EndsWith("msiexec.exe", StringComparison.OrdinalIgnoreCase) && args != null)
        {
            args = args.Replace("/I", "/X", StringComparison.OrdinalIgnoreCase);
            if (!args.Contains("/quiet", StringComparison.OrdinalIgnoreCase) &&
                !args.Contains("/passive", StringComparison.OrdinalIgnoreCase))
            {
                args += " /quiet";
            }
        }

        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = args ?? string.Empty,
            UseShellExecute = true
        };

        using var process = Process.Start(psi);
        if (process == null)
        {
            return false;
        }

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return process.ExitCode == 0;
    }

    private async Task<string?> EnsurePackageFileAsync(InstallerPackage package, IProgress<DownloadProgressInfo>? progress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (package.Category == InstallerCategory.LeitorA3)
        {
            return await EnsureLeitorPackageAsync(package, progress, cancellationToken).ConfigureAwait(false);
        }

        if (string.Equals(package.Id, "drv-java", StringComparison.OrdinalIgnoreCase))
        {
            return await EnsureJavaRuntimeAsync(package, cancellationToken).ConfigureAwait(false);
        }

        if (!string.IsNullOrWhiteSpace(package.RelativePath))
        {
            var localPath = ResourceResolver.TryGetLocalFile(package.RelativePath);
            if (!string.IsNullOrWhiteSpace(localPath))
            {
                return await ResolveInstallablePathAsync(package, localPath, cancellationToken).ConfigureAwait(false);
            }
        }

        var downloadedPath = await DownloadPackageAsync(package, progress, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(downloadedPath))
        {
            return null;
        }

        return await ResolveInstallablePathAsync(package, downloadedPath, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string?> EnsureManualPackageFileAsync(
        InstallerPackage package,
        string targetDirectory,
        IProgress<DownloadProgressInfo>? progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (package.Category == InstallerCategory.LeitorA3)
        {
            return await DownloadLeitorPackageAsync(package, progress, cancellationToken, targetDirectory).ConfigureAwait(false);
        }

        if (string.Equals(package.Id, "drv-java", StringComparison.OrdinalIgnoreCase))
        {
            return await EnsureJavaRuntimeAsync(package, cancellationToken, targetDirectory).ConfigureAwait(false);
        }

        if (!string.IsNullOrWhiteSpace(package.RelativePath))
        {
            var localPath = ResourceResolver.TryGetLocalFile(package.RelativePath);
            if (!string.IsNullOrWhiteSpace(localPath) && File.Exists(localPath))
            {
                var destination = BuildManualCopyDestination(targetDirectory, localPath, package.Id);
                File.Copy(localPath, destination, overwrite: true);
                return destination;
            }
        }

        return await DownloadPackageAsync(package, progress, cancellationToken, targetDirectory).ConfigureAwait(false);
    }

    private async Task<string?> EnsureLeitorPackageAsync(InstallerPackage package, IProgress<DownloadProgressInfo>? progress, CancellationToken cancellationToken)
    {
        var manifestPath = ResourceResolver.TryGetLocalFile(package.RelativePath);
        if (!string.IsNullOrWhiteSpace(manifestPath) && File.Exists(manifestPath))
        {
            try
            {
                var manifestContent = await File.ReadAllTextAsync(manifestPath, cancellationToken);
                var manifestInfo = ParseLeitorManifest(manifestContent);
                if (manifestInfo != null)
                {
                    var manifestDir = Path.GetDirectoryName(manifestPath)!;
                    var localInstaller = Path.Combine(manifestDir, manifestInfo.FileName);
                    if (File.Exists(localInstaller))
                    {
                        return localInstaller;
                    }
                }
            }
            catch
            {
                // fall back to download
            }
        }

        return await DownloadLeitorPackageAsync(package, progress, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string?> DownloadLeitorPackageAsync(
        InstallerPackage package,
        IProgress<DownloadProgressInfo>? progress,
        CancellationToken cancellationToken,
        string? destinationDirectory = null)
    {
        if (package.DownloadUri is null)
        {
            return null;
        }

        var manifestContent = await HttpClient.GetStringAsync(package.DownloadUri, cancellationToken);
        var manifestInfo = ParseLeitorManifest(manifestContent);
        if (manifestInfo == null)
        {
            return null;
        }

        var downloadUri = new Uri(package.DownloadUri, manifestInfo.FileName);
        var targetDirectory = destinationDirectory ?? _workingDirectory;
        var destination = Path.Combine(targetDirectory, manifestInfo.FileName);
        return await DownloadFileAsync(downloadUri, destination, manifestInfo.Sha512, progress, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string?> EnsureJavaRuntimeAsync(
        InstallerPackage package,
        CancellationToken cancellationToken,
        string? destinationDirectory = null)
    {
        foreach (var uri in InstallerCatalogService.GetJavaManualUris())
        {
            var content = await DownloadJavaManualPageAsync(uri, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(content))
            {
                continue;
            }

            var downloadUri = TryExtractJavaDesktopUri(content);
            if (downloadUri != null)
            {
                var targetDirectory = destinationDirectory ?? _workingDirectory;
                var destination = Path.Combine(targetDirectory, "java-runtime.exe");
                return await DownloadFileAsync(downloadUri, destination, null, null, cancellationToken).ConfigureAwait(false);
            }
        }

        return null;
    }

    private async Task<string?> DownloadJavaManualPageAsync(Uri manualUri, CancellationToken cancellationToken)
    {
        var content = await TryGetStringAsync(manualUri, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(content))
        {
            return content;
        }

        if (!manualUri.Host.Contains("java.com", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var mirrorUri = new Uri($"https://r.jina.ai/{manualUri}");
        return await TryGetStringAsync(mirrorUri, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string?> TryGetStringAsync(Uri uri, CancellationToken cancellationToken)
    {
        try
        {
            return await HttpClient.GetStringAsync(uri, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private static Uri? TryExtractJavaDesktopUri(string content)
    {
        foreach (Match match in JavaDesktopDownloadRegex.Matches(content))
        {
            if (!match.Success)
            {
                continue;
            }

            var spanStart = Math.Max(0, match.Index - 200);
            var spanLength = Math.Min(content.Length - spanStart, 400);
            var window = content.Substring(spanStart, spanLength);

            if (JavaDesktopIndicators.Any(indicator =>
                    window.IndexOf(indicator, StringComparison.OrdinalIgnoreCase) >= 0))
            {
                if (Uri.TryCreate(match.Value, UriKind.Absolute, out var uri))
                {
                    return uri;
                }
            }
        }

        return null;
    }

    private async Task<string?> DownloadPackageAsync(
        InstallerPackage package,
        IProgress<DownloadProgressInfo>? progress,
        CancellationToken cancellationToken,
        string? destinationDirectory = null)
    {
        if (package.DownloadUri is null)
        {
            return null;
        }

        var targetDirectory = destinationDirectory ?? _workingDirectory;
        var destination = BuildDownloadDestinationPath(package, targetDirectory);
        return await DownloadFileAsync(package.DownloadUri, destination, null, progress, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string?> ResolveInstallablePathAsync(InstallerPackage package, string packagePath, CancellationToken cancellationToken)
    {
        if (!Path.GetExtension(packagePath).Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            return packagePath;
        }

        return await ExtractArchiveInstallerAsync(package, packagePath, cancellationToken).ConfigureAwait(false);
    }

    private Task<string?> ExtractArchiveInstallerAsync(InstallerPackage package, string archivePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var extractDirectory = Path.Combine(_workingDirectory, "extracted", package.Id);
        Directory.CreateDirectory(extractDirectory);
        ZipFile.ExtractToDirectory(archivePath, extractDirectory, overwriteFiles: true);

        string? installerPath = null;
        if (!string.IsNullOrWhiteSpace(package.ArchiveEntryPath))
        {
            installerPath = Path.Combine(extractDirectory, package.ArchiveEntryPath);
            if (!File.Exists(installerPath))
            {
                return Task.FromResult<string?>(null);
            }
        }
        else
        {
            installerPath = Directory
                .EnumerateFiles(extractDirectory, "*", SearchOption.AllDirectories)
                .FirstOrDefault(path =>
                {
                    var extension = Path.GetExtension(path);
                    return extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
                           extension.Equals(".msi", StringComparison.OrdinalIgnoreCase);
                });

            if (string.IsNullOrWhiteSpace(installerPath))
            {
                return Task.FromResult<string?>(null);
            }
        }

        return Task.FromResult<string?>(installerPath);
    }

    private async Task<string?> DownloadFileAsync(Uri uri, string destinationPath, string? expectedSha512, IProgress<DownloadProgressInfo>? progress, CancellationToken cancellationToken)
    {
        using var response = await HttpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        var resolvedDestination = ResolveDestinationFromHeaders(destinationPath, response);

        await using var httpStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var fileStream = new FileStream(resolvedDestination, FileMode.Create, FileAccess.Write, FileShare.Read);
        using var sha512 = string.IsNullOrWhiteSpace(expectedSha512) ? null : SHA512.Create();
        var totalBytes = response.Content.Headers.ContentLength;
        var buffer = new byte[81920];
        long totalRead = 0;
        int bytesRead;
        while ((bytesRead = await httpStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
        {
            await fileStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
            sha512?.TransformBlock(buffer, 0, bytesRead, null, 0);
            totalRead += bytesRead;
            var percent = totalBytes.HasValue && totalBytes.Value > 0
                ? Math.Clamp((double)totalRead / totalBytes.Value * 100d, 0d, 100d)
                : 0d;
            progress?.Report(new DownloadProgressInfo(totalRead, totalBytes, percent));
        }

        sha512?.TransformFinalBlock(Array.Empty<byte>(), 0, 0);

        if (sha512 != null)
        {
            var computedHash = Convert.ToBase64String(sha512.Hash!);
            if (!computedHash.Equals(expectedSha512, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(resolvedDestination);
                return null;
            }
        }

        return resolvedDestination;
    }

    private async Task<bool> ExecuteInstallerAsync(InstallerPackage package, string? filePath, CancellationToken cancellationToken)
    {
        var useAutomaticOverride = !string.IsNullOrWhiteSpace(package.AutomaticInstallerPath);

        if (!useAutomaticOverride)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                return false;
            }
        }

        var startInfo = BuildProcessStartInfo(package, filePath);
        if (startInfo == null)
        {
            return false;
        }

        if (IsBiometricPlatform(package))
        {
            _ = await _biometricLicenseBackupService.BackupLocalAsync(cancellationToken).ConfigureAwait(false);
        }

        using var process = Process.Start(startInfo);
        if (process == null)
        {
            return false;
        }

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        var success = process.ExitCode == 0;

        if (success && IsBiometricPlatform(package))
        {
            _ = await _biometricLicenseBackupService.RestoreLocalAsync(cancellationToken).ConfigureAwait(false);
        }

        return success;
    }

    private static bool IsBiometricPlatform(InstallerPackage package)
        => string.Equals(package.Id, BiometricPlatformPackageId, StringComparison.OrdinalIgnoreCase);

    private ProcessStartInfo? BuildProcessStartInfo(InstallerPackage package, string? filePath)
    {
        var startInfo = new ProcessStartInfo
        {
            UseShellExecute = true,
            Verb = "runas"
        };

        if (!string.IsNullOrWhiteSpace(package.AutomaticInstallerPath))
        {
            startInfo.FileName = package.AutomaticInstallerPath!;
            startInfo.Arguments = package.AutomaticInstallerArguments is { Length: > 0 }
                ? string.Join(" ", package.AutomaticInstallerArguments)
                : string.Empty;
            startInfo.WorkingDirectory = _workingDirectory;
            return startInfo;
        }

        if (string.IsNullOrWhiteSpace(filePath))
        {
            return null;
        }

        var extension = Path.GetExtension(filePath);
        startInfo.WorkingDirectory = Path.GetDirectoryName(filePath) ?? Environment.CurrentDirectory;
        var arguments = package.AutomaticInstallArguments is { Length: > 0 }
            ? package.AutomaticInstallArguments
            : package.InstallArguments ?? Array.Empty<string>();

        if (extension.Equals(".msi", StringComparison.OrdinalIgnoreCase))
        {
            var args = new List<string> { "/i", $"\"{filePath}\"" };
            if (arguments.Length > 0)
            {
                args.AddRange(arguments);
            }
            startInfo.FileName = "msiexec.exe";
            startInfo.Arguments = string.Join(" ", args);
        }
        else
        {
            startInfo.FileName = filePath;
            startInfo.Arguments = arguments.Length > 0
                ? string.Join(" ", arguments)
                : string.Empty;
        }

        return startInfo;
    }

    private static string ResolveDestinationFromHeaders(string originalPath, HttpResponseMessage response)
    {
        var directory = Path.GetDirectoryName(originalPath)!;
        var headerFileName = ExtractFileNameFromHeaders(response) ?? ExtractFileNameFromUri(response.RequestMessage?.RequestUri);
        if (string.IsNullOrWhiteSpace(headerFileName))
        {
            return EnsureExtensionFromResponse(originalPath, response);
        }

        var sanitized = SanitizeFileName(headerFileName);
        var extension = GetKnownExtension(sanitized);
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = ResolvePreferredExtension(response.RequestMessage?.RequestUri, originalPath, response.Content.Headers.ContentType?.MediaType);
            sanitized += extension;
        }

        return Path.Combine(directory, sanitized);
    }

    private static string? ExtractFileNameFromHeaders(HttpResponseMessage response)
    {
        var disposition = response.Content.Headers.ContentDisposition;
        var fileName = disposition?.FileNameStar ?? disposition?.FileName;
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        return fileName.Trim().Trim('"');
    }

    private static string SanitizeFileName(string fileName)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(fileName.Length);
        foreach (var c in fileName)
        {
            builder.Append(invalidChars.Contains(c) ? '_' : c);
        }

        return builder.ToString();
    }

    private static string BuildDownloadDestinationPath(InstallerPackage package, string targetDirectory)
    {
        var extension = ResolvePreferredExtension(package.DownloadUri, package.RelativePath, mediaType: null);
        return Path.Combine(targetDirectory, $"{package.Id}-{DateTime.Now:yyyyMMddHHmmss}{extension}");
    }

    private static string EnsureExtensionFromResponse(string originalPath, HttpResponseMessage response)
    {
        var originalExtension = Path.GetExtension(originalPath);
        if (GetKnownExtension(originalPath) is not null && !originalExtension.Equals(".bin", StringComparison.OrdinalIgnoreCase))
        {
            return originalPath;
        }

        var extension = ResolvePreferredExtension(response.RequestMessage?.RequestUri, originalPath, response.Content.Headers.ContentType?.MediaType);
        return Path.ChangeExtension(originalPath, extension);
    }

    private static string ResolvePreferredExtension(Uri? requestUri, string? fallbackPath, string? mediaType)
    {
        return GetKnownExtensionFromUri(requestUri)
            ?? GetKnownExtension(fallbackPath)
            ?? GetKnownExtensionFromMediaType(mediaType)
            ?? ".bin";
    }

    private static string? GetKnownExtensionFromUri(Uri? uri)
    {
        if (uri is null)
        {
            return null;
        }

        var fileName = Path.GetFileName(uri.LocalPath);
        return GetKnownExtension(fileName);
    }

    private static string? GetKnownExtension(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var extension = Path.GetExtension(path);
        return KnownPackageExtensions.Contains(extension) ? extension : null;
    }

    private static string? GetKnownExtensionFromMediaType(string? mediaType)
    {
        if (string.IsNullOrWhiteSpace(mediaType))
        {
            return null;
        }

        return ContentTypeExtensionMap.TryGetValue(mediaType, out var extension) ? extension : null;
    }

    private static string? ExtractFileNameFromUri(Uri? uri)
    {
        if (uri is null)
        {
            return null;
        }

        var candidate = Path.GetFileName(uri.LocalPath);
        return string.IsNullOrWhiteSpace(candidate) ? null : candidate;
    }

    private static string BuildManualCopyDestination(string targetDirectory, string sourcePath, string packageId)
    {
        var extension = Path.GetExtension(sourcePath);
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = ".bin";
        }

        var baseName = Path.GetFileNameWithoutExtension(sourcePath);
        if (string.IsNullOrWhiteSpace(baseName))
        {
            baseName = $"{packageId}-{DateTime.Now:yyyyMMddHHmmss}";
        }

        return Path.Combine(targetDirectory, $"{baseName}{extension}");
    }

    private static void OpenFileDirectory(string filePath)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{filePath}\"",
                UseShellExecute = true
            });
        }
        catch
        {
            var directory = Path.GetDirectoryName(filePath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = directory,
                    UseShellExecute = true
                });
            }
            catch
            {
                // ignored
            }
        }
    }

    private static (string FileName, string? Arguments) SplitCommandLine(string commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return (string.Empty, null);
        }

        commandLine = commandLine.Trim();
        if (commandLine.StartsWith("\"", StringComparison.Ordinal))
        {
            var end = commandLine.IndexOf('"', 1);
            if (end > 1)
            {
                var file = commandLine.Substring(1, end - 1);
                var args = commandLine.Substring(end + 1).Trim();
                return (file, args);
            }
        }

        var parts = commandLine.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0
            ? (string.Empty, null)
            : (parts[0], parts.Length > 1 ? parts[1] : null);
    }

    private sealed record LeitorManifestInfo(string FileName, string? Sha512, long? Size);

    public sealed record DownloadProgressInfo(long BytesReceived, long? TotalBytes, double Percent);

    private static LeitorManifestInfo? ParseLeitorManifest(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        string? fileName = null;
        string? sha512 = null;
        long? size = null;

        using var reader = new StringReader(content);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            var normalized = trimmed.TrimStart('-').Trim();
            var separatorIndex = normalized.IndexOf(':');
            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = normalized.Substring(0, separatorIndex).Trim();
            var value = normalized.Substring(separatorIndex + 1).Trim();

            if (key.Equals("url", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(fileName))
            {
                fileName = value;
            }
            else if (key.Equals("sha512", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(sha512))
            {
                sha512 = value;
            }
            else if (key.Equals("size", StringComparison.OrdinalIgnoreCase) && !size.HasValue)
            {
                if (long.TryParse(value, out var parsed))
                {
                    size = parsed;
                }
            }

            if (!string.IsNullOrWhiteSpace(fileName) && !string.IsNullOrWhiteSpace(sha512))
            {
                break;
            }
        }

        return string.IsNullOrWhiteSpace(fileName)
            ? null
            : new LeitorManifestInfo(fileName, sha512, size);
    }
}
