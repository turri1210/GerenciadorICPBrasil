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
using Microsoft.UI.Xaml;
using GerenciadorIcpBrasil.Modules.ConfigAuditoria.Commands;
using GerenciadorIcpBrasil.Modules.ConfigAuditoria.Models;
using GerenciadorIcpBrasil.Modules.ConfigAuditoria.Services;
using GerenciadorIcpBrasil.Modules.ConfigAuditoria.Logging;
using GerenciadorIcpBrasil.Services;
using GerenciadorIcpBrasil.Security;

namespace GerenciadorIcpBrasil.Modules.ConfigAuditoria.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    private readonly IConfigurationAuditService _auditService;
    private readonly ElevatedConfigurationRunner _elevatedRunner = new();
    private bool _isBusy;
    private readonly DispatcherTimer _clockTimer;
    private DateTime _currentDateTime;
    private readonly IReadOnlyList<string> _macAddresses;

    public MainViewModel(IConfigurationAuditService auditService, IConfigAuditoriaUiService? uiService = null)
    {
        _auditService = auditService;
        _uiService = uiService;
        Categories = new ObservableCollection<ConfigurationCategory>(CreateDefaultCategories());

        foreach (var category in Categories)
        {
            category.SetWaiting("Aguardando");
        }

        RefreshCommand = new AsyncRelayCommand(RefreshStatusesAsync, () => !IsBusy);
        RemediateCommand = new AsyncRelayCommand<ConfigurationCategory>(
            RemediateAsync,
            category => !IsBusy && category is { IsInteractiveRemediationAvailable: true });
        SaveEvidenceCommand = new AsyncRelayCommand(SaveEvidenceAsync, () => !IsBusy);

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

    public AsyncRelayCommand<ConfigurationCategory> RemediateCommand { get; }

    public AsyncRelayCommand SaveEvidenceCommand { get; }

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
            RemediateCommand.RaiseCanExecuteChanged();
            SaveEvidenceCommand.RaiseCanExecuteChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public Task InitializeAsync(CancellationToken cancellationToken = default) =>
        ExecuteBusyStateAsync(
            ct => RefreshStatusesInternalAsync(requestAdministrativeAssessment: false, ct),
            cancellationToken);

    public string BuildEvidenceReport(string screenshotPath)
    {
        var builder = new StringBuilder();
        builder.AppendLine("========================================");
        builder.AppendLine("Evidências Geradas pelo ConfigAuditoria");
        builder.AppendLine($"Data e hora: {DateTime.Now:dd/MM/yyyy HH:mm:ss}");
        builder.AppendLine($"Hostname: {HostName}");
        builder.AppendLine("Enderecos MAC:");

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
            builder.AppendLine($"- {category.DisplayName}: {TranslateStatus(category.Status)} - {category.StatusMessage}");
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

    private async Task RefreshStatusesAsync()
    {
        if (_uiService is not null)
        {
            await _uiService.ShowInfoAsync(
                "Para verificar algumas configurações protegidas do Windows, será necessário autorizar a elevação no Controle de Conta de Usuário (UAC).\n\n" +
                "A verificação não altera o computador. Se a autorização não for concedida, as demais verificações continuarão normalmente e apenas as configurações protegidas ficarão como não verificadas.");
        }

        await ExecuteBusyStateAsync(
            ct => RefreshStatusesInternalAsync(requestAdministrativeAssessment: true, ct));
    }

    private async Task RemediateAsync(ConfigurationCategory? category)
    {
        if (category is null || _uiService is null)
        {
            return;
        }

        var summary = GetMachineChangeSummary(category.Key);
        var confirmed = await _uiService.ConfirmAsync(
            $"Você está prestes a executar a ação abaixo:\n\n{category.DisplayName}: {summary}\n\n" +
            "A ação ocorre somente após esta confirmação. Configurações administrativas também exibirão o controle de conta do Windows (UAC). Deseja continuar?");

        if (!confirmed)
        {
            return;
        }

        await ExecuteBusyStateAsync(ct => RemediateCategoryAsync(category, ct));
    }

    private async Task SaveEvidenceAsync()
    {
        if (_uiService is null)
        {
            return;
        }

        try
        {
            await _uiService.SaveEvidenceAsync(BuildEvidenceReport);
        }
        catch (Exception ex)
        {
            LogWriter.Write(ex, "Falha ao salvar evidências.");
            await _uiService.ShowErrorAsync($"Não foi possível salvar as evidências: {ex.Message}");
        }
    }

    private async Task RemediateCategoryAsync(ConfigurationCategory category, CancellationToken cancellationToken)
    {
        category.BeginLoading("Aplicando configuração...");
        ConfigurationAssessmentResult? elevatedAssessment = null;

        try
        {
            if (ElevatedConfigurationRunner.RequiresElevation(category.Key))
            {
                var elevatedResult = await _elevatedRunner.ApplyAsync(category.Key, cancellationToken);
                if (elevatedResult.WasCancelled)
                {
                    category.ApplyAssessment(category.Status, "Elevação cancelada pelo usuário; nenhuma alteração foi aplicada.");
                    return;
                }

                if (!elevatedResult.Succeeded)
                {
                    throw new InvalidOperationException(elevatedResult.Error ?? "Falha na execução administrativa.");
                }

                elevatedAssessment = elevatedResult.Assessment;
            }
            else
            {
                await _auditService.ApplyFixAsync(category.Key, cancellationToken);
            }

            if (string.Equals(category.Key, "antivirus", StringComparison.OrdinalIgnoreCase))
            {
                category.ApplyAssessment(
                    ConfigurationStatus.Pending,
                    "Revise a proteção e as atualizações na Segurança do Windows; depois clique em Verificar configurações.");
                return;
            }

            if (string.Equals(category.Key, "criptografia", StringComparison.OrdinalIgnoreCase))
            {
                category.ApplyAssessment(
                    ConfigurationStatus.Pending,
                    "Políticas preparadas. Conclua a ativação no BitLocker e depois clique em Verificar configurações.");
                return;
            }

            category.BeginLoading("Verificando...");
            var result = elevatedAssessment ??
                await _auditService.EvaluateCategoryAsync(category.Key, cancellationToken);
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

    private async Task RefreshStatusesInternalAsync(
        bool requestAdministrativeAssessment,
        CancellationToken cancellationToken)
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

            await RefreshPrivilegedSecurityPoliciesAsync(
                requestAdministrativeAssessment,
                cancellationToken);
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

    private async Task RefreshPrivilegedSecurityPoliciesAsync(
        bool requestAdministrativeAssessment,
        CancellationToken cancellationToken)
    {
        var privilegedCategories = Categories
            .Where(category => SecurityPolicy.RequiresElevatedAssessment(category.Key))
            .ToArray();

        if (!requestAdministrativeAssessment)
        {
            foreach (var category in privilegedCategories)
            {
                category.ApplyAssessment(
                    ConfigurationStatus.Unknown,
                    "Clique em Verificar configurações para autorizar a leitura administrativa desta política.");
            }

            return;
        }

        foreach (var category in privilegedCategories)
        {
            category.BeginLoading("Aguardando autorização para verificar...");
        }

        var elevatedResult = await _elevatedRunner
            .EvaluateSecurityPoliciesAsync(cancellationToken)
            .ConfigureAwait(true);

        if (elevatedResult.WasCancelled)
        {
            foreach (var category in privilegedCategories)
            {
                category.ApplyAssessment(
                    ConfigurationStatus.Unknown,
                    "A verificação administrativa foi cancelada; nenhuma configuração foi alterada.");
            }

            return;
        }

        if (!elevatedResult.Succeeded)
        {
            foreach (var category in privilegedCategories)
            {
                category.ApplyAssessment(
                    ConfigurationStatus.Unknown,
                    $"Não foi possível verificar a política: {elevatedResult.Error}");
            }

            return;
        }

        foreach (var category in privilegedCategories)
        {
            if (elevatedResult.Assessments.TryGetValue(category.Key, out var assessment))
            {
                category.ApplyAssessment(assessment.Status, assessment.Message);
            }
            else
            {
                category.ApplyAssessment(
                    ConfigurationStatus.Unknown,
                    "O executor administrativo não retornou esta verificação.");
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

    private static string GetMachineChangeSummary(string categoryKey) => categoryKey switch
    {
        "antivirus" => "abre a Segurança do Windows para você revisar a proteção e as atualizações.",
        "windows-update" => "procura, baixa e instala automaticamente as atualizações recomendadas pela API oficial do Windows. Atualizações opcionais, drivers, versões Preview, beta e atualizações de recurso não serão instaladas. Se necessário, os termos da atualização serão aceitos. O computador não será reiniciado automaticamente.",
        "protecao-tela" => "ajusta a proteção de tela somente do usuário atual.",
        "login-remoto" => "desativa Assistência Remota e conexões remotas por políticas locais.",
        "senha-forte" => "aplica valores fixos à política local de senhas.",
        "bloqueio-conta" => "aplica valores fixos à política local de bloqueio de conta.",
        "auditoria-estacoes" => "aplica políticas predefinidas de auditoria do Windows.",
        "contas-usuarios" => "abre o plano de contas locais para sua revisão antes de aplicar.",
        "visualizador-eventos" => "ajusta a ativação e o tamanho do log de Aplicativo.",
        "firewall" => "ativa e ajusta os perfis predefinidos do Windows Firewall.",
        "sincronismo-hora" => "configura a fonte NTP predefinida e sincroniza a hora.",
        "integridade" => "configura auditoria somente nas pastas biométricas especificadas.",
        "criptografia" => "prepara políticas do BitLocker; a ativação é concluída no Windows.",
        _ => "executa uma ação local predefinida."
    };

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private readonly IConfigAuditoriaUiService? _uiService;

    private static string TranslateStatus(ConfigurationStatus status) =>
        status switch
        {
            ConfigurationStatus.Compliant => "Em conformidade",
            ConfigurationStatus.NonCompliant => "Não Conforme",
            ConfigurationStatus.Pending => "Pendente",
            _ => "Desconhecido"
        };

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
            LogWriter.Write(ex, "Falha ao obter enderecos MAC.");
            return Array.Empty<string>();
        }
    }
}
