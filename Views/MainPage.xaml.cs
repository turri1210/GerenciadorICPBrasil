using GerenciadorIcpBrasil.Services;
using GerenciadorIcpBrasil.ViewModels;
using GerenciadorIcpBrasil.Modules.InstallerLauncher.Services;
using System.Net.NetworkInformation;
using System.Diagnostics;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Dispatching;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;
using Windows.UI;
using WinRT.Interop;

namespace GerenciadorIcpBrasil.Views;

public sealed partial class MainPage : Page
{
    private readonly MainViewModel _viewModel;
    private readonly BiometriaService _biometriaService;
    private readonly AppUpdateService _appUpdateService;
    private ConfigAuditoriaUiService? _configAuditoriaUiService;
    private readonly AuditService _auditService;
    private readonly BiometricLicenseBackupService _biometricLicenseBackupService;
    private readonly DispatcherTimer _leitorRuntimeStatusTimer;

    public MainPage()
    {
        InitializeComponent();

        var baseDir = AppContext.BaseDirectory;
        var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Gerenciador ICP Brasil");
        var appVersion = typeof(App).Assembly.GetName().Version?.ToString() ?? "0.0.0";
        _auditService = new AuditService(appVersion);
        _appUpdateService = new AppUpdateService(baseDir, appData);
        NetworkChange.NetworkAvailabilityChanged += async (_, args) =>
        {
            if (args.IsAvailable)
            {
                await _auditService.SendPendingAsync();
            }
        };
        var catalogService = new ModuleCatalogService(baseDir, appData, _auditService);
        var settingsService = new SettingsService(appData);
        var usefulLinksCatalogService = new UsefulLinksCatalogService(baseDir);
        _biometriaService = new BiometriaService();
        _biometricLicenseBackupService = new BiometricLicenseBackupService();

        var dispatcher = App.MainWindow?.DispatcherQueue ?? DispatcherQueue.GetForCurrentThread();
        _viewModel = new MainViewModel(catalogService, settingsService, usefulLinksCatalogService, dispatcher);
        DataContext = _viewModel;
        _leitorRuntimeStatusTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(3),
        };
        _leitorRuntimeStatusTimer.Tick += async (_, _) => await _viewModel.RefreshLeitorRuntimeStatusAsync();

        Loaded += OnLoadedAsync;
    }

    private async void OnLoadedAsync(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        try
        {
            ConfigureTitleBar();
            await _auditService.EnsureAppInstallEventAsync();
            await _auditService.SendPendingAsync();
            _configAuditoriaUiService ??= new ConfigAuditoriaUiService(() => XamlRoot, () => ConfigAuditoriaPanel);
            _viewModel.InitializeConfigAuditoria(_configAuditoriaUiService);
            _viewModel.InitializeInstallerLauncher(PickManualInstallerFolderAsync);
            await _viewModel.InitializeAsync();
            await _viewModel.RefreshLeitorRuntimeStatusAsync();
            _leitorRuntimeStatusTimer.Start();
            await RefreshBiometricLicenseStatusAsync();
            await CheckAppUpdateAsync(silentIfUpToDate: true);
            RootGrid.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
        }
        catch (Exception ex)
        {
            _viewModel.StatusText = $"Erro: {ex.Message}";
        }
    }

    private void ConfigureTitleBar()
    {
        var window = App.MainWindow;
        if (window == null)
        {
            return;
        }

        window.ExtendsContentIntoTitleBar = true;
        window.SetTitleBar(TitleBarArea);

        var hwnd = WindowNative.GetWindowHandle(window);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        var titleBar = appWindow.TitleBar;

        titleBar.ExtendsContentIntoTitleBar = true;
        titleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        titleBar.ButtonBackgroundColor = Colors.Transparent;
        titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        titleBar.ButtonHoverBackgroundColor = Color.FromArgb(30, 255, 255, 255);
        titleBar.ButtonPressedBackgroundColor = Color.FromArgb(60, 255, 255, 255);

        TitleBarArea.Margin = new Thickness(titleBar.LeftInset, 0, titleBar.RightInset, 0);
    }

    private void OnSearchTextChanged(object sender, AutoSuggestBoxTextChangedEventArgs e)
    {
        if (e.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
        {
            return;
        }

        _viewModel.SearchQuery = SearchBox.Text ?? string.Empty;
    }

    private async Task ShowMessageAsync(string message, string title = "Gerenciador")
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            CloseButtonText = "Fechar",
            XamlRoot = XamlRoot,
        };
        await dialog.ShowAsync();
    }

    private async void OnBiometriaStatusClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        _viewModel.IsBiometriaBusy = true;
        _viewModel.BiometriaResultText = "";
        _viewModel.BiometriaStatusText = "Verificando leitora biométrica...";
        try
        {
            var result = await _biometriaService.CheckStatusAsync();
            _viewModel.BiometriaStatusText = result.Success ? "Leitora pronta." : "Leitora indisponível.";
            _viewModel.BiometriaResultText = result.Message;
            _viewModel.UpdateBiometriaResult(result);
        }
        catch (Exception ex)
        {
            _viewModel.BiometriaStatusText = "Falha ao verificar.";
            _viewModel.BiometriaResultText = ex.Message;
            _viewModel.UpdateBiometriaResult(new BiometriaService.BiometriaResult(false, ex.Message, null, null, null));
        }
        finally
        {
            _viewModel.IsBiometriaBusy = false;
        }
    }

    private async void OnBiometriaCaptureClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        _viewModel.IsBiometriaBusy = true;
        _viewModel.BiometriaResultText = "";
        _viewModel.BiometriaStatusText = "Capturando biometria...";
        try
        {
            var timeout = Math.Clamp(_viewModel.BiometriaTimeoutMs, 5000, 60000);
            var result = await _biometriaService.CaptureAsync(timeout);
            _viewModel.BiometriaStatusText = result.Success ? "Captura concluida." : "Falha na captura.";
            _viewModel.BiometriaResultText = result.Message;
            _viewModel.UpdateBiometriaResult(result);
        }
        catch (Exception ex)
        {
            _viewModel.BiometriaStatusText = "Falha na captura.";
            _viewModel.BiometriaResultText = ex.Message;
            _viewModel.UpdateBiometriaResult(new BiometriaService.BiometriaResult(false, ex.Message, null, null, null));
        }
        finally
        {
            _viewModel.IsBiometriaBusy = false;
        }
    }

    private async void OnOpenUsefulLinkClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string url || string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        try
        {
            var started = Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true,
            });

            if (started == null)
            {
                await ShowMessageAsync("Nao foi possivel abrir o link selecionado.", "Links uteis");
            }
        }
        catch (Exception ex)
        {
            await ShowMessageAsync($"Falha ao abrir link: {ex.Message}", "Links uteis");
        }
    }

    private async void OnCopyUsefulLinkClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string url || string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        try
        {
            var package = new DataPackage();
            package.SetText(url);
            Clipboard.SetContent(package);
            Clipboard.Flush();
            _viewModel.UsefulLinksStatusText = "Link copiado para a area de transferencia.";
        }
        catch (Exception ex)
        {
            await ShowMessageAsync($"Falha ao copiar link: {ex.Message}", "Links uteis");
        }
    }

    private async void OnCheckAppUpdateClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        await CheckForUpdatesFromTrayAsync();
    }

    public Task CheckForUpdatesFromTrayAsync()
        => CheckAppUpdateAsync(silentIfUpToDate: false);

    private async Task CheckAppUpdateAsync(bool silentIfUpToDate)
    {
        try
        {
            var check = await _appUpdateService.CheckForUpdateAsync();
            AppUpdateButton.Visibility = check.HasUpdate
                ? Microsoft.UI.Xaml.Visibility.Visible
                : Microsoft.UI.Xaml.Visibility.Collapsed;

            if (!check.HasUpdate)
            {
                return;
            }

            if (silentIfUpToDate)
            {
                return;
            }

            var details = string.IsNullOrWhiteSpace(check.Notes)
                ? string.Empty
                : $"\nNotas: {check.Notes}";
            var published = string.IsNullOrWhiteSpace(check.PublishedAt)
                ? string.Empty
                : $"\nPublicada em: {check.PublishedAt}";

            var dialog = new ContentDialog
            {
                Title = "Atualização disponível",
                Content = $"Versão atual: {check.CurrentVersion}\nNova versão: {check.LatestVersion}{published}{details}\nFonte: {check.Source}\n\nDeseja atualizar agora?",
                PrimaryButtonText = "Atualizar agora",
                CloseButtonText = "Depois",
                XamlRoot = XamlRoot,
            };

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary)
            {
                return;
            }

            var applyResult = await _appUpdateService.StartUpdateAsync(check);
            if (applyResult.Success)
            {
                _viewModel.StatusText = applyResult.Message;
                Application.Current.Exit();
                return;
            }

            await ShowMessageAsync(applyResult.Message, "Atualizacao do aplicativo");
        }
        catch (Exception ex)
        {
            await ShowMessageAsync($"Falha ao verificar atualizacao: {ex.Message}", "Atualizacao do aplicativo");
        }
    }

    private async Task<string?> PickManualInstallerFolderAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");

        var window = App.MainWindow;
        if (window == null)
        {
            return null;
        }

        var hwnd = WindowNative.GetWindowHandle(window);
        InitializeWithWindow.Initialize(picker, hwnd);

        var folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }

    private Task RefreshBiometricLicenseStatusAsync(string? message = null)
    {
        var installed = _biometricLicenseBackupService.IsLicenseInstalled();
        var backupExists = _biometricLicenseBackupService.HasLocalBackup();
        _viewModel.UpdateBiometricLicenseStatus(installed, backupExists, message);
        _viewModel.UpdateBiometricLicenseMetadata(
            _biometricLicenseBackupService.TryGetDetectedLicenseFilePath(),
            _biometricLicenseBackupService.TryGetDetectedLicenseLastWriteTime());
        return Task.CompletedTask;
    }

    private async void OnBiometricLicenseStatusRefreshClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        await RefreshBiometricLicenseStatusAsync();
    }

    private void OnToggleBiometricDetailsClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        _viewModel.IsBiometricDetailsExpanded = !_viewModel.IsBiometricDetailsExpanded;
    }

    private async void OnBiometricLicenseBackupClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        try
        {
            var result = await _biometricLicenseBackupService.BackupLocalAsync();
            await RefreshBiometricLicenseStatusAsync(result.Message);
        }
        catch (Exception ex)
        {
            await RefreshBiometricLicenseStatusAsync($"Falha ao executar backup: {ex.Message}");
        }
    }

    private async void OnBiometricLicenseRestoreClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        try
        {
            var result = await _biometricLicenseBackupService.RestoreLocalAsync();
            await RefreshBiometricLicenseStatusAsync(result.Message);
        }
        catch (Exception ex)
        {
            await RefreshBiometricLicenseStatusAsync($"Falha ao restaurar backup: {ex.Message}");
        }
    }
}
