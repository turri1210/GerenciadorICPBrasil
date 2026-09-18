using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using ConfigAuditoria.Commands;
using ConfigAuditoria.Models;
using ConfigAuditoria.Services;
using ConfigAuditoria.Logging;

namespace ConfigAuditoria.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    private readonly IConfigurationAuditService _auditService;
    private bool _isBusy;
    private readonly DispatcherTimer _clockTimer;
    private DateTime _currentDateTime;
    private readonly IReadOnlyList<string> _macAddresses;

    public MainViewModel(IConfigurationAuditService auditService)
    {
        _auditService = auditService;
        Categories = new ObservableCollection<ConfigurationCategory>(CreateDefaultCategories());

        foreach (var category in Categories)
        {
            category.SetWaiting("Aguardando");
        }

        RefreshCommand = new AsyncRelayCommand(RefreshStatusesAsync, () => !IsBusy);
        ApplyAllCommand = new AsyncRelayCommand(
            ApplyAllAsync,
            () => !IsBusy && Categories.Any(category => category.IsRemediationAvailable));
        RemediateCommand = new AsyncRelayCommand<ConfigurationCategory>(
            RemediateAsync,
            category => !IsBusy && category is { IsInteractiveRemediationAvailable: true });

        foreach (var category in Categories)
        {
            category.PropertyChanged += OnCategoryPropertyChanged;
        }

        HostName = Environment.MachineName;
        _macAddresses = GetMacAddresses();
        CurrentDateTime = DateTime.Now;

        _clockTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _clockTimer.Tick += (_, _) => CurrentDateTime = DateTime.Now;
        _clockTimer.Start();
    }

    public ObservableCollection<ConfigurationCategory> Categories { get; }

    public AsyncRelayCommand RefreshCommand { get; }

    public AsyncRelayCommand ApplyAllCommand { get; }

    public AsyncRelayCommand<ConfigurationCategory> RemediateCommand { get; }

    public string HostName { get; }

    public IReadOnlyList<string> MacAddresses => _macAddresses;

    public string MacAddressDisplay => _macAddresses.Count == 0
        ? "Sem interfaces ativas identificadas."
        : string.Join(Environment.NewLine, _macAddresses);

    public DateTime CurrentDateTime
    {
        get => _currentDateTime;
        private set
        {
            if (_currentDateTime == value)
            {
                return;
            }

            _currentDateTime = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CurrentDateTimeDisplay));
            OnPropertyChanged(nameof(CurrentDateDisplay));
            OnPropertyChanged(nameof(CurrentTimeDisplay));
        }
    }

    public string CurrentDateTimeDisplay => CurrentDateTime.ToString("dd/MM/yyyy HH:mm:ss");

    public string CurrentDateDisplay => CurrentDateTime.ToString("dd/MM/yyyy");

    public string CurrentTimeDisplay => CurrentDateTime.ToString("HH:mm:ss");

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (_isBusy == value)
            {
                return;
            }

            _isBusy = value;
            OnPropertyChanged();
            RefreshCommand.RaiseCanExecuteChanged();
            ApplyAllCommand.RaiseCanExecuteChanged();
            RemediateCommand.RaiseCanExecuteChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public Task InitializeAsync(CancellationToken cancellationToken = default) =>
        ExecuteBusyStateAsync(RefreshStatusesInternalAsync, cancellationToken);

    public string BuildEvidenceReport(string screenshotPath)
    {
        var builder = new StringBuilder();
        builder.AppendLine("========================================");
        builder.AppendLine("Evidências Geradas pelo ConfigAuditoria");
        builder.AppendLine($"Data e hora: {DateTime.Now:dd/MM/yyyy HH:mm:ss}");
        builder.AppendLine($"Hostname: {HostName}");
        builder.AppendLine("Endereços MAC:");

        if (_macAddresses.Count == 0)
        {
            builder.AppendLine("  - Não foi possível identificar interfaces ativas.");
        }
        else
        {
            foreach (var mac in _macAddresses)
            {
                builder.AppendLine($"  - {mac}");
            }
        }

        builder.AppendLine();
        builder.AppendLine("Resumo das configurações avaliadas:");

        foreach (var category in Categories)
        {
            builder.AppendLine($"- {category.DisplayName}: {category.Status} - {category.StatusMessage}");
        }

        if (!string.IsNullOrWhiteSpace(screenshotPath))
        {
            builder.AppendLine();
            builder.AppendLine($"Captura de tela armazenada em: {screenshotPath}");
        }

        builder.AppendLine("========================================");
        builder.AppendLine();

        return builder.ToString();
    }

    private Task RefreshStatusesAsync() => ExecuteBusyStateAsync(RefreshStatusesInternalAsync);

    private Task RemediateAsync(ConfigurationCategory? category)
    {
        if (category is null)
        {
            return Task.CompletedTask;
        }

        if (!ConfirmMachineChange(new[] { category }))
        {
            category.ApplyAssessment(category.Status, "Alteração cancelada pelo usuário.");
            LogWriter.Write($"Alteração de máquina cancelada pelo usuário: {category.Key}.");
            return Task.CompletedTask;
        }

        return ExecuteBusyStateAsync(ct => RemediateCategoryAsync(category, ct));
    }

    private Task ApplyAllAsync() => ExecuteBusyStateAsync(ApplyAllInternalAsync);

    private async Task ApplyAllInternalAsync(CancellationToken cancellationToken)
    {
        var pendingCategories = Categories
            .Where(category => category.IsRemediationAvailable)
            .ToList();

        if (pendingCategories.Count == 0 || !ConfirmMachineChange(pendingCategories))
        {
            LogWriter.Write("Aplicação em lote de configurações cancelada pelo usuário.");
            return;
        }

        foreach (var category in pendingCategories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await RemediateCategoryAsync(category, cancellationToken);
        }
    }

    private async Task RemediateCategoryAsync(ConfigurationCategory category, CancellationToken cancellationToken)
    {
        category.BeginLoading("Aplicando configuração...");

        try
        {
            await _auditService.ApplyFixAsync(category.Key, cancellationToken);

            category.BeginLoading("Verificando...");
            var result = await _auditService.EvaluateCategoryAsync(category.Key, cancellationToken);
            category.ApplyAssessment(result.Status, result.Message);
        }
        catch (OperationCanceledException)
        {
            category.ApplyAssessment(ConfigurationStatus.Unknown, "Operação cancelada.");
            throw;
        }
        catch (Exception ex)
        {
            LogWriter.Write(ex, $"Falha ao corrigir categoria {category.Key}");
            category.ApplyAssessment(ConfigurationStatus.Unknown, $"Erro ao corrigir: {ex.Message}");
        }
        finally
        {
            category.EndLoading();
        }
    }

    private static bool ConfirmMachineChange(IReadOnlyCollection<ConfigurationCategory> categories)
    {
        var changes = string.Join(
            Environment.NewLine,
            categories.Select(category => $"• {category.DisplayName}: {GetMachineChangeSummary(category.Key)}"));

        var message =
            "Você está prestes a aplicar configurações locais do Windows.\n\n" +
            changes +
            "\n\nAs alterações ocorrem somente após esta confirmação e não são executadas em segundo plano. " +
            "Deseja continuar?";

        return MessageBox.Show(
                   message,
                   "Confirmar configuração de máquina",
                   MessageBoxButton.YesNo,
                   MessageBoxImage.Warning,
                   MessageBoxResult.No) == MessageBoxResult.Yes;
    }

    private static string GetMachineChangeSummary(string categoryKey) => categoryKey switch
    {
        "antivirus" => "orienta a ativação e atualização do Microsoft Defender.",
        "windows-update" => "orienta as configurações do Windows Update.",
        "protecao-tela" => "ajusta a proteção de tela do usuário atual.",
        "login-remoto" => "desativa Assistência Remota e conexões remotas.",
        "senha-forte" => "ajusta a política local de senha.",
        "bloqueio-conta" => "ajusta a política local de bloqueio de conta.",
        "auditoria-estacoes" => "ajusta políticas de auditoria do Windows.",
        "contas-usuarios" => "cria ou ajusta contas e grupos locais conforme o plano revisado.",
        "visualizador-eventos" => "ajusta o tamanho e a ativação do log de Aplicativo.",
        "firewall" => "ajusta perfis e registros do Windows Firewall.",
        "sincronismo-hora" => "ajusta a fonte NTP e sincroniza a hora.",
        "criptografia" => "prepara políticas do BitLocker; a criptografia é concluída no assistente do Windows.",
        "integridade" => "configura auditoria nas pastas biométricas especificadas.",
        _ => "aplica uma configuração local do Windows."
    };

    private async Task ExecuteBusyStateAsync(
        Func<CancellationToken, Task> work,
        CancellationToken cancellationToken = default)
    {
        if (work is null || IsBusy)
        {
            return;
        }

        IsBusy = true;

        try
        {
            await work(cancellationToken);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RefreshStatusesInternalAsync(CancellationToken cancellationToken)
    {
        foreach (var category in Categories)
        {
            category.SetWaiting("Aguardando");
        }

        var progress = new Progress<(string Key, ConfigurationAssessmentResult Result)>(update =>
        {
            var category = Categories.FirstOrDefault(c => string.Equals(c.Key, update.Key, StringComparison.OrdinalIgnoreCase));
            category?.ApplyAssessment(update.Result.Status, update.Result.Message);
        });

        var stageProgress = new Progress<string>(key =>
        {
            var category = Categories.FirstOrDefault(c => string.Equals(c.Key, key, StringComparison.OrdinalIgnoreCase));
            category?.BeginLoading("Verificando...");
        });

        try
        {
            var results = await _auditService.EvaluateAsync(progress, stageProgress, cancellationToken);

            foreach (var category in Categories)
            {
                if (results.TryGetValue(category.Key, out var result))
                {
                    category.ApplyAssessment(result.Status, result.Message);
                }
                else
                {
                    category.ResetAssessment();
                }
            }
        }
        catch (OperationCanceledException)
        {
            foreach (var category in Categories)
            {
                category.EndLoading();
            }

            throw;
        }
        catch (Exception ex)
        {
            LogWriter.Write(ex, "Falha durante a verificação das configurações");

            foreach (var category in Categories)
            {
                category.ApplyAssessment(ConfigurationStatus.Unknown, $"Erro ao verificar: {ex.Message}");
            }
        }
    }

    private static IEnumerable<ConfigurationCategory> CreateDefaultCategories()
    {
        yield return new ConfigurationCategory("sistema-operacional", "Sistema operacional");
        yield return new ConfigurationCategory("antivirus", "Antivirus");
        yield return new ConfigurationCategory("windows-update", "Windows Update");
        yield return new ConfigurationCategory("protecao-tela", "Proteção de tela");
        yield return new ConfigurationCategory("login-remoto", "Login remoto");
        yield return new ConfigurationCategory("senha-forte", "Senha forte");
        yield return new ConfigurationCategory("bloqueio-conta", "Bloqueio de conta");
        yield return new ConfigurationCategory("auditoria-estacoes", "Auditoria nas estações de trabalho");
        yield return new ConfigurationCategory("contas-usuarios", "Configuração de Contas de Usuários");
        yield return new ConfigurationCategory("visualizador-eventos", "Visualizador de Eventos");
        yield return new ConfigurationCategory("firewall", "Firewall");
        yield return new ConfigurationCategory("sincronismo-hora", "Sincronismo de hora");
        yield return new ConfigurationCategory("integridade", "Configurações de Integridade");
        yield return new ConfigurationCategory("criptografia", "Criptografia");
    }

    private void OnCategoryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ConfigurationCategory.IsRemediationAvailable))
        {
            ApplyAllCommand.RaiseCanExecuteChanged();
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private static IReadOnlyList<string> GetMacAddresses()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(nic =>
                    nic.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                    nic.NetworkInterfaceType != NetworkInterfaceType.Tunnel &&
                    nic.GetPhysicalAddress().GetAddressBytes().Length >= 6)
                .Select(nic => nic.GetPhysicalAddress())
                .Select(address => string.Join(":", address.GetAddressBytes().Select(b => b.ToString("X2"))))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex)
        {
            LogWriter.Write(ex, "Falha ao obter endereços MAC.");
            return Array.Empty<string>();
        }
    }
}
