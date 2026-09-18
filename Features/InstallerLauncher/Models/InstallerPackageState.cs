namespace GerenciadorIcpBrasil.Modules.InstallerLauncher.Models;

public sealed record InstallerPackageState(
    bool IsInstalled,
    bool IsUpToDate,
    string? InstalledVersion,
    string? UninstallString,
    string? QuietUninstallString);
