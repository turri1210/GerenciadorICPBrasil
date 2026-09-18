using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using GerenciadorIcpBrasil.Models;
using GerenciadorIcpBrasil.Services;
using GerenciadorIcpBrasil.Modules.ConfigAuditoria.Services;
using GerenciadorIcpBrasil.Modules.InstallerLauncher.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.UI;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace GerenciadorIcpBrasil.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly ModuleCatalogService _catalogService;
    private readonly SettingsService _settingsService;
    private readonly UsefulLinksCatalogService _usefulLinksCatalogService;
    private readonly DispatcherQueue _dispatcher;

    private ModuleItem? _selectedModule;
    private string? _selectedModuleId;
    private string _statusText = "Pronto";
    private bool _isBusy;
    private string _searchQuery = string.Empty;
    private int _filteredCount;
    private bool _isAboutSearch;
    private string? _lastOperationMessage;
    private GerenciadorIcpBrasil.Modules.ConfigAuditoria.ViewModels.MainViewModel? _configAuditoria;
    private InstallerLauncherViewModel? _installerLauncher;

    private string _biometriaStatusText = "Aguardando verificação.";
    private string _biometriaResultText = "";
    private int _biometriaTimeoutMs = 100;
    private bool _isBiometriaBusy;
    private string? _biometriaImageBase64;
    private string _biometriaImageInfo = "";
    private bool _isUsefulLinksBusy;
    private string _usefulLinksStatusText = "Links ainda nao carregados.";
    private bool _isBiometricLicenseInstalled;
    private bool _hasBiometricLicenseBackup;
    private string _biometricLicenseStatusText = "Status da licença não verificado.";
    private string _biometricLicenseDetectedFilePath = "-";
    private string _biometricLicenseLastWriteText = "-";
    private bool _isBiometricDetailsExpanded;
    private Brush _biometricLicenseStatusBrush = new SolidColorBrush(Colors.DarkGray);
    private string _biometricLicenseStatusTitle = "Status não verificado";

    public MainViewModel(
        ModuleCatalogService catalogService,
        SettingsService settingsService,
        UsefulLinksCatalogService usefulLinksCatalogService,
        DispatcherQueue? dispatcher = null)
    {
        _catalogService = catalogService;
        _settingsService = settingsService;
        _usefulLinksCatalogService = usefulLinksCatalogService;
        _dispatcher = dispatcher ?? DispatcherQueue.GetForCurrentThread();
    }

    public ObservableCollection<ModuleItem> Modules { get; } = new();
    public ObservableCollection<ModuleItem> FilteredModules { get; } = new();
    public ObservableCollection<UsefulLinkItem> UsefulLinks { get; } = new();
    public ObservableCollection<UsefulLinkGroup> UsefulLinkGroups { get; } = new();

    public ModuleItem? SelectedModule
    {
        get => _selectedModule;
        set
        {
            if (!Equals(_selectedModule, value))
            {
                _selectedModule = value;
                if (!string.IsNullOrWhiteSpace(_selectedModule?.Id))
                {
                    _selectedModuleId = _selectedModule.Id;
                }
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasSelection));
                OnPropertyChanged(nameof(ShowDefaultEmptyState));
            }
        }
    }

    public bool HasSelection => SelectedModule is not null;

    public bool IsAboutSearch
    {
        get => _isAboutSearch;
        private set
        {
            if (_isAboutSearch != value)
            {
                _isAboutSearch = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ShowDefaultEmptyState));
            }
        }
    }

    public bool ShowDefaultEmptyState => !HasSelection && !IsAboutSearch;

    public string AboutVersionText =>
        $"Versão {typeof(MainViewModel).Assembly.GetName().Version?.ToString(4) ?? "não identificada"}";

    public string AboutSummaryText =>
        "O Gerenciador ICP Brasil reúne verificações, orientações e ferramentas para preparar estações Windows que utilizam certificados digitais, mídias criptográficas, leitoras e biometria.";

    public string StatusText
    {
        get => _statusText;
        set
        {
            if (_statusText != value)
            {
                _statusText = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (_isBusy != value)
            {
                _isBusy = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanInteract));
                OnPropertyChanged(nameof(CanBiometriaInteract));
            }
        }
    }

    public bool CanInteract => !IsBusy;

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (_searchQuery != value)
            {
                _searchQuery = value;
                OnPropertyChanged();
                ApplyFilter();
            }
        }
    }

    public int FilteredCount
    {
        get => _filteredCount;
        set
        {
            if (_filteredCount != value)
            {
                _filteredCount = value;
                OnPropertyChanged();
            }
        }
    }

    public string? LastOperationMessage
    {
        get => _lastOperationMessage;
        set
        {
            if (_lastOperationMessage != value)
            {
                _lastOperationMessage = value;
                OnPropertyChanged();
            }
        }
    }

    public string BiometriaStatusText
    {
        get => _biometriaStatusText;
        set
        {
            if (_biometriaStatusText != value)
            {
                _biometriaStatusText = value;
                OnPropertyChanged();
            }
        }
    }

    public string BiometriaResultText
    {
        get => _biometriaResultText;
        set
        {
            if (_biometriaResultText != value)
            {
                _biometriaResultText = value;
                OnPropertyChanged();
            }
        }
    }

    public int BiometriaTimeoutMs
    {
        get => _biometriaTimeoutMs;
        set
        {
            if (_biometriaTimeoutMs != value)
            {
                _biometriaTimeoutMs = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsBiometriaBusy
    {
        get => _isBiometriaBusy;
        set
        {
            if (_isBiometriaBusy != value)
            {
                _isBiometriaBusy = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanBiometriaInteract));
            }
        }
    }

    public bool CanBiometriaInteract => !IsBusy && !IsBiometriaBusy;
    public bool IsUsefulLinksBusy
    {
        get => _isUsefulLinksBusy;
        set
        {
            if (_isUsefulLinksBusy != value)
            {
                _isUsefulLinksBusy = value;
                OnPropertyChanged();
            }
        }
    }

    public string UsefulLinksStatusText
    {
        get => _usefulLinksStatusText;
        set
        {
            if (_usefulLinksStatusText != value)
            {
                _usefulLinksStatusText = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsBiometricLicenseInstalled
    {
        get => _isBiometricLicenseInstalled;
        set
        {
            if (_isBiometricLicenseInstalled != value)
            {
                _isBiometricLicenseInstalled = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanRestoreBiometricLicenseBackup));
            }
        }
    }

    public bool HasBiometricLicenseBackup
    {
        get => _hasBiometricLicenseBackup;
        set
        {
            if (_hasBiometricLicenseBackup != value)
            {
                _hasBiometricLicenseBackup = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanRestoreBiometricLicenseBackup));
            }
        }
    }

    public bool CanRestoreBiometricLicenseBackup => HasBiometricLicenseBackup && !IsBiometricLicenseInstalled;

    public string BiometricLicenseStatusText
    {
        get => _biometricLicenseStatusText;
        set
        {
            if (_biometricLicenseStatusText != value)
            {
                _biometricLicenseStatusText = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsBiometricDetailsExpanded
    {
        get => _isBiometricDetailsExpanded;
        set
        {
            if (_isBiometricDetailsExpanded != value)
            {
                _isBiometricDetailsExpanded = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(BiometricDetailsChevronGlyph));
            }
        }
    }

    public string BiometricDetailsChevronGlyph => IsBiometricDetailsExpanded ? "\uE70E" : "\uE70D";

    public Brush BiometricLicenseStatusBrush
    {
        get => _biometricLicenseStatusBrush;
        set
        {
            if (!Equals(_biometricLicenseStatusBrush, value))
            {
                _biometricLicenseStatusBrush = value;
                OnPropertyChanged();
            }
        }
    }

    public string BiometricLicenseStatusTitle
    {
        get => _biometricLicenseStatusTitle;
        set
        {
            if (_biometricLicenseStatusTitle != value)
            {
                _biometricLicenseStatusTitle = value;
                OnPropertyChanged();
            }
        }
    }

    public string BiometricLicenseDetectedFilePath
    {
        get => _biometricLicenseDetectedFilePath;
        set
        {
            if (_biometricLicenseDetectedFilePath != value)
            {
                _biometricLicenseDetectedFilePath = value;
                OnPropertyChanged();
            }
        }
    }

    public string BiometricLicenseLastWriteText
    {
        get => _biometricLicenseLastWriteText;
        set
        {
            if (_biometricLicenseLastWriteText != value)
            {
                _biometricLicenseLastWriteText = value;
                OnPropertyChanged();
            }
        }
    }


    public string? BiometriaImageBase64
    {
        get => _biometriaImageBase64;
        set
        {
            if (_biometriaImageBase64 != value)
            {
                _biometriaImageBase64 = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasBiometriaImage));
            }
        }
    }

    public bool HasBiometriaImage => !string.IsNullOrWhiteSpace(BiometriaImageBase64);

    public string BiometriaImageInfo
    {
        get => _biometriaImageInfo;
        set
        {
            if (_biometriaImageInfo != value)
            {
                _biometriaImageInfo = value;
                OnPropertyChanged();
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public GerenciadorIcpBrasil.Modules.ConfigAuditoria.ViewModels.MainViewModel? ConfigAuditoria
    {
        get => _configAuditoria;
        private set
        {
            if (!Equals(_configAuditoria, value))
            {
                _configAuditoria = value;
                OnPropertyChanged();
            }
        }
    }

    public InstallerLauncherViewModel? InstallerLauncher
    {
        get => _installerLauncher;
        private set
        {
            if (!Equals(_installerLauncher, value))
            {
                _installerLauncher = value;
                OnPropertyChanged();
            }
        }
    }

    public async Task InitializeAsync()
    {
        await _settingsService.LoadAsync();
        await RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        await RunOnUiAsync(() =>
        {
            IsBusy = true;
            StatusText = "Carregando funcionalidades...";
        });
        try
        {
            var modules = await _catalogService.LoadMergedModulesAsync();
            await RunOnUiAsync(() =>
            {
                Modules.Clear();
                foreach (var module in modules.OrderBy(m => m.Name))
                {
                    Modules.Add(module);
                }

                ApplyFilter();
                StatusText = "Pronto";
            });

            if (modules.Any(m => string.Equals(m.Id, "links-uteis", StringComparison.OrdinalIgnoreCase)))
            {
                await RefreshUsefulLinksAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            await RunOnUiAsync(() => IsBusy = false);
        }
    }

    public Task RefreshLeitorRuntimeStatusAsync()
    {
        var isRunning = false;
        try
        {
            using var client = new System.Net.Sockets.TcpClient();
            var connect = client.ConnectAsync("127.0.0.1", CertificateBridgeService.Port);
            isRunning = connect.Wait(TimeSpan.FromMilliseconds(300)) && client.Connected;
        }
        catch
        {
            isRunning = false;
        }

        var readerModule = Modules.FirstOrDefault(module =>
            string.Equals(module.Id, ModuleCatalogService.LeitorCertificadoModuleId, StringComparison.OrdinalIgnoreCase));
        if (readerModule is not null)
        {
            readerModule.IsRunning = isRunning;
        }

        return Task.CompletedTask;
    }

    private Task RunOnUiAsync(Action action)
    {
        if (_dispatcher.HasThreadAccess)
        {
            action();
            return Task.CompletedTask;
        }

        var tcs = new TaskCompletionSource<object?>();
        var enqueued = _dispatcher.TryEnqueue(() =>
        {
            try
            {
                action();
                tcs.SetResult(null);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });

        if (!enqueued)
        {
            try
            {
                action();
                tcs.SetResult(null);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        }

        return tcs.Task;
    }

    public async Task RefreshUsefulLinksAsync()
    {
        await RunOnUiAsync(() => IsUsefulLinksBusy = true);
        try
        {
            var result = await _usefulLinksCatalogService.LoadAsync().ConfigureAwait(false);
            await RunOnUiAsync(() =>
            {
                UsefulLinks.Clear();
                UsefulLinkGroups.Clear();
                foreach (var link in result.Links)
                {
                    UsefulLinks.Add(link);
                }

                foreach (var group in result.Links
                             .GroupBy(l => string.IsNullOrWhiteSpace(l.Category) ? "Sem categoria" : l.Category)
                             .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
                {
                    var category = group.Key;
                    var stateKey = BuildUsefulLinksStateKey(category);
                    var itemGroup = new UsefulLinkGroup(
                        category,
                        _settingsService.GetCategoryExpandedState(stateKey, false),
                        (categoryName, isExpanded) =>
                        {
                            _ = _settingsService.SetCategoryExpandedStateAsync(BuildUsefulLinksStateKey(categoryName), isExpanded);
                        });
                    foreach (var link in group.OrderBy(l => l.Title, StringComparer.OrdinalIgnoreCase))
                    {
                        itemGroup.Items.Add(link);
                    }
                    UsefulLinkGroups.Add(itemGroup);
                }

                if (UsefulLinks.Count == 0)
                {
                    UsefulLinksStatusText = "Nenhum link disponível no momento.";
                    return;
                }

                UsefulLinksStatusText = $"{UsefulLinks.Count} links disponíveis no aplicativo.";
            });
        }
        catch (Exception ex)
        {
            await RunOnUiAsync(() => UsefulLinksStatusText = $"Falha ao carregar links: {ex.Message}");
        }
        finally
        {
            await RunOnUiAsync(() => IsUsefulLinksBusy = false);
        }
    }

    public void UpdateBiometriaResult(BiometriaService.BiometriaResult result)
    {
        BiometriaImageBase64 = result.ImageBase64;
        if (result.Width.HasValue && result.Height.HasValue)
        {
            BiometriaImageInfo = $"Imagem: {result.Width} x {result.Height}";
        }
        else
        {
            BiometriaImageInfo = "";
        }
    }

    private void ApplyFilter()
    {
        var query = _searchQuery?.Trim();
        IsAboutSearch = string.Equals(NormalizeSearchTerm(query), "sobre", StringComparison.Ordinal);
        FilteredModules.Clear();

        var selectedId = _selectedModuleId ?? SelectedModule?.Id;
        IEnumerable<ModuleItem> items = IsAboutSearch
            ? Enumerable.Empty<ModuleItem>()
            : Modules;
        if (!IsAboutSearch && !string.IsNullOrEmpty(query))
        {
            items = items.Where(m =>
                m.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || m.Id.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var item in items)
        {
            FilteredModules.Add(item);
        }

        FilteredCount = FilteredModules.Count;
        if (!string.IsNullOrWhiteSpace(selectedId))
        {
            SelectedModule = FilteredModules.FirstOrDefault(m =>
                string.Equals(m.Id, selectedId, StringComparison.OrdinalIgnoreCase))
                ?? FilteredModules.FirstOrDefault();
        }
        else
        {
            SelectedModule = FilteredModules.FirstOrDefault();
        }
    }

    private static string NormalizeSearchTerm(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var decomposed = value.Trim().Normalize(NormalizationForm.FormD);
        var normalized = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                normalized.Append(char.ToLowerInvariant(character));
            }
        }

        return normalized.ToString().Normalize(NormalizationForm.FormC);
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public void InitializeConfigAuditoria(IConfigAuditoriaUiService uiService)
    {
        if (ConfigAuditoria is not null)
        {
            return;
        }

        var service = new GerenciadorIcpBrasil.Modules.ConfigAuditoria.Services.ConfigurationAuditService(uiService);
        ConfigAuditoria = new GerenciadorIcpBrasil.Modules.ConfigAuditoria.ViewModels.MainViewModel(service, uiService);
    }

    public void InitializeInstallerLauncher(Func<CancellationToken, Task<string?>>? resolveManualDownloadDirectoryAsync = null)
    {
        InstallerLauncher ??= new InstallerLauncherViewModel(
            title => _settingsService.GetCategoryExpandedState(InstallerLauncherViewModel.BuildStateKey(title), false),
            (title, isExpanded) => _ = _settingsService.SetCategoryExpandedStateAsync(InstallerLauncherViewModel.BuildStateKey(title), isExpanded),
            resolveManualDownloadDirectoryAsync);
        InstallerLauncher.RefreshAll();
    }

    private static string BuildUsefulLinksStateKey(string category) => $"links:{category}";

    public void UpdateBiometricLicenseStatus(bool licenseInstalled, bool backupExists, string? message = null)
    {
        IsBiometricLicenseInstalled = licenseInstalled;
        HasBiometricLicenseBackup = backupExists;

        if (licenseInstalled && backupExists)
        {
            BiometricLicenseStatusTitle = "OK";
            BiometricLicenseStatusBrush = new SolidColorBrush(Color.FromArgb(255, 34, 197, 94));
        }
        else if (licenseInstalled && !backupExists)
        {
            BiometricLicenseStatusTitle = "Necessário realizar backup";
            BiometricLicenseStatusBrush = new SolidColorBrush(Color.FromArgb(255, 245, 158, 11));
        }
        else if (!licenseInstalled && backupExists)
        {
            BiometricLicenseStatusTitle = "Necessário restaurar backup";
            BiometricLicenseStatusBrush = new SolidColorBrush(Color.FromArgb(255, 245, 158, 11));
        }
        else
        {
            BiometricLicenseStatusTitle = "Licença não instalada ou não reconhecida";
            BiometricLicenseStatusBrush = new SolidColorBrush(Color.FromArgb(255, 239, 68, 68));
        }

        if (!string.IsNullOrWhiteSpace(message))
        {
            BiometricLicenseStatusText = message;
            return;
        }

        BiometricLicenseStatusText = licenseInstalled
            ? "Licença biométrica instalada."
            : (backupExists
                ? "Licença não detectada. Backup disponível para restauração."
                : "Licença não detectada e sem backup.");
    }

    public void UpdateBiometricLicenseMetadata(string? detectedFilePath, DateTime? lastWrite)
    {
        BiometricLicenseDetectedFilePath = string.IsNullOrWhiteSpace(detectedFilePath) ? "-" : detectedFilePath!;
        BiometricLicenseLastWriteText = lastWrite.HasValue
            ? lastWrite.Value.ToString("dd/MM/yyyy HH:mm:ss")
            : "-";
    }
}
