using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using GerenciadorIcpBrasil.Modules.InstallerLauncher.Commands;
using GerenciadorIcpBrasil.Modules.InstallerLauncher.Models;
using GerenciadorIcpBrasil.Modules.InstallerLauncher.Services;

namespace GerenciadorIcpBrasil.Modules.InstallerLauncher.ViewModels;

public sealed class InstallerItemViewModel : INotifyPropertyChanged
{
    private readonly InstallerManager _manager;
    private readonly Func<bool> _isManualInstallationEnabled;
    private readonly Func<CancellationToken, Task<string?>>? _resolveManualDownloadDirectoryAsync;
    private InstallerPackageState _state;
    private bool _isBusy;
    private double _downloadProgress;
    private bool _isDownloading;
    private long _downloadedBytes;
    private long? _totalBytes;

    public InstallerItemViewModel(
        InstallerPackage package,
        InstallerManager manager,
        Func<bool>? isManualInstallationEnabled = null,
        Func<CancellationToken, Task<string?>>? resolveManualDownloadDirectoryAsync = null)
    {
        Package = package;
        _manager = manager;
        _isManualInstallationEnabled = isManualInstallationEnabled ?? (() => false);
        _resolveManualDownloadDirectoryAsync = resolveManualDownloadDirectoryAsync;
        _state = manager.GetState(package);

        InstallCommand = new AsyncRelayCommand(InstallAsync, () => !IsBusy);
        ReinstallCommand = new AsyncRelayCommand(ReinstallAsync, () => !IsBusy);
        UninstallCommand = new AsyncRelayCommand(UninstallAsync, () => !IsBusy && IsInstalled);
        InstallOnChromeCommand = new AsyncRelayCommand(InstallOnChromeAsync, () => !IsBusy && IsBrowserExtensionItem);
        InstallOnEdgeCommand = new AsyncRelayCommand(InstallOnEdgeAsync, () => !IsBusy && IsBrowserExtensionItem);
    }

    public InstallerPackage Package { get; }

    public string DisplayName => Package.DisplayName;

    public string CategoryLabel => Package.Category.ToString();

    public string? InstalledVersion => _state.InstalledVersion;

    public string? RequiredVersion => Package.RequiredVersion;

    public bool IsInstalled => _state.IsInstalled;

    public bool IsUpToDate => _state.IsUpToDate;

    public bool IsBrowserExtensionItem => Package.ExtensionInstallUri is not null;

    public bool ShowStandardActions => !IsBrowserExtensionItem;

    public bool ShowBrowserExtensionActions => IsBrowserExtensionItem;
    public bool ShowUninstallAction => true;

    public string StatusLabel
    {
        get
        {
            if (IsBrowserExtensionItem)
            {
                return "Extensão disponível";
            }

            if (!IsInstalled)
            {
                return "Não instalado";
            }

            if (!IsUpToDate)
            {
                return "Atualização necessária";
            }

            return "Instalado";
        }
    }

    public string InstallationSummary => IsInstalled
        ? $"Instalado: {InstalledVersion ?? "-"}"
        : "Não instalado";

    public string PrimaryActionLabel => IsDownloading
        ? "Baixando"
        : (IsInstalled ? "Reinstalar" : "Instalar");

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (_isBusy != value)
            {
                _isBusy = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanUninstall));
                InstallCommand.RaiseCanExecuteChanged();
                ReinstallCommand.RaiseCanExecuteChanged();
                UninstallCommand.RaiseCanExecuteChanged();
                InstallOnChromeCommand.RaiseCanExecuteChanged();
                InstallOnEdgeCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public double DownloadProgress
    {
        get => _downloadProgress;
        private set
        {
            if (Math.Abs(_downloadProgress - value) > 0.01)
            {
                _downloadProgress = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsDownloadIndeterminate));
            }
        }
    }

    public long DownloadedBytes
    {
        get => _downloadedBytes;
        private set
        {
            if (_downloadedBytes != value)
            {
                _downloadedBytes = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DownloadInfoText));
            }
        }
    }

    public long? TotalBytes
    {
        get => _totalBytes;
        private set
        {
            if (_totalBytes != value)
            {
                _totalBytes = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DownloadInfoText));
            }
        }
    }

    public string DownloadInfoText
    {
        get
        {
            if (!IsDownloading)
            {
                return string.Empty;
            }

            var totalText = TotalBytes.HasValue ? FormatBytes(TotalBytes.Value) : "-";
            var percentText = DownloadProgress > 0 ? $"{DownloadProgress:0.#}%" : "";
            return $"Baixado: {FormatBytes(DownloadedBytes)} / {totalText} {percentText}".Trim();
        }
    }

    public bool IsDownloading
    {
        get => _isDownloading;
        private set
        {
            if (_isDownloading != value)
            {
                _isDownloading = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsDownloadIndeterminate));
                OnPropertyChanged(nameof(PrimaryActionLabel));
            }
        }
    }

    public bool IsDownloadIndeterminate => IsDownloading && DownloadProgress <= 0;

    public bool CanUninstall => !IsBrowserExtensionItem && IsInstalled && !IsBusy;

    public AsyncRelayCommand InstallCommand { get; }

    public AsyncRelayCommand ReinstallCommand { get; }

    public AsyncRelayCommand UninstallCommand { get; }

    public AsyncRelayCommand InstallOnChromeCommand { get; }

    public AsyncRelayCommand InstallOnEdgeCommand { get; }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void RefreshState()
    {
        _state = _manager.GetState(Package);
        OnPropertyChanged(nameof(InstalledVersion));
        OnPropertyChanged(nameof(IsInstalled));
        OnPropertyChanged(nameof(IsUpToDate));
        OnPropertyChanged(nameof(StatusLabel));
        OnPropertyChanged(nameof(InstallationSummary));
        OnPropertyChanged(nameof(PrimaryActionLabel));
        OnPropertyChanged(nameof(CanUninstall));
        OnPropertyChanged(nameof(ShowUninstallAction));
        OnPropertyChanged(nameof(ShowStandardActions));
        OnPropertyChanged(nameof(ShowBrowserExtensionActions));
        UninstallCommand.RaiseCanExecuteChanged();
        InstallOnChromeCommand.RaiseCanExecuteChanged();
        InstallOnEdgeCommand.RaiseCanExecuteChanged();
    }

    private async Task InstallAsync()
    {
        if (IsBrowserExtensionItem)
        {
            await InstallOnChromeAsync();
            return;
        }

        await ExecuteInstallWorkflowAsync();
    }

    private async Task ReinstallAsync()
    {
        if (IsBrowserExtensionItem)
        {
            await InstallOnChromeAsync();
            return;
        }

        await ExecuteInstallWorkflowAsync();
    }

    private async Task ExecuteInstallWorkflowAsync()
    {
        IsBusy = true;
        IsDownloading = true;
        DownloadProgress = 0;
        DownloadedBytes = 0;
        TotalBytes = null;
        var progress = new Progress<InstallerManager.DownloadProgressInfo>(info =>
        {
            DownloadedBytes = info.BytesReceived;
            TotalBytes = info.TotalBytes;
            DownloadProgress = info.Percent;
            IsDownloading = true;
        });
        try
        {
            if (_isManualInstallationEnabled())
            {
                var targetDirectory = _resolveManualDownloadDirectoryAsync is null
                    ? null
                    : await _resolveManualDownloadDirectoryAsync(CancellationToken.None);
                if (string.IsNullOrWhiteSpace(targetDirectory))
                {
                    return;
                }

                await _manager.DownloadOnlyAsync(Package, targetDirectory, progress, CancellationToken.None);
            }
            else
            {
                await _manager.InstallAsync(Package, progress, CancellationToken.None);
            }
        }
        finally
        {
            RefreshState();
            IsDownloading = false;
            DownloadedBytes = 0;
            TotalBytes = null;
            IsBusy = false;
        }
    }

    private static string FormatBytes(long bytes)
    {
        const double scale = 1024;
        string[] units = { "B", "KB", "MB", "GB" };
        var size = (double)bytes;
        var unitIndex = 0;
        while (size >= scale && unitIndex < units.Length - 1)
        {
            size /= scale;
            unitIndex++;
        }
        return $"{size:0.#} {units[unitIndex]}";
    }

    private async Task UninstallAsync()
    {
        if (IsBrowserExtensionItem)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await _manager.UninstallAsync(Package, _state, CancellationToken.None);
        }
        finally
        {
            RefreshState();
            IsBusy = false;
        }
    }

    private Task InstallOnChromeAsync() => OpenExtensionInBrowserAsync(BrowserTarget.Chrome);

    private Task InstallOnEdgeAsync() => OpenExtensionInBrowserAsync(BrowserTarget.Edge);

    private Task OpenExtensionInBrowserAsync(BrowserTarget target)
    {
        var uri = Package.ExtensionInstallUri;
        if (uri is null)
        {
            return Task.CompletedTask;
        }

        IsBusy = true;
        try
        {
            var browserPath = ResolveBrowserPath(target);
            if (!TryStartProcess(browserPath ?? GetBrowserExecutableName(target), uri.AbsoluteUri))
            {
                if (target == BrowserTarget.Edge)
                {
                    TryStartProcess($"microsoft-edge:{uri.AbsoluteUri}", null);
                }
                else
                {
                    TryStartProcess(uri.AbsoluteUri, null);
                }
            }
        }
        finally
        {
            IsBusy = false;
        }

        return Task.CompletedTask;
    }

    private static bool TryStartProcess(string fileName, string? arguments)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments ?? string.Empty,
                UseShellExecute = true
            });
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string GetBrowserExecutableName(BrowserTarget target) =>
        target == BrowserTarget.Chrome ? "chrome.exe" : "msedge.exe";

    private static string? ResolveBrowserPath(BrowserTarget target)
    {
        var candidates = target == BrowserTarget.Chrome
            ? GetChromeCandidates()
            : GetEdgeCandidates();

        return candidates.FirstOrDefault(File.Exists);
    }

    private static IEnumerable<string> GetChromeCandidates()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        return new[]
        {
            Path.Combine(programFiles, "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(programFilesX86, "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(localAppData, "Google", "Chrome", "Application", "chrome.exe")
        };
    }

    private static IEnumerable<string> GetEdgeCandidates()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        return new[]
        {
            Path.Combine(programFilesX86, "Microsoft", "Edge", "Application", "msedge.exe"),
            Path.Combine(programFiles, "Microsoft", "Edge", "Application", "msedge.exe")
        };
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private enum BrowserTarget
    {
        Chrome,
        Edge
    }
}
