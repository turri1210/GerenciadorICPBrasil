namespace GerenciadorIcpBrasil.Modules.InstallerLauncher.Models;

public sealed record InstallerPackage(
    string Id,
    string DisplayName,
    InstallerCategory Category,
    string? RelativePath,
    Uri? DownloadUri,
    Uri? ExtensionInstallUri = null,
    string? ArchiveEntryPath = null,
    string? RequiredVersion = null,
    string[]? DetectionNames = null,
    string[]? InstallArguments = null,
    string[]? AutomaticInstallArguments = null,
    string? AutomaticInstallerPath = null,
    string[]? AutomaticInstallerArguments = null,
    int Sequence = 0,
    bool RequiresElevation = true,
    bool RequiresManualConfirmation = false);
