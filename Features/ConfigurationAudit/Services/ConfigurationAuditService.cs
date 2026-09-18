using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.DirectoryServices.AccountManagement;
using System.Security;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using GerenciadorIcpBrasil.Modules.ConfigAuditoria.Logging;
using GerenciadorIcpBrasil.Modules.ConfigAuditoria.Models;
using GerenciadorIcpBrasil.Security;
using Microsoft.Win32;

namespace GerenciadorIcpBrasil.Modules.ConfigAuditoria.Services;

public class ConfigurationAuditService : IConfigurationAuditService
{
    private static IConfigAuditoriaUiService? UiService;

    public ConfigurationAuditService(IConfigAuditoriaUiService? uiService = null)
    {
        UiService = uiService ?? UiService;
    }
    private static readonly HashSet<string> AllowedEditionIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "Professional",
        "ProfessionalN",
        "ProfessionalCountrySpecific",
        "ProfessionalSingleLanguage",
        "ProfessionalEducation",
        "ProfessionalEducationN",
        "Enterprise",
        "EnterpriseN",
        "ProfessionalWorkstation",
        "ProfessionalWorkstationN"
    };

    private static readonly HashSet<string> BlockedEditionIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "Core",
        "CoreN",
        "CoreSingleLanguage",
        "Education",
        "EducationN"
    };

    private const long ApplicationLogMaxSizeBytes = 4194240L * 1024;
    private const int FirewallLogSizeKilobytes = 4096;
    private const string TimeSyncServer = "ntp.certisign.com.br";
    private const string TimeSyncServerManualEntry = TimeSyncServer + ",0x9";
    private const int TimeSyncSpecialPollIntervalSeconds = 3600;
    private static readonly string[] IntegrityAuditDirectories =
    {
        @"C:\Program Files (x86)\Certibio\BiometricLocalServicePlataform",
        @"C:\Users\Public\Documents\BiometricLocalServicePlataform"
    };

    // Ações externas são limitadas aos utilitários nativos necessários. A interface
    // nunca fornece comandos, scripts ou caminhos executáveis a este serviço.
    private static readonly HashSet<string> ApprovedSystemExecutables = new(StringComparer.OrdinalIgnoreCase)
    {
        "auditpol.exe",
        "net.exe",
        "secedit.exe",
        "wevtutil.exe",
        "w32tm.exe"
    };

    private static readonly HashSet<string> ApprovedRemediationCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        "antivirus", "protecao-tela", "login-remoto", "senha-forte",
        "bloqueio-conta", "auditoria-estacoes", "visualizador-eventos", "firewall",
        "sincronismo-hora", "criptografia", "integridade", "contas-usuarios"
    };

    private static readonly string ScopedTemporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        "GerenciadorIcpBrasil",
        "ConfigAuditoria");

    private const string MinimumDisplayVersionLabel = "25H2";
    private const int MinimumDisplayVersionYear = 25;
    private const int MinimumDisplayVersionHalf = 2;

    [Flags]
    private enum NetFwProfileType2
    {
        Domain = 1,
        Private = 2,
        Public = 4
    }

    private const string FirewallPolicyRegistryPath = @"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy";

    private static readonly (NetFwProfileType2 Profile, string DisplayName, string RegistryName)[] FirewallProfiles =
    {
        (NetFwProfileType2.Domain, "rede de domínio", "DomainProfile"),
        (NetFwProfileType2.Private, "rede privada", "StandardProfile"),
        (NetFwProfileType2.Public, "rede pública", "PublicProfile")
    };

    private static readonly string[] BaseAuditPolicyKeys =
    {
        "AuditAccountLogon",
        "AuditAccountManage",
        "AuditDSAccess",
        "AuditLogonEvents",
        "AuditObjectAccess",
        "AuditPolicyChange",
        "AuditPrivilegeUse",
        "AuditProcessTracking",
        "AuditSystemEvents"
    };

    private enum AuditSettingState
    {
        NotConfigured,
        NoAuditing,
        SuccessOnly,
        FailureOnly,
        SuccessAndFailure
    }

    private static readonly Dictionary<string, string> AuditSubcategoryIds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Credential Validation"] = "0CCE923F-69AE-11D9-BED3-505054503030",
        ["Kerberos Service Ticket Operations"] = "0CCE9240-69AE-11D9-BED3-505054503030",
        ["Other Account Logon Events"] = "0CCE9241-69AE-11D9-BED3-505054503030",
        ["Kerberos Authentication Service"] = "0CCE9242-69AE-11D9-BED3-505054503030",
        ["Application Group Management"] = "0CCE9239-69AE-11D9-BED3-505054503030",
        ["Computer Account Management"] = "0CCE9236-69AE-11D9-BED3-505054503030",
        ["Distribution Group Management"] = "0CCE9238-69AE-11D9-BED3-505054503030",
        ["Other Account Management Events"] = "0CCE923A-69AE-11D9-BED3-505054503030",
        ["Security Group Management"] = "0CCE9237-69AE-11D9-BED3-505054503030",
        ["User Account Management"] = "0CCE9235-69AE-11D9-BED3-505054503030",
        ["DPAPI Activity"] = "0CCE922D-69AE-11D9-BED3-505054503030",
        ["Plug and Play Events"] = "0CCE9248-69AE-11D9-BED3-505054503030",
        ["Process Creation"] = "0CCE922B-69AE-11D9-BED3-505054503030",
        ["Process Termination"] = "0CCE922C-69AE-11D9-BED3-505054503030",
        ["RPC Events"] = "0CCE922E-69AE-11D9-BED3-505054503030",
        ["Token Right Adjusted Events"] = "0CCE924A-69AE-11D9-BED3-505054503030",
        ["Account Lockout"] = "0CCE9217-69AE-11D9-BED3-505054503030",
        ["User / Device Claims"] = "0CCE9247-69AE-11D9-BED3-505054503030",
        ["Group Membership"] = "0CCE9249-69AE-11D9-BED3-505054503030",
        ["IPsec Extended Mode"] = "0CCE921A-69AE-11D9-BED3-505054503030",
        ["IPsec Main Mode"] = "0CCE9218-69AE-11D9-BED3-505054503030",
        ["IPsec Quick Mode"] = "0CCE9219-69AE-11D9-BED3-505054503030",
        ["Logoff"] = "0CCE9216-69AE-11D9-BED3-505054503030",
        ["Logon"] = "0CCE9215-69AE-11D9-BED3-505054503030",
        ["Network Policy Server"] = "0CCE9243-69AE-11D9-BED3-505054503030",
        ["Other Logon/Logoff Events"] = "0CCE921C-69AE-11D9-BED3-505054503030",
        ["Special Logon"] = "0CCE921B-69AE-11D9-BED3-505054503030",
        ["Application Generated"] = "0CCE9222-69AE-11D9-BED3-505054503030",
        ["Certification Services"] = "0CCE9221-69AE-11D9-BED3-505054503030",
        ["Detailed File Share"] = "0CCE9244-69AE-11D9-BED3-505054503030",
        ["File Share"] = "0CCE9224-69AE-11D9-BED3-505054503030",
        ["File System"] = "0CCE921D-69AE-11D9-BED3-505054503030",
        ["Filtering Platform Connection"] = "0CCE9226-69AE-11D9-BED3-505054503030",
        ["Filtering Platform Packet Drop"] = "0CCE9225-69AE-11D9-BED3-505054503030",
        ["Handle Manipulation"] = "0CCE9223-69AE-11D9-BED3-505054503030",
        ["Kernel Object"] = "0CCE921F-69AE-11D9-BED3-505054503030",
        ["Other Object Access Events"] = "0CCE9227-69AE-11D9-BED3-505054503030",
        ["Registry"] = "0CCE921E-69AE-11D9-BED3-505054503030",
        ["Removable Storage"] = "0CCE9245-69AE-11D9-BED3-505054503030",
        ["SAM"] = "0CCE9220-69AE-11D9-BED3-505054503030",
        ["Central Policy Staging"] = "0CCE9246-69AE-11D9-BED3-505054503030",
        ["Audit Policy Change"] = "0CCE922F-69AE-11D9-BED3-505054503030",
        ["Authentication Policy Change"] = "0CCE9230-69AE-11D9-BED3-505054503030",
        ["Authorization Policy Change"] = "0CCE9231-69AE-11D9-BED3-505054503030",
        ["Filtering Platform Policy Change"] = "0CCE9233-69AE-11D9-BED3-505054503030",
        ["MPSSVC Rule-Level Policy Change"] = "0CCE9232-69AE-11D9-BED3-505054503030",
        ["Other Policy Change Events"] = "0CCE9234-69AE-11D9-BED3-505054503030",
        ["Non Sensitive Privilege Use"] = "0CCE9229-69AE-11D9-BED3-505054503030",
        ["Other Privilege Use Events"] = "0CCE922A-69AE-11D9-BED3-505054503030",
        ["Sensitive Privilege Use"] = "0CCE9228-69AE-11D9-BED3-505054503030",
        ["IPsec Driver"] = "0CCE9213-69AE-11D9-BED3-505054503030",
        ["Other System Events"] = "0CCE9214-69AE-11D9-BED3-505054503030",
        ["Security State Change"] = "0CCE9210-69AE-11D9-BED3-505054503030",
        ["Security System Extension"] = "0CCE9211-69AE-11D9-BED3-505054503030",
        ["System Integrity"] = "0CCE9212-69AE-11D9-BED3-505054503030"
    };


    private sealed record AdvancedAuditSetting(string Name, AuditSettingState ExpectedState, string SubcategoryId, params string[] Aliases)
    {
        public IEnumerable<string> GetAllNames()
        {
            yield return Name;
            foreach (var alias in Aliases)
            {
                yield return alias;
            }
        }
    }

    private sealed record LocalAccountInfo(string Name, string Sid, bool Enabled);

    private sealed class UserAccountEvaluation
    {
        public bool AdministratorDisabled { get; set; }
        public bool GuestDisabled { get; set; }
        public List<string> CustomAdministrators { get; } = new();
        public List<string> StandardUsers { get; } = new();
        public List<LocalAccountInfo> Snapshot { get; } = new();
        public List<string> Issues { get; } = new();
    }

    private sealed class AuditPolCsvDocument
    {
        public AuditPolCsvDocument(string header, List<AuditPolCsvRecord> records)
        {
            Header = header;
            Records = records;
        }

        public string Header { get; }
        public List<AuditPolCsvRecord> Records { get; }

        public IEnumerable<string> ToCsvLines()
        {
            yield return Header;
            foreach (var record in Records)
            {
                yield return record.ToCsvLine();
            }
        }
    }

    private sealed class AuditPolCsvRecord
    {
        private string[] _columns;

        public AuditPolCsvRecord(string[] columns)
        {
            _columns = columns;
        }

        public string[] Columns => _columns;

        public string ComputerName
        {
            get => GetColumn(0);
            set => SetColumn(0, value);
        }

        public string PolicyTarget
        {
            get => GetColumn(1);
            set => SetColumn(1, value);
        }

        public string Subcategory
        {
            get => GetColumn(2);
            set => SetColumn(2, value);
        }

        public string SubcategoryGuid
        {
            get => GetColumn(3);
            set => SetColumn(3, value);
        }

        public string IncludeSetting
        {
            get => GetColumn(4);
            set => SetColumn(4, value);
        }

        public string ExcludeSetting
        {
            get => GetColumn(5);
            set => SetColumn(5, value);
        }

        public string ConfigurationValue
        {
            get => GetColumn(6);
            set => SetColumn(6, value);
        }

        private string GetColumn(int index) =>
            index < _columns.Length ? _columns[index] : string.Empty;

        private void SetColumn(int index, string value)
        {
            EnsureLength(index + 1);
            _columns[index] = value ?? string.Empty;
        }

        private void EnsureLength(int length)
        {
            if (_columns.Length >= length)
            {
                return;
            }

            Array.Resize(ref _columns, length);
            for (var i = 0; i < _columns.Length; i++)
            {
                _columns[i] ??= string.Empty;
            }
        }

        public string ToCsvLine()
        {
            var builder = new StringBuilder();
            for (var i = 0; i < _columns.Length; i++)
            {
                if (i > 0)
                {
                    builder.Append(',');
                }

                builder.Append(EscapeCsvValue(_columns[i] ?? string.Empty));
            }

            return builder.ToString();
        }

        private static string EscapeCsvValue(string value)
        {
            var needsQuotes = value.Contains('"') || value.Contains(',') || value.Contains('\r') || value.Contains('\n');
            if (value.Contains('"'))
            {
                value = value.Replace("\"", "\"\"");
            }

            return needsQuotes ? $"\"{value}\"" : value;
        }
    }

    private static ConfigurationAssessmentResult AnalyzeFirewall()
    {
        try
        {
            return AnalyzeFirewallUsingCom();
        }
        catch (Exception comEx)
        {
            LogWriter.Write(comEx, "Falha ao verificar firewall via API COM.");
            return new ConfigurationAssessmentResult(
                ConfigurationStatus.Unknown,
                "Não foi possível consultar o Windows Firewall pela API nativa do Windows.");
        }
    }

    private static ConfigurationAssessmentResult AnalyzeFirewallUsingCom() =>
        RunOnStaThread(() =>
        {
            object? policyCom = null;
            dynamic? policy = null;

            try
            {
                var issues = new List<string>();
                var policyType = Type.GetTypeFromProgID("HNetCfg.FwPolicy2")
                                 ?? throw new InvalidOperationException("Não foi possível acessar as configurações do Windows Firewall.");

                policyCom = Activator.CreateInstance(policyType) ?? throw new InvalidOperationException("Não foi possível carregar a política do Windows Firewall.");
                policy = policyCom;

                foreach (var (profile, displayName, registryName) in FirewallProfiles)
                {
                    var profileIndex = (int)profile;

                    bool isEnabled = Convert.ToBoolean(policy.FirewallEnabled[profileIndex]);
                    if (!isEnabled)
                    {
                        issues.Add($"Ative o firewall para o perfil de {displayName}.");
                    }

                    bool blockAllInbound = Convert.ToBoolean(policy.BlockAllInboundTraffic[profileIndex]);
                    if (blockAllInbound)
                    {
                        issues.Add($"Desmarque 'Bloquear todas as conexões de entrada' para o perfil de {displayName}.");
                    }

                    bool notificationsDisabled = Convert.ToBoolean(policy.NotificationsDisabled[profileIndex]);
                    if (notificationsDisabled)
                    {
                        issues.Add($"Habilite as notificações de bloqueio de novos aplicativos no perfil de {displayName}.");
                    }

                    using var loggingKey = Registry.LocalMachine.OpenSubKey(
                        $@"{FirewallPolicyRegistryPath}\{registryName}\Logging",
                        writable: false);
                    if (loggingKey is null)
                    {
                        issues.Add($"Não foi possível ler as configurações de log do perfil de {displayName}.");
                        continue;
                    }

                    var logSizeKilobytes = ReadRegistryDword(loggingKey, "LogFileSize");
                    if (logSizeKilobytes != FirewallLogSizeKilobytes)
                    {
                        issues.Add($"Defina o limite do log para {FirewallLogSizeKilobytes} KB no perfil de {displayName}.");
                    }

                    if (ReadRegistryDword(loggingKey, "LogDroppedPackets") != 0)
                    {
                        issues.Add($"Desabilite o registro de pacotes removidos no perfil de {displayName}.");
                    }

                    if (ReadRegistryDword(loggingKey, "LogSuccessfulConnections") != 0)
                    {
                        issues.Add($"Desabilite o registro de conexões bem-sucedidas no perfil de {displayName}.");
                    }
                }

                return BuildFirewallAssessment(issues);
            }
            catch
            {
                throw;
            }
            finally
            {
                ReleaseComObject(policyCom);
            }
        });
    private static ConfigurationAssessmentResult BuildFirewallAssessment(IReadOnlyCollection<string> issues)
    {
        if (issues is null || issues.Count == 0)
        {
            return new ConfigurationAssessmentResult(
                ConfigurationStatus.Compliant,
                "Firewall ativo em todos os perfis com log configurado para 4096 KB.");
        }

        return new ConfigurationAssessmentResult(
            ConfigurationStatus.NonCompliant,
            "Ajuste o Windows Firewall: " + string.Join(" ", issues));
    }

    private static readonly AdvancedAuditSetting[] AdvancedAuditSettings =
    {
        // Account Logon
        new("Credential Validation", AuditSettingState.FailureOnly, AuditSubcategoryIds["Credential Validation"], "auditoria de validação de credenciais"),
        new("Kerberos Authentication Service", AuditSettingState.NotConfigured, AuditSubcategoryIds["Kerberos Authentication Service"], "auditoria de serviço de autenticação kerberos"),
        new("Kerberos Service Ticket Operations", AuditSettingState.NotConfigured, AuditSubcategoryIds["Kerberos Service Ticket Operations"], "auditoria de operações do tiquete de serviço kerberos"),

        // Account Management
        new("Application Group Management", AuditSettingState.NotConfigured, AuditSubcategoryIds["Application Group Management"], "auditoria de gerenciamento de grupo de aplicativos"),
        new("Computer Account Management", AuditSettingState.SuccessAndFailure, AuditSubcategoryIds["Computer Account Management"], "auditoria de gerenciamento de conta de computador"),
        new("Distribution Group Management", AuditSettingState.NotConfigured, AuditSubcategoryIds["Distribution Group Management"], "auditoria de gerenciamento de grupo de distribuição"),
        new("Other Account Management Events", AuditSettingState.SuccessAndFailure, AuditSubcategoryIds["Other Account Management Events"], "auditoria de outros eventos de gerenciamento de contas"),
        new("Security Group Management", AuditSettingState.SuccessAndFailure, AuditSubcategoryIds["Security Group Management"], "auditoria de gerenciamento de grupo de segurança"),
        new("User Account Management", AuditSettingState.SuccessAndFailure, AuditSubcategoryIds["User Account Management"], "auditoria de gerenciamento de conta de usuário"),

        // Detailed Tracking
        new("DPAPI Activity", AuditSettingState.NotConfigured, AuditSubcategoryIds["DPAPI Activity"], "auditoria de atividade dpapi"),
        new("Plug and Play Events", AuditSettingState.NotConfigured, AuditSubcategoryIds["Plug and Play Events"], "auditar atividade pnp", "auditoria de eventos plug and play"),
        new("Process Creation", AuditSettingState.NotConfigured, AuditSubcategoryIds["Process Creation"], "auditoria de criação de processo"),
        new("Process Termination", AuditSettingState.FailureOnly, AuditSubcategoryIds["Process Termination"], "auditoria de terminação de processo"),
        new("RPC Events", AuditSettingState.SuccessAndFailure, AuditSubcategoryIds["RPC Events"], "auditoria de eventos de rpc"),
        new("Token Right Adjusted Events", AuditSettingState.NotConfigured, AuditSubcategoryIds["Token Right Adjusted Events"], "auditar direito de token ajustado"),

        // Logon/Logoff
        new("Account Lockout", AuditSettingState.SuccessOnly, AuditSubcategoryIds["Account Lockout"], "auditoria de bloqueio de conta"),
        new("User / Device Claims", AuditSettingState.NotConfigured, AuditSubcategoryIds["User / Device Claims"], "auditar declarações do usuário", "auditar declarações do usuário/dispositivo"),
        new("Group Membership", AuditSettingState.NotConfigured, AuditSubcategoryIds["Group Membership"], "audita associação de grupo"),
        new("IPsec Extended Mode", AuditSettingState.NotConfigured, AuditSubcategoryIds["IPsec Extended Mode"], "auditoria de modo estendido do ipsec"),
        new("IPsec Main Mode", AuditSettingState.NotConfigured, AuditSubcategoryIds["IPsec Main Mode"], "auditoria de modo principal do ipsec"),
        new("IPsec Quick Mode", AuditSettingState.NotConfigured, AuditSubcategoryIds["IPsec Quick Mode"], "auditoria de modo rapido do ipsec"),
        new("Logoff", AuditSettingState.SuccessOnly, AuditSubcategoryIds["Logoff"], "logoff de auditoria"),
        new("Logon", AuditSettingState.FailureOnly, AuditSubcategoryIds["Logon"], "logon de auditoria"),
        new("Network Policy Server", AuditSettingState.NotConfigured, AuditSubcategoryIds["Network Policy Server"], "auditoria de servidor de política de rede"),
        new("Other Logon/Logoff Events", AuditSettingState.NotConfigured, AuditSubcategoryIds["Other Logon/Logoff Events"], "auditoria de outros eventos de logon", "auditoria de outros eventos de logon/logoff"),
        new("Special Logon", AuditSettingState.NotConfigured, AuditSubcategoryIds["Special Logon"], "auditoria de logon especial"),

        // Object Access
        new("Application Generated", AuditSettingState.SuccessAndFailure, AuditSubcategoryIds["Application Generated"], "auditoria de aplicativo gerado"),
        new("Certification Services", AuditSettingState.SuccessAndFailure, AuditSubcategoryIds["Certification Services"], "auditoria de serviços de certificação"),
        new("Detailed File Share", AuditSettingState.SuccessAndFailure, AuditSubcategoryIds["Detailed File Share"], "compartilhamento de arquivos de auditoria detalhado"),
        new("File Share", AuditSettingState.SuccessAndFailure, AuditSubcategoryIds["File Share"], "auditoria de compartilhamento de arquivos"),
        new("File System", AuditSettingState.SuccessAndFailure, AuditSubcategoryIds["File System"], "auditoria de sistema de arquivos"),
        new("Filtering Platform Connection", AuditSettingState.SuccessAndFailure, AuditSubcategoryIds["Filtering Platform Connection"], "auditoria de conexao de plataforma de filtragem"),
        new("Filtering Platform Packet Drop", AuditSettingState.SuccessAndFailure, AuditSubcategoryIds["Filtering Platform Packet Drop"], "auditoria de descarte de pacote de plataforma de filtragem"),
        new("Handle Manipulation", AuditSettingState.SuccessAndFailure, AuditSubcategoryIds["Handle Manipulation"], "auditoria de manipulação de identificador"),
        new("Kernel Object", AuditSettingState.SuccessAndFailure, AuditSubcategoryIds["Kernel Object"], "auditoria de objeto kernel"),
        new("Other Object Access Events", AuditSettingState.SuccessOnly, AuditSubcategoryIds["Other Object Access Events"], "auditoria de outros eventos de acesso a objetos"),
        new("Registry", AuditSettingState.SuccessAndFailure, AuditSubcategoryIds["Registry"], "auditoria de registro"),
        new("Removable Storage", AuditSettingState.SuccessOnly, AuditSubcategoryIds["Removable Storage"], "auditoria de armazenamento removivel"),
        new("SAM", AuditSettingState.SuccessAndFailure, AuditSubcategoryIds["SAM"], "auditoria de sam"),
        new("Central Policy Staging", AuditSettingState.SuccessOnly, AuditSubcategoryIds["Central Policy Staging"], "preparo de política de acesso central de auditoria"),

        // Policy Change
        new("Audit Policy Change", AuditSettingState.SuccessAndFailure, AuditSubcategoryIds["Audit Policy Change"], "auditoria de alteração de políticas de auditoria"),
        new("Authentication Policy Change", AuditSettingState.SuccessAndFailure, AuditSubcategoryIds["Authentication Policy Change"], "auditoria de alteração de políticas de autenticação"),
        new("Authorization Policy Change", AuditSettingState.SuccessAndFailure, AuditSubcategoryIds["Authorization Policy Change"], "auditoria de alteração de políticas de autorizacao"),
        new("Filtering Platform Policy Change", AuditSettingState.NotConfigured, AuditSubcategoryIds["Filtering Platform Policy Change"], "auditoria de alteração na políticas da plataforma de filtragem"),
        new("MPSSVC Rule-Level Policy Change", AuditSettingState.NotConfigured, AuditSubcategoryIds["MPSSVC Rule-Level Policy Change"], "auditoria de alteração na políticas de nivel de regra mpssvc"),
        new("Other Policy Change Events", AuditSettingState.NotConfigured, AuditSubcategoryIds["Other Policy Change Events"], "auditoria de outros eventos de alteração de políticas"),
        new("Non Sensitive Privilege Use", AuditSettingState.NotConfigured, AuditSubcategoryIds["Non Sensitive Privilege Use"], "auditoria de uso de privilégio não importante"),
        new("Other Privilege Use Events", AuditSettingState.NotConfigured, AuditSubcategoryIds["Other Privilege Use Events"], "auditoria de outros eventos de uso de privilégios"),
        new("Sensitive Privilege Use", AuditSettingState.FailureOnly, AuditSubcategoryIds["Sensitive Privilege Use"], "auditoria de uso de privilégio importante"),

        // System
        new("IPsec Driver", AuditSettingState.NotConfigured, AuditSubcategoryIds["IPsec Driver"], "auditoria de driver ipsec"),
        new("Other System Events", AuditSettingState.NotConfigured, AuditSubcategoryIds["Other System Events"], "auditoria de outros eventos do sistema"),
        new("Security State Change", AuditSettingState.SuccessAndFailure, AuditSubcategoryIds["Security State Change"], "auditoria de alteração no estado de segurança"),
        new("Security System Extension", AuditSettingState.NotConfigured, AuditSubcategoryIds["Security System Extension"], "auditoria de extensao do sistema de segurança"),
        new("System Integrity", AuditSettingState.SuccessAndFailure, AuditSubcategoryIds["System Integrity"], "auditoria de integridade do sistema")
    };


    private static readonly string[] AuditNamePrefixes =
    {
        "auditoria de ",
        "auditoria do ",
        "auditoria da ",
        "auditoria dos ",
        "auditoria das ",
        "auditar ",
        "auditoria "
    };

    private const int AuditPolParseWarningLimit = 20;
    private static int _auditPolSplitWarnings;
    private static int _auditPolStateWarnings;

    private static readonly Encoding AuditPolEncoding = InitializeAuditPolEncoding();

    private static readonly Dictionary<string, string[]> LocalizedAuditAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Credential Validation"] = new[] { "Validação de Credenciais" },
        ["Kerberos Authentication Service"] = new[] { "Serviço de Autenticação Kerberos" },
        ["Kerberos Service Ticket Operations"] = new[] { "Operações do Tiquete de serviço Kerberos" },
        ["Application Group Management"] = new[] { "Gerenciamento de grupo de aplicativos" },
        ["Computer Account Management"] = new[] { "Gerenciamento de conta de computador" },
        ["Distribution Group Management"] = new[] { "Gerenciamento de grupo de distribuição" },
        ["Other Account Management Events"] = new[] { "Outros Eventos de Gerenciamento de Contas" },
        ["Security Group Management"] = new[] { "Gerenciamento de grupo de segurança" },
        ["User Account Management"] = new[] { "Gerenciamento de conta de usuário" },
        ["DPAPI Activity"] = new[] { "Atividade DPAPI" },
        ["Plug and Play Events"] = new[] { "Eventos Plug and Play" },
        ["Process Creation"] = new[] { "Criação de processo" },
        ["Process Termination"] = new[] { "Terminação de processo" },
        ["RPC Events"] = new[] { "Eventos de RPC" },
        ["Token Right Adjusted Events"] = new[] { "Eventos Ajustados ao Direito de Token" },
        ["Account Lockout"] = new[] { "Bloqueio de conta" },
        ["User / Device Claims"] = new[] { "Declarações do Usuário/Dispositivo" },
        ["Group Membership"] = new[] { "Associação a um Grupo" },
        ["IPsec Extended Mode"] = new[] { "Modo estendido do IPsec" },
        ["IPsec Main Mode"] = new[] { "Modo principal do IPsec" },
        ["IPsec Quick Mode"] = new[] { "Modo rapido do IPsec" },
        ["Logoff"] = new[] { "Logoff" },
        ["Logon"] = new[] { "Logon" },
        ["Network Policy Server"] = new[] { "Servidor de política de Rede" },
        ["Other Logon/Logoff Events"] = new[] { "Outros eventos de logon/logoff" },
        ["Special Logon"] = new[] { "Logon especial" },
        ["Application Generated"] = new[] { "Aplicativo gerado" },
        ["Certification Services"] = new[] { "Serviços de certificação" },
        ["Detailed File Share"] = new[] { "Compartilhamento de arquivos detalhado" },
        ["File Share"] = new[] { "Compartilhamento de arquivos" },
        ["File System"] = new[] { "Sistema de arquivos" },
        ["Filtering Platform Connection"] = new[] { "Conexao de Plataforma de Filtragem" },
        ["Filtering Platform Packet Drop"] = new[] { "Descarte de Pacote de Plataforma de Filtragem" },
        ["Handle Manipulation"] = new[] { "Manipulação de Identificador" },
        ["Kernel Object"] = new[] { "Objeto Kernel" },
        ["Other Object Access Events"] = new[] { "Outros Eventos de Acesso a Objetos" },
        ["Registry"] = new[] { "Registro" },
        ["Removable Storage"] = new[] { "Armazenamento Removivel" },
        ["SAM"] = new[] { "SAM" },
        ["Central Policy Staging"] = new[] { "Preparo da política Central" },
        ["Audit Policy Change"] = new[] { "Auditoria de alteração de políticas" },
        ["Authentication Policy Change"] = new[] { "Alteração na política de autenticação" },
        ["Authorization Policy Change"] = new[] { "Alteração na política de Autorizacao" },
        ["Filtering Platform Policy Change"] = new[] { "Alteração na política da Plataforma de Filtragem" },
        ["MPSSVC Rule-Level Policy Change"] = new[] { "Alteração na política de nivel de regra MPSSVC" },
        ["Other Policy Change Events"] = new[] { "Outros eventos de alteração de política" },
        ["Non Sensitive Privilege Use"] = new[] { "Uso de Privilégio Não Importante" },
        ["Other Privilege Use Events"] = new[] { "Outros Eventos de Uso de Privilégios" },
        ["Sensitive Privilege Use"] = new[] { "Uso de Privilégio Importante" },
        ["IPsec Driver"] = new[] { "Driver IPsec" },
        ["Other System Events"] = new[] { "Outros Eventos do Sistema" },
        ["Security State Change"] = new[] { "Alteração no estado de segurança" },
        ["Security System Extension"] = new[] { "Extensao do sistema de segurança" },
        ["System Integrity"] = new[] { "Integridade do sistema" }
    };

    private static readonly Dictionary<string, AdvancedAuditSetting> AdvancedAuditSettingLookup =
        CreateAdvancedAuditLookup();

    private static readonly Dictionary<string, AdvancedAuditSetting> AdvancedAuditSettingByGuid =
        CreateAdvancedAuditGuidLookup();

    private static readonly Dictionary<string, string> ObservedAuditSubcategoryNames =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly object AuditSubcategoryNamesLock = new();

    private static readonly Dictionary<string, string> LocalizedAuditSubcategoryGuids =
        new(StringComparer.OrdinalIgnoreCase);

    private static bool _localizedAuditSubcategoriesLoaded;

    private static void RegisterAuditPolDisplayName(string canonicalName, string actualDisplayName)
    {
        if (string.IsNullOrWhiteSpace(canonicalName) || string.IsNullOrWhiteSpace(actualDisplayName))
        {
            return;
        }

        lock (AuditSubcategoryNamesLock)
        {
            if (!ObservedAuditSubcategoryNames.TryGetValue(canonicalName, out var existing) ||
                string.IsNullOrWhiteSpace(existing))
            {
                ObservedAuditSubcategoryNames[canonicalName] = actualDisplayName;
            }
        }
    }

    private static bool TryGetObservedAuditPolName(string canonicalName, out string displayName)
    {
        lock (AuditSubcategoryNamesLock)
        {
            return ObservedAuditSubcategoryNames.TryGetValue(canonicalName, out displayName!);
        }
    }

    private static IEnumerable<string> GetAuditPolCommandNames(AdvancedAuditSetting setting)
    {
        var yielded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var guidToken = FormatGuidForAuditPol(setting.SubcategoryId);
        if (!string.IsNullOrEmpty(guidToken) && yielded.Add(guidToken))
        {
            yield return guidToken;
        }

        if (TryGetObservedAuditPolName(setting.Name, out var observed) && yielded.Add(observed))
        {
            yield return observed;
        }

        foreach (var name in setting.GetAllNames())
        {
            if (string.IsNullOrWhiteSpace(name) || !yielded.Add(name))
            {
                continue;
            }

            yield return name;
        }
    }

    private static AuditSettingState GetAuditSettingState(AdvancedAuditSetting setting)
    {
        InvalidOperationException? lastError = null;

        foreach (var candidate in GetAuditPolCommandNames(setting))
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            try
            {
                if (TryExecuteAuditPolForSubcategory(candidate, out var output) &&
                    TryParseSingleAuditState(output, out var state))
                {
                    return state;
                }
            }
            catch (InvalidOperationException ex)
            {
                lastError = ex;
                LogWriter.Write(ex, $"auditpol.exe falhou ao consultar '{setting.Name}' usando '{candidate}'.");
            }
        }

        throw lastError ?? new InvalidOperationException($"Não foi possível obter a configuração da subcategoria '{setting.Name}'.");
    }

    private static readonly Dictionary<string, string> BaseAuditPolicyFriendlyNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["AuditAccountLogon"] = "Logon de conta",
            ["AuditAccountManage"] = "Gerenciamento de contas",
            ["AuditDSAccess"] = "Acesso ao Serviço de Diretório",
            ["AuditLogonEvents"] = "Logon/Logoff",
            ["AuditObjectAccess"] = "Acesso a objetos",
            ["AuditPolicyChange"] = "Alteração de políticas",
            ["AuditPrivilegeUse"] = "Uso de privilégios",
            ["AuditProcessTracking"] = "Rastreamento de processos",
            ["AuditSystemEvents"] = "Eventos do sistema"
        };

    private static readonly Dictionary<string, int> BaseAuditPolicyExpectedValues =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["AuditObjectAccess"] = 1
        };

    private static readonly WellKnownSidType[] BuiltInAccountSidTypes =
    {
        WellKnownSidType.AccountAdministratorSid,
        WellKnownSidType.AccountGuestSid
    };

    private static IReadOnlyList<BuiltInAccountSidInfo> ResolveBuiltInAccountSids()
    {
        var emitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<BuiltInAccountSidInfo>();
        var domainSid = GetLocalAccountDomainSid();

        if (domainSid is not null)
        {
            foreach (var sidType in BuiltInAccountSidTypes)
            {
                try
                {
                    var sid = new SecurityIdentifier(sidType, domainSid).Value;
                    if (emitted.Add(sid))
                    {
                        result.Add(new BuiltInAccountSidInfo(sidType, sid));
                    }
                }
                catch (Exception ex)
                {
                    LogWriter.Write(ex, $"Falha ao compor SID para {sidType} com domainSid {domainSid.Value}.");
                }
            }
        }

        foreach (var sid in EnumerateBuiltInAccountSidsFromWmi())
        {
            if (emitted.Add(sid.Sid))
            {
                result.Add(sid);
            }
        }

        return result;
    }

    private static SecurityIdentifier? GetLocalAccountDomainSid()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return identity?.User?.AccountDomainSid;
        }
        catch (Exception ex)
        {
            LogWriter.Write(ex, "Não foi possível obter o SID de dominio local.");
            return null;
        }
    }

    private static IEnumerable<BuiltInAccountSidInfo> EnumerateBuiltInAccountSidsFromWmi()
    {
        var result = new List<BuiltInAccountSidInfo>();
        ManagementObjectCollection? collection = null;

        try
        {
            var query = new ObjectQuery("SELECT SID FROM Win32_UserAccount WHERE LocalAccount = TRUE AND (SID LIKE '%-500' OR SID LIKE '%-501')");
            using var searcher = new ManagementObjectSearcher(query);
            collection = searcher.Get();
            foreach (ManagementObject obj in collection)
            {
                if (obj["SID"] is string sid && !string.IsNullOrWhiteSpace(sid))
                {
                    var type = sid.EndsWith("-500", StringComparison.Ordinal) ? WellKnownSidType.AccountAdministratorSid : WellKnownSidType.AccountGuestSid;
                    result.Add(new BuiltInAccountSidInfo(type, sid));
                }
            }
        }
        catch (Exception ex)
        {
            LogWriter.Write(ex, "Falha ao enumerar contas padrao via WMI.");
        }
        finally
        {
            collection?.Dispose();
        }

        return result;
    }

    private const string AdministratorsGroupSid = "S-1-5-32-544";
    private const string UsersGroupSid = "S-1-5-32-545";

    public async Task<IReadOnlyDictionary<string, ConfigurationAssessmentResult>> EvaluateAsync(
        IProgress<(string Key, ConfigurationAssessmentResult Result)>? progress = null,
        IProgress<string>? stageProgress = null,
        CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var results = new Dictionary<string, ConfigurationAssessmentResult>(StringComparer.OrdinalIgnoreCase);

            void Report(string key, ConfigurationAssessmentResult result)
            {
                results[key] = result;
                progress?.Report((key, result));
            }

            cancellationToken.ThrowIfCancellationRequested();
            stageProgress?.Report("sistema-operacional");
            LogWriter.Write("Iniciando verificacao: sistema-operacional");
            var operatingSystemResult = EvaluateOperatingSystem();
            LogWriter.Write($"Resultado sistema-operacional: {operatingSystemResult.Status}");
            Report("sistema-operacional", operatingSystemResult);

            cancellationToken.ThrowIfCancellationRequested();
            stageProgress?.Report("antivirus");
            LogWriter.Write("Iniciando verificacao: antivirus");
            var antivirusAssessment = AnalyzeAntivirus();
            LogWriter.Write($"Resultado antivirus: {antivirusAssessment.Result.Status}");
            Report("antivirus", antivirusAssessment.Result);

            cancellationToken.ThrowIfCancellationRequested();
            stageProgress?.Report("windows-update");
            LogWriter.Write("Iniciando verificacao: windows-update");
            var windowsUpdateAssessment = AnalyzeWindowsUpdate();
            LogWriter.Write($"Resultado windows-update: {windowsUpdateAssessment.Result.Status}");
            Report("windows-update", windowsUpdateAssessment.Result);

            cancellationToken.ThrowIfCancellationRequested();
            stageProgress?.Report("protecao-tela");
            LogWriter.Write("Iniciando verificacao: protecao-tela");
            var screenSaverAssessment = AnalyzeScreenSaver();
            LogWriter.Write($"Resultado protecao-tela: {screenSaverAssessment.Status}");
            Report("protecao-tela", screenSaverAssessment);

            cancellationToken.ThrowIfCancellationRequested();
            stageProgress?.Report("login-remoto");
            LogWriter.Write("Iniciando verificacao: login-remoto");
            var remoteAssistanceAssessment = AnalyzeRemoteAssistance();
            LogWriter.Write($"Resultado login-remoto: {remoteAssistanceAssessment.Status}");
            Report("login-remoto", remoteAssistanceAssessment);

            cancellationToken.ThrowIfCancellationRequested();
            stageProgress?.Report("senha-forte");
            LogWriter.Write("Iniciando verificacao: senha-forte");
            var passwordPolicyAssessment = AnalyzePasswordPolicy();
            LogWriter.Write($"Resultado senha-forte: {passwordPolicyAssessment.Status}");
            Report("senha-forte", passwordPolicyAssessment);

            cancellationToken.ThrowIfCancellationRequested();
            stageProgress?.Report("bloqueio-conta");
            LogWriter.Write("Iniciando verificacao: bloqueio-conta");
            var lockoutAssessment = AnalyzeAccountLockoutPolicy();
            LogWriter.Write($"Resultado bloqueio-conta: {lockoutAssessment.Status}");
            Report("bloqueio-conta", lockoutAssessment);

            cancellationToken.ThrowIfCancellationRequested();
            stageProgress?.Report("auditoria-estacoes");
            LogWriter.Write("Iniciando verificacao: auditoria-estacoes");
            var auditPolicyAssessment = AnalyzeAuditPolicy();
            LogWriter.Write($"Resultado auditoria-estacoes: {auditPolicyAssessment.Status}");
            Report("auditoria-estacoes", auditPolicyAssessment);

            cancellationToken.ThrowIfCancellationRequested();
            stageProgress?.Report("contas-usuarios");
            LogWriter.Write("Iniciando verificacao: contas-usuarios");
            var userAccountsAssessment = GetUserAccountsAssessment();
            LogWriter.Write($"Resultado contas-usuarios: {userAccountsAssessment.Status}");
            Report("contas-usuarios", userAccountsAssessment);

            cancellationToken.ThrowIfCancellationRequested();
            stageProgress?.Report("visualizador-eventos");
            LogWriter.Write("Iniciando verificacao: visualizador-eventos");
            var eventViewerAssessment = AnalyzeApplicationEventLog();
            LogWriter.Write($"Resultado visualizador-eventos: {eventViewerAssessment.Status}");
            Report("visualizador-eventos", eventViewerAssessment);

            cancellationToken.ThrowIfCancellationRequested();
            stageProgress?.Report("firewall");
            LogWriter.Write("Iniciando verificacao: firewall");
            var firewallAssessment = AnalyzeFirewall();
            LogWriter.Write($"Resultado firewall: {firewallAssessment.Status}");
            Report("firewall", firewallAssessment);

            cancellationToken.ThrowIfCancellationRequested();
            stageProgress?.Report("sincronismo-hora");
            LogWriter.Write("Iniciando verificacao: sincronismo-hora");
            var timeSyncAssessment = AnalyzeTimeSynchronization();
            LogWriter.Write($"Resultado sincronismo-hora: {timeSyncAssessment.Status}");
            Report("sincronismo-hora", timeSyncAssessment);

            cancellationToken.ThrowIfCancellationRequested();
            stageProgress?.Report("criptografia");
            LogWriter.Write("Iniciando verificacao: criptografia");
            var bitLockerAssessment = AnalyzeBitLockerConfiguration();
            LogWriter.Write($"Resultado criptografia: {bitLockerAssessment.Status}");
            Report("criptografia", bitLockerAssessment);

            cancellationToken.ThrowIfCancellationRequested();
            stageProgress?.Report("integridade");
            LogWriter.Write("Iniciando verificacao: integridade");
            var integrityAssessment = AnalyzeIntegrityConfiguration();
            LogWriter.Write($"Resultado integridade: {integrityAssessment.Status}");
            Report("integridade", integrityAssessment);

            return (IReadOnlyDictionary<string, ConfigurationAssessmentResult>)results;
        }, cancellationToken).ConfigureAwait(false);
    }

    public Task<ConfigurationAssessmentResult> EvaluateCategoryAsync(
        string categoryKey,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            ConfigurationAssessmentResult result = categoryKey switch
            {
                "sistema-operacional" => EvaluateOperatingSystem(),
                "antivirus" => AnalyzeAntivirus().Result,
                "windows-update" => AnalyzeWindowsUpdate().Result,
                "protecao-tela" => AnalyzeScreenSaver(),
                "login-remoto" => AnalyzeRemoteAssistance(),
                "senha-forte" => AnalyzePasswordPolicy(),
                "bloqueio-conta" => AnalyzeAccountLockoutPolicy(),
                "auditoria-estacoes" => AnalyzeAuditPolicy(),
                "contas-usuarios" => GetUserAccountsAssessment(),
                "visualizador-eventos" => AnalyzeApplicationEventLog(),
                "firewall" => AnalyzeFirewall(),
                "sincronismo-hora" => AnalyzeTimeSynchronization(),
                "criptografia" => AnalyzeBitLockerConfiguration(),
                "integridade" => AnalyzeIntegrityConfiguration(),
                _ => new ConfigurationAssessmentResult(ConfigurationStatus.Unknown, "Categoria não reconhecida.")
            };

            LogWriter.Write($"Reavaliacao direta ({categoryKey}): {result.Status}");
            return result;
        }, cancellationToken);
    }

    public async Task ApplyFixAsync(string categoryKey, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!ApprovedRemediationCategories.Contains(categoryKey))
        {
            throw new InvalidOperationException("A configuração solicitada não faz parte do escopo permitido deste módulo.");
        }

        LogWriter.Write($"Configuração de máquina aprovada para execução: {categoryKey}.");

        try
        {
            switch (categoryKey)
            {
                case "antivirus":
                    await RemediateAntivirusAsync(cancellationToken).ConfigureAwait(false);
                    break;
                case "protecao-tela":
                    await RemediateScreenSaverAsync(cancellationToken).ConfigureAwait(false);
                    break;
                case "login-remoto":
                    await RemediateRemoteAssistanceAsync(cancellationToken).ConfigureAwait(false);
                    break;
                case "senha-forte":
                    await RemediatePasswordPolicyAsync(cancellationToken).ConfigureAwait(false);
                    break;
                case "bloqueio-conta":
                    await RemediateAccountLockoutPolicyAsync(cancellationToken).ConfigureAwait(false);
                    break;
                case "auditoria-estacoes":
                    await RemediateAuditPolicyAsync(cancellationToken).ConfigureAwait(false);
                    break;
                case "visualizador-eventos":
                    await RemediateEventViewerApplicationLogAsync(cancellationToken).ConfigureAwait(false);
                    break;
                case "firewall":
                    await RemediateFirewallAsync(cancellationToken).ConfigureAwait(false);
                    break;
                case "sincronismo-hora":
                    await RemediateTimeSynchronizationAsync(cancellationToken).ConfigureAwait(false);
                    break;
                case "criptografia":
                    await RemediateBitLockerConfigurationAsync(cancellationToken).ConfigureAwait(false);
                    break;
                case "integridade":
                    await RemediateIntegrityConfigurationAsync(cancellationToken).ConfigureAwait(false);
                    break;
                case "contas-usuarios":
                    await RemediateUserAccountsAsync(cancellationToken).ConfigureAwait(false);
                    break;
                default:
                    throw new InvalidOperationException("A configuração solicitada não é reconhecida.");
            }
        }
        catch (Exception ex)
        {
            LogWriter.Write(ex, $"Falha ao aplicar ajustes para '{categoryKey}'");
            throw;
        }
    }

    private static ConfigurationAssessmentResult EvaluateOperatingSystem()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            if (key is null)
            {
                return new ConfigurationAssessmentResult(
                    ConfigurationStatus.Unknown,
                    "Não foi possível obter as informacoes do sistema operacional.");
            }

            var productName = Convert.ToString(key.GetValue("ProductName"), CultureInfo.InvariantCulture) ?? string.Empty;
            var editionId = Convert.ToString(key.GetValue("EditionID"), CultureInfo.InvariantCulture) ?? string.Empty;
            var compositionEditionId = Convert.ToString(key.GetValue("CompositionEditionID"), CultureInfo.InvariantCulture) ?? string.Empty;
            var currentBuildRaw = Convert.ToString(key.GetValue("CurrentBuild"), CultureInfo.InvariantCulture);
            var currentBuild = int.TryParse(currentBuildRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedBuild)
                ? parsedBuild
                : 0;
            var displayVersion = Convert.ToString(key.GetValue("DisplayVersion"), CultureInfo.InvariantCulture) ?? string.Empty;
            var isDisplayVersionCompliant = IsDisplayVersionCompliant(displayVersion);

            var isWindows11 =
                productName.Contains("Windows 11", StringComparison.OrdinalIgnoreCase) ||
                currentBuild >= 22000 ||
                isDisplayVersionCompliant;
            if (!isWindows11)
            {
                return new ConfigurationAssessmentResult(
                    ConfigurationStatus.NonCompliant,
                    "Atualize o equipamento para Windows 11 Pro.");
            }

            if (!isDisplayVersionCompliant)
            {
                var versionInfo = string.IsNullOrWhiteSpace(displayVersion) ? "indefinida" : displayVersion;
                return new ConfigurationAssessmentResult(
                    ConfigurationStatus.NonCompliant,
                    $"Atualize o Windows para a versão {MinimumDisplayVersionLabel} ou mais recente (versão detectada: {versionInfo}).");
            }

            if (IsTargetWindowsEdition(editionId) || IsTargetWindowsEdition(compositionEditionId))
            {
                return new ConfigurationAssessmentResult(
                    ConfigurationStatus.Compliant,
                    $"Sistema operacional em conformidade: {productName} ({displayVersion})");
            }

            if (BlockedEditionIds.Contains(editionId))
            {
                return new ConfigurationAssessmentResult(
                    ConfigurationStatus.NonCompliant,
                    "Instale a versão Windows 11 Pro para garantir os recursos de segurança necessarios.");
            }

            var editionInfo = string.IsNullOrWhiteSpace(editionId) ? "edição desconhecida" : editionId;
            return new ConfigurationAssessmentResult(
                ConfigurationStatus.NonCompliant,
                $"Verifique a edição instalada ({editionInfo}). Instale Windows 11 Pro.");
        }
        catch (Exception ex)
        {
            LogWriter.Write(ex, "Erro ao avaliar sistema operacional");
            return new ConfigurationAssessmentResult(
                ConfigurationStatus.Unknown,
                $"Não foi possível validar a edição do Windows. Detalhes: {ex.Message}");
        }
    }

    private static bool IsDisplayVersionCompliant(string displayVersion)
    {
        if (string.IsNullOrWhiteSpace(displayVersion))
        {
            return false;
        }

        var sanitized = displayVersion.Trim().ToUpperInvariant();
        var separatorIndex = sanitized.IndexOf('H', StringComparison.Ordinal);
        if (separatorIndex <= 0 || separatorIndex == sanitized.Length - 1)
        {
            return false;
        }

        if (!int.TryParse(sanitized[..separatorIndex], NumberStyles.Integer, CultureInfo.InvariantCulture, out var yearPart))
        {
            return false;
        }

        if (!int.TryParse(sanitized[(separatorIndex + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var halfPart))
        {
            return false;
        }

        if (yearPart > MinimumDisplayVersionYear)
        {
            return true;
        }

        if (yearPart < MinimumDisplayVersionYear)
        {
            return false;
        }

        return halfPart >= MinimumDisplayVersionHalf;
    }

    private static AntivirusAssessment AnalyzeAntivirus()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\SecurityCenter2",
                "SELECT displayName, productState FROM AntiVirusProduct");

            var products = new List<AntivirusProductInfo>();

            using var results = searcher.Get();
            foreach (ManagementObject? product in results)
            {
                if (product is null)
                {
                    continue;
                }

                var name = Convert.ToString(product["displayName"], CultureInfo.InvariantCulture);
                var stateValue = product["productState"];
                var state = stateValue is null ? 0 : Convert.ToInt32(stateValue, CultureInfo.InvariantCulture);

                var analysis = ParseProductState(state);
                products.Add(new AntivirusProductInfo(name ?? "Produto desconhecido", analysis.IsEnabled, analysis.IsUpToDate));
            }

            if (products.Count == 0)
            {
                var result = new ConfigurationAssessmentResult(
                    ConfigurationStatus.NonCompliant,
                    "Nenhum antivirus detectado. Iremos habilitar o Microsoft Defender e buscar atualizações.");
                return new AntivirusAssessment(result, HasProducts: false, HasHealthyProduct: false, NeedsUpdate: true);
            }

            var healthyProducts = products
                .Where(p => p.IsEnabled && p.IsUpToDate)
                .Select(p => p.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var needsUpdate = products.Any(p => p.IsEnabled && !p.IsUpToDate);

            if (healthyProducts.Count > 0 && !needsUpdate)
            {
                var list = string.Join(", ", healthyProducts);
                var compliantResult = new ConfigurationAssessmentResult(
                    ConfigurationStatus.Compliant,
                    $"Antivirus ativo e atualizado: {list}");
                return new AntivirusAssessment(compliantResult, HasProducts: true, HasHealthyProduct: true, NeedsUpdate: false);
            }

            var details = products.Select(p =>
                $"{p.Name} ({(p.IsEnabled ? "ativo" : "desativado")}, {(p.IsUpToDate ? "atualizado" : "desatualizado")})");

            var nonCompliantResult = new ConfigurationAssessmentResult(
                ConfigurationStatus.NonCompliant,
                "Atualize ou ative o antivirus: " + string.Join("; ", details));

            return new AntivirusAssessment(nonCompliantResult, HasProducts: true, HasHealthyProduct: healthyProducts.Count > 0, NeedsUpdate: true);
        }
        catch (ManagementException)
        {
            var result = new ConfigurationAssessmentResult(
                ConfigurationStatus.Unknown,
                "Não foi possível verificar o antivirus via WMI.");
            LogWriter.Write("Falha WMI ao consultar antivirus.");
            return new AntivirusAssessment(result, HasProducts: false, HasHealthyProduct: false, NeedsUpdate: false);
        }
        catch (UnauthorizedAccessException)
        {
            var result = new ConfigurationAssessmentResult(
                ConfigurationStatus.Unknown,
                "Permissoes insuficientes para verificar o antivirus.");
            LogWriter.Write("Permissao insuficiente ao consultar antivirus.");
            return new AntivirusAssessment(result, HasProducts: false, HasHealthyProduct: false, NeedsUpdate: false);
        }
        catch (Exception ex)
        {
            var result = new ConfigurationAssessmentResult(
                ConfigurationStatus.Unknown,
                $"Erro ao verificar o antivirus: {ex.Message}");
            LogWriter.Write(ex, "Erro inesperado ao consultar antivirus");
            return new AntivirusAssessment(result, HasProducts: false, HasHealthyProduct: false, NeedsUpdate: false);
        }
    }

    private static WindowsUpdateAssessment AnalyzeWindowsUpdate()
    {
        try
        {
            var restartPending = IsRestartPending();

            var pendingInfo = GetPendingUpdatesInfo();

            if (restartPending)
            {
                return new WindowsUpdateAssessment(
                    new ConfigurationAssessmentResult(
                        ConfigurationStatus.Pending,
                        "Reinicie o computador para concluir as atualizações do Windows."),
                    true);
            }

            if (pendingInfo.TotalUpdates > 0)
            {
                var message = $"Atualizações recomendadas pendentes: {pendingInfo.TotalUpdates}. Clique para baixar e instalar automaticamente.";
                return new WindowsUpdateAssessment(
                    new ConfigurationAssessmentResult(ConfigurationStatus.NonCompliant, message),
                    restartPending);
            }

            var skippedFragment = pendingInfo.OptionalUpdates > 0
                ? $" {pendingInfo.OptionalUpdates} atualização(ões) opcional(is), Preview, beta, de driver ou de recurso não fazem parte da instalação automática."
                : string.Empty;
            return new WindowsUpdateAssessment(
                new ConfigurationAssessmentResult(
                    ConfigurationStatus.Compliant,
                    $"Nenhuma atualização recomendada está pendente.{skippedFragment}"),
                restartPending);
        }
        catch (Exception ex)
        {
            LogWriter.Write(ex, "Erro ao avaliar Windows Update");
            return new WindowsUpdateAssessment(
                new ConfigurationAssessmentResult(
                    ConfigurationStatus.Unknown,
                    $"Não foi possível verificar o Windows Update: {ex.Message}"),
                false);
        }
    }

    private static (bool IsEnabled, bool IsUpToDate) ParseProductState(int productState)
    {
        var stateHex = productState.ToString("X6").PadLeft(6, '0');

        var statusCode = Convert.ToInt32(stateHex.Substring(0, 2), 16);
        var definitionCode = Convert.ToInt32(stateHex.Substring(2, 2), 16);
        var scanCode = Convert.ToInt32(stateHex.Substring(4, 2), 16);

        var isEnabled = statusCode switch
        {
            0x00 or 0x01 or 0x02 or 0x03 => false,
            _ => true
        };

        if (!isEnabled && (scanCode & 0x10) == 0x10)
        {
            isEnabled = true;
        }

        var isUpToDate = (definitionCode & 0x10) == 0x10;

        return (isEnabled, isUpToDate);
    }

    private static bool IsRestartPending()
    {
        static bool KeyExists(string path) => Registry.LocalMachine.OpenSubKey(path) is not null;

        return KeyExists(@"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired")
               || KeyExists(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending");
    }

    private static WindowsUpdatePendingInfo GetPendingUpdatesInfo() =>
        RunOnStaThread(() =>
        {
            var sessionType = Type.GetTypeFromProgID("Microsoft.Update.Session");
            if (sessionType is null)
            {
                throw new InvalidOperationException("API do Windows Update indisponivel (Microsoft.Update.Session).");
            }

            var rawSession = Activator.CreateInstance(sessionType!);
            if (rawSession is null)
            {
                throw new InvalidOperationException("Falha ao inicializar a sessao do Windows Update.");
            }

            dynamic session = rawSession;
            var rawSearcher = session.CreateUpdateSearcher();
            if (rawSearcher is null)
            {
                throw new InvalidOperationException("Falha ao acessar o mecanismo de busca do Windows Update.");
            }

            dynamic searcher = rawSearcher;
            dynamic searchResult = searcher.Search("IsInstalled=0 and Type='Software'");

            dynamic updatesCollection = searchResult.Updates;
            var titles = new List<string>();
            var optionalCount = 0;

            try
            {
                int count = updatesCollection.Count;
                for (int i = 0; i < count; i++)
                {
                    dynamic update = updatesCollection.Item(i);
                    try
                    {
                        bool isHidden = false;
                        try
                        {
                            isHidden = update.IsHidden;
                        }
                        catch
                        {
                            // Ignore property access issues.
                        }

                        if (isHidden)
                        {
                            continue;
                        }

                        string title = update.Title as string ?? "Atualização desconhecida";
                        bool autoSelect = true;
                        try
                        {
                            autoSelect = update.AutoSelectOnWebSites;
                        }
                        catch
                        {
                            // Treat as optional when property not accessible.
                            autoSelect = false;
                        }

                        bool isBeta = false;
                        try
                        {
                            isBeta = update.IsBeta;
                        }
                        catch
                        {
                            isBeta = true;
                        }

                        if (!SecurityPolicy.ShouldAutomaticallyInstallWindowsUpdate(
                                isHidden,
                                isBeta,
                                autoSelect,
                                title))
                        {
                            optionalCount++;
                            continue;
                        }

                        titles.Add(title);
                    }
                    finally
                    {
                        ReleaseComObject(update);
                    }
                }
            }
            finally
            {
                ReleaseComObject(updatesCollection);
                ReleaseComObject(searchResult);
                ReleaseComObject(searcher);
                ReleaseComObject(session);
            }

            return new WindowsUpdatePendingInfo(titles, optionalCount);
        });

    private static int? ReadRegistryDword(RegistryKey key, string valueName)
    {
        var value = key.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        return value is null ? null : Convert.ToInt32(value);
    }


    private static Task<T> RunOnStaThreadAsync<T>(Func<T> work, CancellationToken cancellationToken = default)
    {
        if (work is null)
        {
            throw new ArgumentNullException(nameof(work));
        }

        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
        {
            try
            {
                return Task.FromResult(work());
            }
            catch (Exception ex)
            {
                return Task.FromException<T>(ex);
            }
        }

        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

        var thread = new Thread(() =>
        {
            try
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    tcs.TrySetCanceled(cancellationToken);
                    return;
                }

                var result = work();
                tcs.TrySetResult(result);
            }
            catch (OperationCanceledException)
            {
                tcs.TrySetCanceled(cancellationToken);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        })
        {
            IsBackground = true
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        if (cancellationToken.CanBeCanceled)
        {
            cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
        }

        return tcs.Task;
    }

    private static Task RunOnStaThreadAsync(Action work, CancellationToken cancellationToken = default)
    {
        if (work is null)
        {
            throw new ArgumentNullException(nameof(work));
        }

        return RunOnStaThreadAsync(() =>
        {
            work();
            return true;
        }, cancellationToken);
    }

    private static T RunOnStaThread<T>(Func<T> work)
    {
        if (work is null)
        {
            throw new ArgumentNullException(nameof(work));
        }

        return RunOnStaThreadAsync(work).GetAwaiter().GetResult();
    }

    private static async Task RemediateAntivirusAsync(CancellationToken cancellationToken)
    {
        var assessment = AnalyzeAntivirus();

        if (!assessment.HasProducts || !assessment.HasHealthyProduct)
        {
            ShowInformation("Nenhum antivírus ativo foi identificado. Revise e ative o Microsoft Defender na Segurança do Windows.");
            await EnableWindowsDefenderAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        if (assessment.NeedsUpdate)
        {
            ShowInformation("O antivírus está desatualizado. Revise as atualizações na Segurança do Windows.");
            await UpdateWindowsDefenderSignaturesAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        await UpdateWindowsDefenderSignaturesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task EnableWindowsDefenderAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        OpenWindowsSettings("windowsdefender:");
        ShowInformation("Revise e ative o Microsoft Defender na Segurança do Windows.");
        await Task.CompletedTask;
    }

    private static Task UpdateWindowsDefenderSignaturesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        OpenWindowsSettings("windowsdefender:");
        ShowInformation("Revise a atualização das definições do Microsoft Defender na Segurança do Windows.");
        return Task.CompletedTask;
    }

    private static ConfigurationAssessmentResult AnalyzeScreenSaver()
    {
        try
        {
            using var desktopKey = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", writable: false);
            using var screenSaverKey = Registry.CurrentUser.OpenSubKey(@"Software\Policies\Microsoft\Windows\Control Panel\Desktop", writable: false);

            var screenSaverExecutable = Convert.ToString(screenSaverKey?.GetValue("SCRNSAVE.EXE") ?? desktopKey?.GetValue("SCRNSAVE.EXE"), CultureInfo.InvariantCulture);
            var screenSaveActive = Convert.ToString(screenSaverKey?.GetValue("ScreenSaveActive") ?? desktopKey?.GetValue("ScreenSaveActive"), CultureInfo.InvariantCulture);
            var screenSaveTimeout = Convert.ToString(screenSaverKey?.GetValue("ScreenSaveTimeout") ?? desktopKey?.GetValue("ScreenSaveTimeout"), CultureInfo.InvariantCulture);
            var logonEnabled = Convert.ToString(screenSaverKey?.GetValue("ScreenSaverIsSecure") ?? desktopKey?.GetValue("ScreenSaverIsSecure"), CultureInfo.InvariantCulture);

            var issues = new List<string>();

            if (!string.IsNullOrWhiteSpace(screenSaverExecutable))
            {
                issues.Add("Defina o protetor de tela como 'Nenhum'.");
            }

            if (!string.Equals(screenSaveActive, "1", StringComparison.OrdinalIgnoreCase))
            {
                issues.Add("Ative a opcao de exibir a tela de logon ao retornar.");
            }

            if (!int.TryParse(screenSaveTimeout, out var timeoutSeconds) || timeoutSeconds != 120)
            {
                issues.Add("Configure o tempo de espera do protetor de tela para 2 minutos.");
            }

            if (!string.Equals(logonEnabled, "1", StringComparison.OrdinalIgnoreCase))
            {
                issues.Add("Habilite a tela de logon apos o protetor de tela.");
            }

            if (issues.Count == 0)
            {
                return new ConfigurationAssessmentResult(
                    ConfigurationStatus.Compliant,
                    "Protetor de tela desabilitado com bloqueio apos 2 minutos.");
            }

            var message = "Ajuste a protecao de tela: " + string.Join(" ", issues);
            return new ConfigurationAssessmentResult(ConfigurationStatus.NonCompliant, message);
        }
        catch (Exception ex)
        {
            LogWriter.Write(ex, "Erro ao verificar configuração de protecao de tela");
            return new ConfigurationAssessmentResult(ConfigurationStatus.Unknown, $"Não foi possível verificar a protecao de tela: {ex.Message}");
        }
    }

    private static async Task RemediateScreenSaverAsync(CancellationToken cancellationToken)
    {
        await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            ConfigureScreenSaverKey(Registry.CurrentUser.CreateSubKey(@"Control Panel\Desktop"));
        }, cancellationToken).ConfigureAwait(false);
        LogWriter.Write("Proteção de tela configurada (nenhum + logon + 2 minutos).");
    }

    private static ConfigurationAssessmentResult AnalyzeRemoteAssistance()
    {
        try
        {
            using var policyKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows NT\Terminal Services", writable: false);
            using var systemKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Remote Assistance", writable: false);
            using var terminalServicesKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Terminal Server", writable: false);

            var policyAllow = Convert.ToInt32(policyKey?.GetValue("fAllowToGetHelp") ?? 1, CultureInfo.InvariantCulture);
            var systemAllow = Convert.ToInt32(systemKey?.GetValue("fAllowToGetHelp") ?? 1, CultureInfo.InvariantCulture);
            var rdDisabled = Convert.ToInt32(terminalServicesKey?.GetValue("fDenyTSConnections") ?? 0, CultureInfo.InvariantCulture);

            if (policyAllow == 0 && systemAllow == 0 && rdDisabled == 1)
            {
                return new ConfigurationAssessmentResult(
                    ConfigurationStatus.Compliant,
                    "Assistencia Remota desativada.");
            }

            var issues = new List<string>();

            if (!(policyAllow == 0 && systemAllow == 0))
            {
                issues.Add("Desative a opcao 'Permitir conexões de Assistencia Remota para este computador'.");
            }

            if (rdDisabled != 1)
            {
                issues.Add("Defina 'Não permitir conexões remotas com este computador'.");
            }

            return new ConfigurationAssessmentResult(
                ConfigurationStatus.NonCompliant,
                string.Join(" ", issues));
        }
        catch (Exception ex)
        {
            LogWriter.Write(ex, "Erro ao verificar Assistencia Remota");
            return new ConfigurationAssessmentResult(
                ConfigurationStatus.Unknown,
                $"Não foi possível verificar a Assistencia Remota: {ex.Message}");
        }
    }

    private static async Task RemediateRemoteAssistanceAsync(CancellationToken cancellationToken)
    {
        await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            SetDwordValue(Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Windows NT\Terminal Services"), "fAllowToGetHelp", 0);
            SetDwordValue(Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\Remote Assistance"), "fAllowToGetHelp", 0);
            SetDwordValue(Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\Terminal Server"), "fDenyTSConnections", 1);
        }, cancellationToken).ConfigureAwait(false);
        LogWriter.Write("Assistência Remota desativada.");
    }

    private static ConfigurationAssessmentResult AnalyzePasswordPolicy()
    {
        try
        {
            var policy = ExportSecurityPolicySection("System Access");
            var issues = new List<string>();

            bool TryGet(string key, out int value)
            {
                if (policy.TryGetValue(key, out var raw) && int.TryParse(raw, out value))
                {
                    return true;
                }

                value = 0;
                return false;
            }

            if (!TryGet("PasswordComplexity", out var complexity) || complexity != 1)
            {
                issues.Add("Habilite a complexidade de senha.");
            }

            if (!TryGet("PasswordHistorySize", out var history) || history < 5)
            {
                issues.Add("Aplicar historico de senhas com pelo menos 5 entradas.");
            }

            if (!TryGet("ClearTextPassword", out var clearText) || clearText != 0)
            {
                issues.Add("Desative o armazenamento de senhas com criptografia reversivel.");
            }

            if (!TryGet("MinimumPasswordLength", out var minLength) || minLength < 8)
            {
                issues.Add("Defina comprimento minimo de 8 caracteres.");
            }

            if (!TryGet("MaximumPasswordAge", out var maxAge) || maxAge != 30)
            {
                issues.Add("Defina o tempo maximo de vida da senha para 30 dias.");
            }

            if (!TryGet("MinimumPasswordAge", out var minAge) || minAge != 0)
            {
                issues.Add("Defina o tempo minimo de vida da senha para 0 dias.");
            }

            if (issues.Count == 0)
            {
                return new ConfigurationAssessmentResult(
                    ConfigurationStatus.Compliant,
                    "políticas de senha configuradas conforme requisitos.");
            }

            return new ConfigurationAssessmentResult(
                ConfigurationStatus.NonCompliant,
                "Ajuste as políticas de senha: " + string.Join(" ", issues));
        }
        catch (Exception ex)
        {
            LogWriter.Write(ex, "Erro ao verificar políticas de senha");
            return new ConfigurationAssessmentResult(
                ConfigurationStatus.Unknown,
                $"Não foi possível verificar as políticas de senha: {ex.Message}");
        }
    }

    private static async Task RemediatePasswordPolicyAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var cfgPath = CreateScopedTemporaryPath($"config-auditoria-password-{Guid.NewGuid():N}.inf");
        var builder = new StringBuilder();
        builder.AppendLine("[Unicode]");
        builder.AppendLine("Unicode=yes");
        builder.AppendLine("[Version]");
        builder.AppendLine("signature=\"$CHICAGO$\"");
        builder.AppendLine("Revision=1");
        builder.AppendLine("[System Access]");
        builder.AppendLine("MinimumPasswordAge = 0");
        builder.AppendLine("MaximumPasswordAge = 30");
        builder.AppendLine("MinimumPasswordLength = 8");
        builder.AppendLine("PasswordComplexity = 1");
        builder.AppendLine("PasswordHistorySize = 5");
        builder.AppendLine("ClearTextPassword = 0");

        await File.WriteAllTextAsync(cfgPath, builder.ToString(), Encoding.Unicode, cancellationToken).ConfigureAwait(false);

        try
        {
            await ApplySecurityTemplateAsync(cfgPath, cancellationToken).ConfigureAwait(false);
            await RunProcessAsync("net.exe", "accounts /minpwlen:8 /maxpwage:30 /minpwage:0 /uniquepw:5", cancellationToken).ConfigureAwait(false);

            LogWriter.Write("políticas de senha atualizadas (complexidade, historico, comprimento, idade e criptografia).");
        }
        finally
        {
            try
            {
                File.Delete(cfgPath);
            }
            catch
            {
                // ignore
            }
        }
    }

    private static ConfigurationAssessmentResult AnalyzeAccountLockoutPolicy()
    {
        try
        {
            var policy = ExportSecurityPolicySection("System Access");

            bool TryGet(string key, out int value)
            {
                if (policy.TryGetValue(key, out var raw) && int.TryParse(raw, out value))
                {
                    return true;
                }

                value = 0;
                return false;
            }

            var issues = new List<string>();

            if (!TryGet("LockoutDuration", out var lockoutDuration) || lockoutDuration != 10080)
            {
                issues.Add("Defina a duracao do bloqueio de conta para 10080 minutos.");
            }

            if (!TryGet("LockoutBadCount", out var lockoutThreshold) || lockoutThreshold != 3)
            {
                issues.Add("Defina o limite de bloqueio de conta para 3 tentativas.");
            }

            if (!TryGet("ResetLockoutCount", out var lockoutReset) || lockoutReset != 1440)
            {
                issues.Add("Defina o tempo para zerar contagem de bloqueios para 1440 minutos.");
            }

            if (issues.Count == 0)
            {
                return new ConfigurationAssessmentResult(
                    ConfigurationStatus.Compliant,
                    "Bloqueio de conta configurado conforme política.");
            }

            return new ConfigurationAssessmentResult(
                ConfigurationStatus.NonCompliant,
                "Ajuste o bloqueio de conta: " + string.Join(" ", issues));
        }
        catch (Exception ex)
        {
            LogWriter.Write(ex, "Erro ao verificar política de bloqueio de conta");
            return new ConfigurationAssessmentResult(
                ConfigurationStatus.Unknown,
                $"Não foi possível verificar o bloqueio de conta: {ex.Message}");
        }
    }

    private static ConfigurationAssessmentResult AnalyzeAuditPolicy()
    {
        try
        {
            var issues = new List<string>();

            if (!IsAdvancedAuditSubcategoryEnforced())
            {
                issues.Add("Habilite \"Auditoria: forçar configurações de subcategorias\" para substituir as políticas legadas.");
            }

            var basePolicy = ExportSecurityPolicySection("Event Audit");
            foreach (var key in BaseAuditPolicyKeys)
            {
                var expectedValue = BaseAuditPolicyExpectedValues.TryGetValue(key, out var customExpected)
                    ? customExpected
                    : 0;

                if (!basePolicy.TryGetValue(key, out var raw) ||
                    !int.TryParse(raw, out var value) ||
                    value != expectedValue)
                {
                    var friendly = BaseAuditPolicyFriendlyNames.TryGetValue(key, out var name) ? name : key;
                    var expectation = DescribeBaseAuditExpectation(expectedValue);
                    issues.Add($"Defina '{friendly}' como '{expectation}' nas políticas basicas.");
                }
            }

            var advancedSettings = GetAdvancedAuditPolicySettings();
            foreach (var setting in AdvancedAuditSettings)
            {
                if (!advancedSettings.TryGetValue(setting.Name, out var actual))
                {
                    issues.Add($"Configuração de auditoria avancada '{setting.Name}' não encontrada.");
                    continue;
                }

                if (actual == setting.ExpectedState)
                {
                    continue;
                }

                if (setting.ExpectedState == AuditSettingState.NotConfigured && actual == AuditSettingState.NoAuditing)
                {
                    continue;
                }

                issues.Add($"Ajuste '{setting.Name}' para {DescribeAuditExpectation(setting.ExpectedState)}.");
            }

            if (issues.Count == 0)
            {
                return new ConfigurationAssessmentResult(
                    ConfigurationStatus.Compliant,
                    "políticas de auditoria configuradas conforme requisitos.");
            }

            return new ConfigurationAssessmentResult(
                ConfigurationStatus.NonCompliant,
                "Ajuste as políticas de auditoria: " + string.Join(" ", issues));
        }
        catch (Exception ex)
        {
            LogWriter.Write(ex, "Erro ao verificar políticas de auditoria");
            return new ConfigurationAssessmentResult(
                ConfigurationStatus.Unknown,
                $"Não foi possível verificar as configurações de auditoria: {ex.Message}");
        }
    }

    private static ConfigurationAssessmentResult GetUserAccountsAssessment()
    {
        try
        {
            var evaluation = EvaluateUserAccountState();
            if (evaluation.Issues.Count == 0)
            {
                return new ConfigurationAssessmentResult(
                    ConfigurationStatus.Compliant,
                    BuildUserAccountSummary(evaluation));
            }

            return new ConfigurationAssessmentResult(
                ConfigurationStatus.NonCompliant,
                "Ajuste as contas de usuário: " + string.Join(" ", evaluation.Issues));
        }
        catch (Exception ex)
        {
            LogWriter.Write(ex, "Erro ao verificar contas de usuário.");
            return new ConfigurationAssessmentResult(
                ConfigurationStatus.Unknown,
                $"Não foi possível verificar as contas de usuário: {ex.Message}");
        }
    }

    private static ConfigurationAssessmentResult AnalyzeApplicationEventLog()
    {
        try
        {
            using var configuration = new EventLogConfiguration("Application");

            if (!configuration.IsEnabled)
            {
                return new ConfigurationAssessmentResult(
                    ConfigurationStatus.NonCompliant,
                    "Habilite o log de Aplicativo no Visualizador de Eventos.");
            }

            var currentBytes = configuration.MaximumSizeInBytes;
            if (currentBytes != ApplicationLogMaxSizeBytes)
            {
                var currentKb = currentBytes / 1024;
                return new ConfigurationAssessmentResult(
                    ConfigurationStatus.NonCompliant,
                    $"Defina o 'Tamanho maximo do log (KB)' para 4194240. Valor atual: {currentKb:N0} KB.");
            }

            return new ConfigurationAssessmentResult(
                ConfigurationStatus.Compliant,
                "Log de Aplicativo configurado com tamanho maximo de 4194240 KB.");
        }
        catch (EventLogNotFoundException ex)
        {
            LogWriter.Write(ex, "Log de Aplicativo não encontrado.");
            return new ConfigurationAssessmentResult(
                ConfigurationStatus.Unknown,
                "Não foi possível localizar o log de Aplicativo no Visualizador de Eventos.");
        }
        catch (Exception ex)
        {
            LogWriter.Write(ex, "Erro ao verificar configuração do log de Aplicativo");
            return new ConfigurationAssessmentResult(
                ConfigurationStatus.Unknown,
                $"Não foi possível verificar o log de Aplicativo: {ex.Message}");
        }
    }

    private static ConfigurationAssessmentResult AnalyzeTimeSynchronization()
    {
        try
        {
            using var parametersKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\W32Time\Parameters");
            using var ntpClientKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\W32Time\TimeProviders\NtpClient");

            if (parametersKey is null || ntpClientKey is null)
            {
                return new ConfigurationAssessmentResult(
                    ConfigurationStatus.Unknown,
                    "Não foi possível acessar as configurações de sincronismo de hora.");
            }

            var issues = new List<string>();

            var ntpServer = Convert.ToString(parametersKey.GetValue("NtpServer"), CultureInfo.InvariantCulture) ?? string.Empty;
            if (!ntpServer.Contains(TimeSyncServer, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add($"Configure o servidor NTP para {TimeSyncServer}.");
            }

            var typeValue = Convert.ToString(parametersKey.GetValue("Type"), CultureInfo.InvariantCulture) ?? string.Empty;
            if (!typeValue.Equals("NTP", StringComparison.OrdinalIgnoreCase))
            {
                issues.Add("Defina o metodo de sincronismo como NTP.");
            }

            var pollValue = ntpClientKey.GetValue("SpecialPollInterval");
            if (pollValue is null || !int.TryParse(Convert.ToString(pollValue, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out var pollSeconds) || pollSeconds != TimeSyncSpecialPollIntervalSeconds)
            {
                issues.Add("Defina SpecialPollInterval para 3600 segundos.");
            }

            if (issues.Count == 0)
            {
                return new ConfigurationAssessmentResult(
                    ConfigurationStatus.Compliant,
                    $"Sincronismo configurado com {TimeSyncServer} a cada {TimeSyncSpecialPollIntervalSeconds} segundos.");
            }

            return new ConfigurationAssessmentResult(
                ConfigurationStatus.NonCompliant,
                "Ajuste o sincronismo de hora: " + string.Join(" ", issues));
        }
        catch (Exception ex)
        {
            LogWriter.Write(ex, "Erro ao verificar sincronismo de hora");
            return new ConfigurationAssessmentResult(
                ConfigurationStatus.Unknown,
                $"Não foi possível verificar o sincronismo de hora: {ex.Message}");
        }
    }

    private static ConfigurationAssessmentResult AnalyzeBitLockerConfiguration()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\FVE");

            if (key is null)
            {
                return new ConfigurationAssessmentResult(
                    ConfigurationStatus.NonCompliant,
                    "Configure as políticas do BitLocker conforme os requisitos.");
            }

            var issues = new List<string>();

            void RequireDword(string name, int expected, string message)
            {
                var raw = key.GetValue(name);
                if (raw is null || !int.TryParse(Convert.ToString(raw, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out var current) || current != expected)
                {
                    issues.Add(message);
                }
            }

            RequireDword("UseAdvancedStartup", 1, "Habilite autenticação adicional na inicialização.");
            RequireDword("EnableBDEWithNoTPM", 1, "Permita o BitLocker sem TPM compatível.");
            RequireDword("UseTPM", 2, "Permita o uso de TPM na inicialização.");
            RequireDword("UseTPMPIN", 1, "Permita PIN de inicialização com TPM.");
            RequireDword("UseTPMKey", 1, "Permita chave de inicialização com TPM.");
            RequireDword("UseTPMKeyPIN", 1, "Permita chave e PIN de inicialização com TPM.");
            RequireDword("DisallowStandardUserPINReset", 1, "Impeça que usuários padrao alterem PIN ou senha.");
            RequireDword("OSPassphraseEnabled", 1, "Habilite o uso de senhas para unidades do sistema operacional.");
            RequireDword("OSPassphraseComplexity", 1, "Habilite a complexidade de senha para unidades do sistema operacional.");
            RequireDword("OSPassphraseLength", 8, "Defina o tamanho minimo da senha do sistema operacional para 8 caracteres.");
            RequireDword("OSPassphraseRequireASCII", 0, "Permita senhas com caracteres não ASCII para unidades do sistema operacional.");

            if (issues.Count == 0)
            {
                return new ConfigurationAssessmentResult(
                    ConfigurationStatus.Compliant,
                    "políticas do BitLocker configuradas conforme requisitos.");
            }

            return new ConfigurationAssessmentResult(
                ConfigurationStatus.NonCompliant,
                "Ajuste o BitLocker: " + string.Join(" ", issues));
        }
        catch (Exception ex)
        {
            LogWriter.Write(ex, "Erro ao verificar políticas do BitLocker");
            return new ConfigurationAssessmentResult(
                ConfigurationStatus.Unknown,
                $"Não foi possível verificar o BitLocker: {ex.Message}");
        }
    }

    private static ConfigurationAssessmentResult AnalyzeIntegrityConfiguration()
    {
        try
        {
            var issues = new List<string>();
            var everyone = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
            var requiredRights =
                FileSystemRights.CreateFiles |
                FileSystemRights.CreateDirectories |
                FileSystemRights.WriteAttributes |
                FileSystemRights.WriteExtendedAttributes |
                FileSystemRights.DeleteSubdirectoriesAndFiles |
                FileSystemRights.Delete;
            var requiredInheritance = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;

            foreach (var path in IntegrityAuditDirectories)
            {
                if (!Directory.Exists(path))
                {
                    issues.Add($"Diretório '{path}' não encontrado.");
                    continue;
                }

                try
                {
                    var dirInfo = new DirectoryInfo(path);
                    var security = dirInfo.GetAccessControl(AccessControlSections.Audit);
                    var rules = security.GetAuditRules(true, true, typeof(SecurityIdentifier))
                        .Cast<FileSystemAuditRule>()
                        .Where(rule => rule.IdentityReference == everyone && rule.AuditFlags.HasFlag(AuditFlags.Success))
                        .ToList();

                    var match = rules.Any(rule =>
                        (rule.FileSystemRights & requiredRights) == requiredRights &&
                        (rule.FileSystemRights & ~requiredRights) == 0 &&
                        rule.InheritanceFlags == requiredInheritance &&
                        rule.PropagationFlags == PropagationFlags.None);

                    if (!match)
                    {
                        issues.Add($"Configure auditoria de sucesso para 'Todos' em '{path}' com os direitos especificados.");
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    issues.Add($"Permissão insuficiente para ler configurações de auditoria em '{path}'.");
                }
            }

            if (issues.Count == 0)
            {
                return new ConfigurationAssessmentResult(
                    ConfigurationStatus.Compliant,
                    "Auditoria configurada para as pastas do BiometricLocalServicePlataform.");
            }

            return new ConfigurationAssessmentResult(
                ConfigurationStatus.NonCompliant,
                "Ajuste as configurações de auditoria das pastas: " + string.Join(" ", issues));
        }
        catch (Exception ex)
        {
            LogWriter.Write(ex, "Erro ao verificar configurações de integridade");
            return new ConfigurationAssessmentResult(
                ConfigurationStatus.Unknown,
                $"Não foi possível verificar as configurações de integridade: {ex.Message}");
        }
    }

    private static bool IsAdvancedAuditSubcategoryEnforced()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Lsa");
            if (key is null)
            {
                return false;
            }

            var value = key.GetValue("SCENoApplyLegacyAuditPolicy");
            return value switch
            {
                int intValue => intValue != 0,
                uint uintValue => uintValue != 0,
                string str when int.TryParse(str, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed != 0,
                _ => false
            };
        }
        catch (Exception ex)
        {
            LogWriter.Write(ex, "Erro ao verificar configuração de políticas de auditoria legadas");
            return false;
        }
    }

    private static async Task RemediateAuditPolicyAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await EnsureAuditSubcategoryEnforcementAsync(cancellationToken).ConfigureAwait(false);
        await ApplyBaseAuditPolicyAsync(cancellationToken).ConfigureAwait(false);
        await ApplyAdvancedAuditPolicyViaCsvAsync(cancellationToken).ConfigureAwait(false);

        LogWriter.Write("políticas de auditoria aplicadas conforme requisitos.");
    }

    private static async Task RemediateFirewallAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await RemediateFirewallUsingComAsync(cancellationToken).ConfigureAwait(false);

        LogWriter.Write("Firewall configurado: perfis ativos, notificações habilitadas e log ajustado para 4096 KB.");
    }

    private static Task RemediateFirewallUsingComAsync(CancellationToken cancellationToken) =>
        RunOnStaThreadAsync(() =>
        {
            object? policyCom = null;
            dynamic? policy = null;

            try
            {
                var policyType = Type.GetTypeFromProgID("HNetCfg.FwPolicy2")
                                 ?? throw new InvalidOperationException("Não foi possível acessar as configurações do Windows Firewall.");

                policyCom = Activator.CreateInstance(policyType) ?? throw new InvalidOperationException("Não foi possível carregar a política do Windows Firewall.");
                policy = policyCom;

                foreach (var (profile, _, registryName) in FirewallProfiles)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var profileIndex = (int)profile;

                    policy.FirewallEnabled[profileIndex] = true;
                    policy.BlockAllInboundTraffic[profileIndex] = false;
                    policy.NotificationsDisabled[profileIndex] = false;

                    using var loggingKey = Registry.LocalMachine.CreateSubKey(
                        $@"{FirewallPolicyRegistryPath}\{registryName}\Logging",
                        writable: true)
                        ?? throw new InvalidOperationException($"Não foi possível acessar o log do perfil {registryName} do Windows Firewall.");
                    loggingKey.SetValue("LogFileSize", FirewallLogSizeKilobytes, RegistryValueKind.DWord);
                    loggingKey.SetValue("LogDroppedPackets", 0, RegistryValueKind.DWord);
                    loggingKey.SetValue("LogSuccessfulConnections", 0, RegistryValueKind.DWord);
                }
            }
            finally
            {
                ReleaseComObject(policyCom);
            }
        }, cancellationToken);

    private static async Task RemediateTimeSynchronizationAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await Task.Run(() =>
        {
            using var serviceKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\W32Time", writable: true)
                ?? throw new InvalidOperationException("Não foi possível acessar o serviço Horário do Windows.");
            using var parametersKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\W32Time\Parameters", writable: true)
                ?? throw new InvalidOperationException("Não foi possível acessar as configurações do W32Time (Parameters).");
            using var ntpClientKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\W32Time\TimeProviders\NtpClient", writable: true)
                ?? throw new InvalidOperationException("Não foi possível acessar as configurações do W32Time (NtpClient).");

            if (Convert.ToInt32(serviceKey.GetValue("Start", 3), CultureInfo.InvariantCulture) == 4)
            {
                serviceKey.SetValue("Start", 3, RegistryValueKind.DWord);
            }

            parametersKey.SetValue("NtpServer", TimeSyncServerManualEntry, RegistryValueKind.String);
            parametersKey.SetValue("Type", "NTP", RegistryValueKind.String);
            ntpClientKey.SetValue("Enabled", 1, RegistryValueKind.DWord);
            ntpClientKey.SetValue("SpecialPollInterval", TimeSyncSpecialPollIntervalSeconds, RegistryValueKind.DWord);
        }, cancellationToken).ConfigureAwait(false);

        try
        {
            await RunProcessAsync("net.exe", "start W32Time", cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // net.exe também retorna erro quando o serviço já está em execução; o
            // w32tm abaixo confirma de forma definitiva se ele está disponível.
            LogWriter.Write(ex, "O serviço W32Time já estava ativo ou não pôde ser iniciado pelo net.exe");
        }

        await RunProcessAsync("w32tm.exe", $"/config /manualpeerlist:\"{TimeSyncServerManualEntry}\" /syncfromflags:manual /update", cancellationToken).ConfigureAwait(false);

        try
        {
            await RunProcessAsync("w32tm.exe", "/resync /rediscover", cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // A configuração permanece aplicada mesmo quando a rede ou o servidor
            // NTP não permitem uma sincronização imediata.
            LogWriter.Write(ex, "Configuração do W32Time aplicada, mas a sincronização imediata não foi concluída");
        }

        LogWriter.Write($"Sincronismo de hora ajustado para {TimeSyncServer} e sincronizado imediatamente.");
    }

    private static async Task RemediateBitLockerConfigurationAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await Task.Run(() =>
        {
            using var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\FVE");
            if (key is null)
            {
                throw new InvalidOperationException("Não foi possível acessar ou criar as políticas do BitLocker.");
            }

            key.SetValue("UseAdvancedStartup", 1, RegistryValueKind.DWord);
            key.SetValue("EnableBDEWithNoTPM", 1, RegistryValueKind.DWord);
            key.SetValue("UseTPM", 2, RegistryValueKind.DWord);
            key.SetValue("UseTPMPIN", 1, RegistryValueKind.DWord);
            key.SetValue("UseTPMKey", 1, RegistryValueKind.DWord);
            key.SetValue("UseTPMKeyPIN", 1, RegistryValueKind.DWord);
            key.SetValue("DisallowStandardUserPINReset", 1, RegistryValueKind.DWord);
            key.SetValue("OSPassphraseEnabled", 1, RegistryValueKind.DWord);
            key.SetValue("OSPassphraseComplexity", 1, RegistryValueKind.DWord);
            key.SetValue("OSPassphraseLength", 8, RegistryValueKind.DWord);
            key.SetValue("OSPassphraseRequireASCII", 0, RegistryValueKind.DWord);
        }, cancellationToken).ConfigureAwait(false);

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "control.exe",
                Arguments = "/name Microsoft.BitLockerDriveEncryption",
                UseShellExecute = true
            };

            Process.Start(psi);
        }
        catch (Exception ex)
        {
            LogWriter.Write(ex, "Falha ao abrir o painel do BitLocker.");
        }

        ShowInformation("Clique em 'Ligar BitLocker', escolha 'Inserir uma senha', salve a senha e a chave de recuperação. Selecione 'Criptografar a unidade inteira', 'Modo compatível', marque 'Executar verificacao do sistema BitLocker' e conclua o assistente. Reinicie o computador ao final.");

        LogWriter.Write("políticas do BitLocker aplicadas e painel aberto para o usuário concluir a criptografia.");
    }

    private static async Task RemediateIntegrityConfigurationAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await Task.Run(() =>
        {
            var everyone = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
            var rights =
                FileSystemRights.CreateFiles |
                FileSystemRights.CreateDirectories |
                FileSystemRights.WriteAttributes |
                FileSystemRights.WriteExtendedAttributes |
                FileSystemRights.DeleteSubdirectoriesAndFiles |
                FileSystemRights.Delete;
            var inheritance = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;

            foreach (var path in IntegrityAuditDirectories)
            {
                cancellationToken.ThrowIfCancellationRequested();

                Directory.CreateDirectory(path);

                var dirInfo = new DirectoryInfo(path);
                var security = dirInfo.GetAccessControl(AccessControlSections.Audit);
                security.PurgeAuditRules(everyone);

                var rule = new FileSystemAuditRule(
                    everyone,
                    rights,
                    inheritance,
                    PropagationFlags.None,
                    AuditFlags.Success);

                security.AddAuditRule(rule);
                dirInfo.SetAccessControl(security);

                LogWriter.Write($"Auditoria configurada para '{path}'.");
            }
        }, cancellationToken).ConfigureAwait(false);

        ShowInformation("Auditoria configurada. Utilize o Visualizador de Eventos para monitorar gravacoes nas pastas do BiometricLocalServicePlataform.");
    }

    private static async Task RemediateEventViewerApplicationLogAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var wevtutilSucceeded = false;

        try
        {
            await RunProcessAsync("wevtutil.exe", $"sl Application /ms:{ApplicationLogMaxSizeBytes}", cancellationToken).ConfigureAwait(false);
            wevtutilSucceeded = true;
        }
        catch (Exception ex)
        {
            LogWriter.Write(ex, "Falha ao ajustar o log Application via wevtutil. Tentando API gerenciada.");
        }

        if (!wevtutilSucceeded)
        {
            await Task.Run(() =>
            {
                using var configuration = new EventLogConfiguration("Application");
                configuration.MaximumSizeInBytes = ApplicationLogMaxSizeBytes;
                configuration.SaveChanges();
            }, cancellationToken).ConfigureAwait(false);
        }

        LogWriter.Write("Tamanho do log de Aplicativo ajustado para 4194240 KB.");
    }

    private static async Task RemediateUserAccountsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await DisableBuiltInAccountsAsync(cancellationToken).ConfigureAwait(false);

        var seedPlan = await Task.Run(BuildUserAccountsPlanSeed, cancellationToken).ConfigureAwait(false);
        var userPlan = ShowUserAccountsPlanDialog(seedPlan);
        if (userPlan is null)
        {
            ShowInformation("Configuracao de contas de usuario cancelada pelo usuario.");
            return;
        }

        await ApplyUserAccountsPlanAsync(userPlan, cancellationToken).ConfigureAwait(false);

        await ValidateUserAccountsStateAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task RemediateAccountLockoutPolicyAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var cfgPath = CreateScopedTemporaryPath($"config-auditoria-lockout-{Guid.NewGuid():N}.inf");
        var builder = new StringBuilder();
        builder.AppendLine("[Unicode]");
        builder.AppendLine("Unicode=yes");
        builder.AppendLine("[Version]");
        builder.AppendLine("signature=\"$CHICAGO$\"");
        builder.AppendLine("Revision=1");
        builder.AppendLine("[System Access]");
        builder.AppendLine("LockoutDuration = 10080");
        builder.AppendLine("LockoutBadCount = 3");
        builder.AppendLine("ResetLockoutCount = 1440");

        await File.WriteAllTextAsync(cfgPath, builder.ToString(), Encoding.Unicode, cancellationToken).ConfigureAwait(false);

        try
        {
            await ApplySecurityTemplateAsync(cfgPath, cancellationToken).ConfigureAwait(false);
            await RunProcessAsync("net.exe", "accounts /lockoutduration:10080 /lockoutthreshold:3 /lockoutwindow:1440", cancellationToken).ConfigureAwait(false);

            LogWriter.Write("política de bloqueio de conta aplicada (duracao, limite, redefinicao).");
        }
        finally
        {
            try
            {
                File.Delete(cfgPath);
            }
            catch
            {
                // ignore
            }
        }
    }

    private static async Task EnsureAuditSubcategoryEnforcementAsync(CancellationToken cancellationToken)
    {
        await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            SetDwordValue(
                Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\Lsa"),
                "SCENoApplyLegacyAuditPolicy",
                1);
        }, cancellationToken).ConfigureAwait(false);
    }

    private static async Task ApplyBaseAuditPolicyAsync(CancellationToken cancellationToken)
    {
        var cfgPath = CreateScopedTemporaryPath($"config-auditoria-event-audit-{Guid.NewGuid():N}.inf");
        var builder = new StringBuilder();
        builder.AppendLine("[Unicode]");
        builder.AppendLine("Unicode=yes");
        builder.AppendLine("[Version]");
        builder.AppendLine("signature=\"$CHICAGO$\"");
        builder.AppendLine("Revision=1");
        builder.AppendLine("[Event Audit]");

        foreach (var key in BaseAuditPolicyKeys)
        {
            var expectedValue = BaseAuditPolicyExpectedValues.TryGetValue(key, out var customExpected)
                ? customExpected
                : 0;

            builder.AppendLine($"{key} = {expectedValue}");
        }

        await File.WriteAllTextAsync(cfgPath, builder.ToString(), Encoding.Unicode, cancellationToken).ConfigureAwait(false);

        try
        {
            await ApplySecurityTemplateAsync(cfgPath, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            try
            {
                File.Delete(cfgPath);
            }
            catch
            {
                // ignore
            }
        }
    }

    private static async Task ApplyAdvancedAuditPolicyViaCsvAsync(CancellationToken cancellationToken)
    {
        var backupPath = CreateScopedTemporaryPath($"config-auditpol-backup-{Guid.NewGuid():N}.csv");
        var restorePath = CreateScopedTemporaryPath($"config-auditpol-restore-{Guid.NewGuid():N}.csv");

        try
        {
            RunAuditPol($"/backup /file:\"{backupPath}\"");
            var document = LoadAuditPolCsvDocument(backupPath);
            var modified = ApplyExpectedStatesToDocument(document);

            if (!modified)
            {
                LogWriter.Write("políticas de auditoria ja estavam alinhadas com o CSV de referencia.");
                return;
            }

            File.WriteAllLines(restorePath, document.ToCsvLines(), AuditPolEncoding);
            await RunProcessAsync("auditpol.exe", $"/restore /file:\"{restorePath}\"", cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogWriter.Write(ex, "Falha ao aplicar políticas via CSV. Tentando abordagem por subcategoria.");

            foreach (var setting in AdvancedAuditSettings)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await ApplyAdvancedAuditSettingAsync(setting, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            TryDeleteFile(backupPath);
            TryDeleteFile(restorePath);
        }
    }

    private static async Task ApplyAdvancedAuditSettingAsync(AdvancedAuditSetting setting, CancellationToken cancellationToken)
    {
        InvalidOperationException? lastException = null;

        foreach (var candidateName in GetAuditPolCommandNames(setting))
        {
            var arguments = BuildAuditPolArguments(candidateName, setting.ExpectedState);
            if (string.IsNullOrEmpty(arguments))
            {
                continue;
            }

            var fallbackArguments = setting.ExpectedState == AuditSettingState.NotConfigured
                ? BuildAuditPolArguments(candidateName, AuditSettingState.NoAuditing)
                : null;

            try
            {
                await RunProcessAsync("auditpol.exe", arguments, cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (InvalidOperationException ex) when (fallbackArguments is not null)
            {
                LogWriter.Write($"Fallback auditpol para '{candidateName}' devido a erro: {ex.Message}");

                try
                {
                    await RunProcessAsync("auditpol.exe", fallbackArguments, cancellationToken).ConfigureAwait(false);
                    return;
                }
                catch (InvalidOperationException fallbackEx)
                {
                    lastException = fallbackEx;
                }
            }
            catch (InvalidOperationException ex)
            {
                lastException = ex;
            }
        }

        if (lastException is not null)
        {
            throw lastException;
        }
    }

    private static string BuildAuditPolArguments(string subcategoryName, AuditSettingState state) =>
        state switch
        {
            AuditSettingState.NotConfigured => $"/clear /subcategory:\"{subcategoryName}\"",
            AuditSettingState.NoAuditing => $"/set /subcategory:\"{subcategoryName}\" /success:disable /failure:disable",
            AuditSettingState.SuccessOnly => $"/set /subcategory:\"{subcategoryName}\" /success:enable /failure:disable",
            AuditSettingState.FailureOnly => $"/set /subcategory:\"{subcategoryName}\" /success:disable /failure:enable",
            AuditSettingState.SuccessAndFailure => $"/set /subcategory:\"{subcategoryName}\" /success:enable /failure:enable",
            _ => string.Empty
        };

    private static UserAccountsPlan? ShowUserAccountsPlanDialog(IReadOnlyList<LocalUserAccountPlanItem> existingAccounts)
    {
        var ui = UiService;
        if (ui is null)
        {
            return null;
        }

        try
        {
            return ui.PromptUserAccountsPlanAsync(existingAccounts).GetAwaiter().GetResult();
        }
        catch
        {
            return null;
        }
    }

    private static List<LocalUserAccountPlanItem> BuildUserAccountsPlanSeed()
    {
        using var context = new PrincipalContext(ContextType.Machine);
        var builtInSidSet = new HashSet<string>(
            ResolveBuiltInAccountSids().Select(info => info.Sid),
            StringComparer.OrdinalIgnoreCase);

        using var adminGroup = GroupPrincipal.FindByIdentity(context, IdentityType.Sid, AdministratorsGroupSid)
            ?? throw new InvalidOperationException("Nao foi possivel localizar o grupo de administradores locais.");

        var adminMemberSidSet = GetGroupMemberSidSet(adminGroup);
        var seed = new List<LocalUserAccountPlanItem>();

        foreach (var account in EnumerateLocalUserAccounts(context))
        {
            if (!account.Enabled || builtInSidSet.Contains(account.Sid))
            {
                continue;
            }

            var profile = adminMemberSidSet.Contains(account.Sid)
                ? LocalUserProfile.Administrator
                : LocalUserProfile.User;

            seed.Add(new LocalUserAccountPlanItem(
                account.Name,
                "********",
                profile,
                true,
                null));
        }

        return seed
            .OrderBy(item => item.UserName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static async Task ApplyUserAccountsPlanAsync(UserAccountsPlan plan, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ValidateUserAccountsPlan(plan);

        foreach (var account in plan.Accounts.Where(account => !account.IsExistingAccount))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (account.NewUserDefinition is null)
            {
                throw new InvalidOperationException($"Dados incompletos para criar o usuario '{account.UserName}'.");
            }

            await CreateLocalUserAsync(
                account.NewUserDefinition,
                account.Profile == LocalUserProfile.Administrator,
                cancellationToken).ConfigureAwait(false);
        }

        await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var context = new PrincipalContext(ContextType.Machine);
            using var adminGroup = GroupPrincipal.FindByIdentity(context, IdentityType.Sid, AdministratorsGroupSid)
                ?? throw new InvalidOperationException("Nao foi possivel localizar o grupo de administradores locais.");
            using var usersGroup = GroupPrincipal.FindByIdentity(context, IdentityType.Sid, UsersGroupSid)
                ?? throw new InvalidOperationException("Nao foi possivel localizar o grupo de usuarios locais.");

            var adminMembers = GetGroupMemberSidSet(adminGroup);
            var userMembers = GetGroupMemberSidSet(usersGroup);
            var adminGroupChanged = false;
            var usersGroupChanged = false;

            foreach (var item in plan.Accounts)
            {
                cancellationToken.ThrowIfCancellationRequested();

                using var user = UserPrincipal.FindByIdentity(context, IdentityType.SamAccountName, item.UserName)
                    ?? throw new InvalidOperationException($"Nao foi possivel localizar o usuario '{item.UserName}'.");

                var sid = user.Sid?.Value
                    ?? throw new InvalidOperationException($"Nao foi possivel ler o SID do usuario '{item.UserName}'.");

                if (item.Profile == LocalUserProfile.Administrator)
                {
                    if (!adminMembers.Contains(sid))
                    {
                        adminGroup.Members.Add(user);
                        adminMembers.Add(sid);
                        adminGroupChanged = true;
                    }
                }
                else
                {
                    if (adminMembers.Contains(sid))
                    {
                        adminGroup.Members.Remove(user);
                        adminMembers.Remove(sid);
                        adminGroupChanged = true;
                    }

                    if (!userMembers.Contains(sid))
                    {
                        usersGroup.Members.Add(user);
                        userMembers.Add(sid);
                        usersGroupChanged = true;
                    }
                }
            }

            if (adminGroupChanged)
            {
                adminGroup.Save();
            }

            if (usersGroupChanged)
            {
                usersGroup.Save();
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    private static void ValidateUserAccountsPlan(UserAccountsPlan plan)
    {
        if (plan.Accounts.Count == 0)
        {
            throw new InvalidOperationException("A configuracao exige no minimo uma conta Administrador e uma conta Usuario.");
        }

        var duplicateNames = plan.Accounts
            .GroupBy(account => account.UserName, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        if (duplicateNames.Count > 0)
        {
            throw new InvalidOperationException("Existem usuarios duplicados na configuracao: " + string.Join(", ", duplicateNames) + ".");
        }

        var adminCount = plan.Accounts.Count(account => account.Profile == LocalUserProfile.Administrator);
        var userCount = plan.Accounts.Count(account => account.Profile == LocalUserProfile.User);

        if (adminCount == 0)
        {
            throw new InvalidOperationException("A configuracao exige exatamente 1 conta com perfil Administrador.");
        }

        if (adminCount > 1)
        {
            throw new InvalidOperationException("A configuracao permite apenas 1 conta com perfil Administrador.");
        }

        if (userCount == 0)
        {
            throw new InvalidOperationException("A configuracao exige ao menos 1 conta com perfil Usuario.");
        }
    }

    private static HashSet<string> GetGroupMemberSidSet(GroupPrincipal group)
    {
        var members = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var principal in group.GetMembers(true))
        {
            using (principal)
            {
                var sid = principal.Sid?.Value;
                if (!string.IsNullOrWhiteSpace(sid))
                {
                    members.Add(sid);
                }
            }
        }

        return members;
    }

    private static async Task DisableBuiltInAccountsAsync(CancellationToken cancellationToken)
    {
        await Task.Run(() =>
        {
            using var context = new PrincipalContext(ContextType.Machine);
            var targetSids = ResolveBuiltInAccountSids();
            if (targetSids.Count == 0)
            {
                throw new InvalidOperationException("Não foi possível localizar as contas Administrador/Convidado nesta maquina.");
            }

            foreach (var info in targetSids)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    using var account = UserPrincipal.FindByIdentity(context, IdentityType.Sid, info.Sid);
                    if (account is null)
                    {
                        continue;
                    }

                    if (account.Enabled != false)
                    {
                        account.Enabled = false;
                        account.Save();
                        LogWriter.Write($"Conta '{account.SamAccountName}' desativada.");
                    }
                }
                catch (Exception ex)
                {
                    LogWriter.Write(ex, $"Falha ao desativar conta com SID {info.Sid}.");
                    throw;
                }
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    private static async Task CreateLocalUserAsync(LocalUserDefinition definition, bool isAdmin, CancellationToken cancellationToken)
    {
        await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var context = new PrincipalContext(ContextType.Machine);

            using var existing = UserPrincipal.FindByIdentity(context, IdentityType.SamAccountName, definition.UserName);
            if (existing is not null)
            {
                throw new InvalidOperationException($"O usuário '{definition.UserName}' ja existe.");
            }

            var user = new UserPrincipal(context)
            {
                SamAccountName = definition.UserName,
                Name = definition.UserName,
                DisplayName = definition.FullName ?? definition.UserName,
                Description = definition.Description,
                Enabled = !definition.IsDisabled,
                UserCannotChangePassword = definition.UserCannotChangePassword,
                PasswordNeverExpires = definition.PasswordNeverExpires
            };

            try
            {
                user.SetPassword(definition.Password);
                user.Save();

                if (definition.MustChangePassword)
                {
                    user.ExpirePasswordNow();
                    user.Save();
                }

                var groupSid = isAdmin ? AdministratorsGroupSid : UsersGroupSid;
                using var group = GroupPrincipal.FindByIdentity(context, IdentityType.Sid, groupSid)
                    ?? throw new InvalidOperationException(isAdmin
                        ? "Não foi possível localizar o grupo de administradores locais."
                        : "Não foi possível localizar o grupo de usuários locais.");

                if (!group.Members.Contains(user))
                {
                    group.Members.Add(user);
                    group.Save();
                }

                LogWriter.Write(isAdmin
                    ? $"Usuário administrativo '{definition.UserName}' criado."
                    : $"Usuário padrao '{definition.UserName}' criado.");
            }
            finally
            {
                user.Dispose();
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    private static async Task ValidateUserAccountsStateAsync(CancellationToken cancellationToken)
    {
        try
        {
            var evaluation = await Task.Run(EvaluateUserAccountState, cancellationToken).ConfigureAwait(false);
            var summary = BuildUserAccountSummary(evaluation);

            if (evaluation.Issues.Count == 0)
            {
                ShowInformation(summary);
            }
            else
            {
                ShowError(string.Join(" ", evaluation.Issues) + " " + summary);
            }
        }
        catch (Exception ex)
        {
            LogWriter.Write(ex, "Falha ao avaliar o estado das contas locais.");
            ShowError($"Não foi possível avaliar as contas de usuário: {ex.Message}");
        }
    }

    private static UserAccountEvaluation EvaluateUserAccountState()
    {
        using var context = new PrincipalContext(ContextType.Machine);
        var evaluation = new UserAccountEvaluation();
        evaluation.Snapshot.AddRange(EnumerateLocalUserAccounts(context));

        var builtIns = ResolveBuiltInAccountSids();
        var builtInSidSet = new HashSet<string>(builtIns.Select(info => info.Sid), StringComparer.OrdinalIgnoreCase);
        var customAdminNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var standardUserNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var adminDisabled = true;
        var guestDisabled = true;

        foreach (var info in builtIns)
        {
            using var account = UserPrincipal.FindByIdentity(context, IdentityType.Sid, info.Sid);
            if (account is null)
            {
                continue;
            }

            var enabled = account.Enabled != false;
            if (info.Type == WellKnownSidType.AccountAdministratorSid)
            {
                adminDisabled = !enabled;
            }
            else if (info.Type == WellKnownSidType.AccountGuestSid)
            {
                guestDisabled = !enabled;
            }
        }

        evaluation.AdministratorDisabled = adminDisabled;
        evaluation.GuestDisabled = guestDisabled;

        if (!adminDisabled)
        {
            evaluation.Issues.Add("Desative a conta padrao 'Administrador'.");
        }

        if (!guestDisabled)
        {
            evaluation.Issues.Add("Desative a conta padrao 'Convidado'.");
        }

        using var adminGroup = GroupPrincipal.FindByIdentity(context, IdentityType.Sid, AdministratorsGroupSid)
            ?? throw new InvalidOperationException("Não foi possível localizar o grupo de administradores locais.");

        var adminMemberSidSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var principal in adminGroup.GetMembers(true))
        {
            using (principal)
            {
                if (principal is not UserPrincipal user)
                {
                    continue;
                }

                var sid = user.Sid?.Value;
                if (string.IsNullOrEmpty(sid))
                {
                    continue;
                }

                adminMemberSidSet.Add(sid);

                if (user.Enabled == false)
                {
                    continue;
                }

                if (!builtInSidSet.Contains(sid))
                {
                    var name = user.SamAccountName ?? user.Name ?? sid;
                    if (customAdminNames.Add(name))
                    {
                        evaluation.CustomAdministrators.Add(name);
                    }
                }
            }
        }

        if (evaluation.CustomAdministrators.Count == 0)
        {
            evaluation.Issues.Add("Crie e mantenha habilitada apenas uma conta administrativa personalizada (ex.: Suporte).");
        }
        else if (evaluation.CustomAdministrators.Count > 1)
        {
            evaluation.Issues.Add("Existe mais de uma conta com perfil de administrador. Remova ou rebaixe as demais contas administrativas.");
        }

        using var usersGroup = GroupPrincipal.FindByIdentity(context, IdentityType.Sid, UsersGroupSid)
            ?? throw new InvalidOperationException("Não foi possível localizar o grupo de usuários locais.");
        var userMemberSidSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var principal in usersGroup.GetMembers(true))
        {
            using (principal)
            {
                if (principal is not UserPrincipal user)
                {
                    continue;
                }

                var sid = user.Sid?.Value;
                if (string.IsNullOrEmpty(sid) || user.Enabled == false)
                {
                    continue;
                }

                userMemberSidSet.Add(sid);

                if (!builtInSidSet.Contains(sid) && !adminMemberSidSet.Contains(sid))
                {
                    var name = user.SamAccountName ?? user.Name ?? sid;
                    if (standardUserNames.Add(name))
                    {
                        evaluation.StandardUsers.Add(name);
                    }
                }
            }
        }

        if (evaluation.StandardUsers.Count == 0)
        {
            evaluation.Issues.Add("Crie ao menos um usuário padrao ativo no grupo 'Usuários'.");
        }

        var strayAccounts = evaluation.Snapshot
            .Where(account => account.Enabled &&
                              !builtInSidSet.Contains(account.Sid) &&
                              !adminMemberSidSet.Contains(account.Sid) &&
                              !userMemberSidSet.Contains(account.Sid))
            .Select(account => account.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (strayAccounts.Count > 0)
        {
            evaluation.Issues.Add("As seguintes contas ativas não pertencem ao grupo 'Usuários': " + string.Join(", ", strayAccounts) + ".");
        }

        return evaluation;
    }

    private static List<LocalAccountInfo> EnumerateLocalUserAccounts(PrincipalContext context)
    {
        var result = new List<LocalAccountInfo>();
        using var prototype = new UserPrincipal(context);
        using var searcher = new PrincipalSearcher(prototype);
        using var results = searcher.FindAll();

        foreach (var principal in results)
        {
            using (principal)
            {
                if (principal is not UserPrincipal user)
                {
                    continue;
                }

                var sid = user.Sid?.Value;
                if (string.IsNullOrEmpty(sid))
                {
                    continue;
                }

                var name = !string.IsNullOrWhiteSpace(user.SamAccountName)
                    ? user.SamAccountName!
                    : (!string.IsNullOrWhiteSpace(user.Name) ? user.Name! : sid);

                result.Add(new LocalAccountInfo(name, sid, user.Enabled != false));
            }
        }

        return result;
    }

    private static string BuildUserAccountSummary(UserAccountEvaluation evaluation)
    {
        var builtInStatus =
            $"Administrador padrão: {(evaluation.AdministratorDisabled ? "desativado" : "ativo")}. " +
            $"Convidado: {(evaluation.GuestDisabled ? "desativado" : "ativo")}.";

        string DescribeList(IReadOnlyList<string> values, string emptyMessage, string prefix) =>
            values.Count switch
            {
                0 => emptyMessage,
                1 => $"{prefix}{values[0]}",
                _ => $"{prefix}{string.Join(", ", values)}"
            };

        var adminStatus = DescribeList(
            evaluation.CustomAdministrators,
            "Nenhuma conta administrativa personalizada ativa.",
            "Conta(s) administrativas: ");

        var userStatus = DescribeList(
            evaluation.StandardUsers,
            "Nenhum usuário padrão ativo identificado.",
            "Usuários padrão ativos: ");

        var activeCount = evaluation.Snapshot.Count(account => account.Enabled);
        var inactiveCount = evaluation.Snapshot.Count - activeCount;

        return string.Join(
            Environment.NewLine,
            builtInStatus,
            adminStatus,
            userStatus,
            $"Contas ativas: {activeCount}; contas desativadas: {inactiveCount}.");
    }

    private static Dictionary<string, string> ExportSecurityPolicySection(string sectionName)
    {
        var cfgPath = CreateScopedTemporaryPath($"config-auditoria-secpol-{Guid.NewGuid():N}.inf");

        try
        {
            RunProcess("secedit.exe", $"/export /cfg \"{cfgPath}\" /quiet");

            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var lines = File.ReadAllLines(cfgPath);
            var inSection = false;

            foreach (var rawLine in lines)
            {
                var line = rawLine.Trim();

                if (string.IsNullOrWhiteSpace(line) || line.StartsWith(";", StringComparison.Ordinal))
                {
                    continue;
                }

                if (line.Equals($"[{sectionName}]", StringComparison.OrdinalIgnoreCase))
                {
                    inSection = true;
                    continue;
                }

                if (line.StartsWith("[", StringComparison.Ordinal) && inSection)
                {
                    break;
                }

                if (!inSection)
                {
                    continue;
                }

                var parts = line.Split('=', 2);
                if (parts.Length == 2)
                {
                    result[parts[0].Trim()] = parts[1].Trim();
                }
            }

            return result;
        }
        finally
        {
            try
            {
                File.Delete(cfgPath);
            }
            catch
            {
                // ignore cleanup failures
            }
        }
    }

    private static Dictionary<string, AdvancedAuditSetting> CreateAdvancedAuditLookup()
    {
        var lookup = new Dictionary<string, AdvancedAuditSetting>(StringComparer.OrdinalIgnoreCase);

        foreach (var setting in AdvancedAuditSettings)
        {
            foreach (var name in setting.GetAllNames())
            {
                var normalized = NormalizeAuditName(name);
                if (string.IsNullOrEmpty(normalized))
                {
                    continue;
                }

                if (!lookup.ContainsKey(normalized))
                {
                    lookup[normalized] = setting;
                }
            }

            if (LocalizedAuditAliases.TryGetValue(setting.Name, out var localizedAliases))
            {
                foreach (var alias in localizedAliases)
                {
                    var normalizedAlias = NormalizeAuditName(alias);
                    if (normalizedAlias.Length == 0 || lookup.ContainsKey(normalizedAlias))
                    {
                        continue;
                    }

                    lookup[normalizedAlias] = setting;

                    var strippedAlias = StripAuditPrefixes(normalizedAlias);
                    if (!string.Equals(strippedAlias, normalizedAlias, StringComparison.Ordinal) &&
                        strippedAlias.Length > 0 &&
                        !lookup.ContainsKey(strippedAlias))
                    {
                        lookup[strippedAlias] = setting;
                    }
                }
            }
        }

        return lookup;
    }

    private static Dictionary<string, AdvancedAuditSetting> CreateAdvancedAuditGuidLookup()
    {
        var lookup = new Dictionary<string, AdvancedAuditSetting>(StringComparer.OrdinalIgnoreCase);

        foreach (var setting in AdvancedAuditSettings)
        {
            var guid = NormalizeGuid(setting.SubcategoryId);
            if (guid.Length == 0 || lookup.ContainsKey(guid))
            {
                continue;
            }

            lookup[guid] = setting;
        }

        return lookup;
    }

    private static AdvancedAuditSetting? ResolveAdvancedAuditSetting(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        EnsureLocalizedAuditSubcategories();

        if (TryGetGuidForLocalizedName(name, out var guid) &&
            AdvancedAuditSettingByGuid.TryGetValue(guid, out var settingByGuid))
        {
            RegisterAuditPolDisplayName(settingByGuid.Name, name);
            return settingByGuid;
        }

        var normalized = NormalizeAuditName(name);
        if (string.IsNullOrEmpty(normalized))
        {
            return null;
        }

        if (AdvancedAuditSettingLookup.TryGetValue(normalized, out var setting))
        {
            return setting;
        }

        var stripped = StripAuditPrefixes(normalized);
        if (!string.Equals(stripped, normalized, StringComparison.Ordinal) &&
            AdvancedAuditSettingLookup.TryGetValue(stripped, out setting))
        {
            return setting;
        }

        return null;
    }

    private static string NormalizeAuditName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var withoutDiacritics = RemoveDiacritics(value).ToLowerInvariant();
        var builder = new StringBuilder(withoutDiacritics.Length);

        foreach (var ch in withoutDiacritics)
        {
            if (char.IsLetterOrDigit(ch) || char.IsWhiteSpace(ch))
            {
                builder.Append(ch);
            }
        }

        var parts = builder.ToString().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        return string.Join(" ", parts);
    }

    private static string NormalizeGuid(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim().Trim('{', '}');
        return trimmed.ToUpperInvariant();
    }

    private static string FormatGuidForAuditPol(string value)
    {
        var normalized = NormalizeGuid(value);
        return normalized.Length == 0 ? string.Empty : $"{{{normalized}}}";
    }

    private static void EnsureLocalizedAuditSubcategories()
    {
        lock (AuditSubcategoryNamesLock)
        {
            if (_localizedAuditSubcategoriesLoaded)
            {
                return;
            }

            try
            {
                var output = RunAuditPol("/list /subcategory:* /v");
                var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

                foreach (var rawLine in lines)
                {
                    if (!rawLine.StartsWith("  ", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var trimmed = rawLine.Trim();
                    var braceIndex = trimmed.LastIndexOf('{');
                    if (braceIndex <= 0 || !trimmed.EndsWith("}", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var namePart = trimmed.Substring(0, braceIndex).Trim();
                    var guidPart = trimmed.Substring(braceIndex).Trim();

                    var normalizedName = NormalizeAuditName(namePart);
                    var normalizedGuid = NormalizeGuid(guidPart);

                    if (normalizedName.Length == 0 || normalizedGuid.Length == 0)
                    {
                        continue;
                    }

                    LocalizedAuditSubcategoryGuids[normalizedName] = normalizedGuid;

                    if (AdvancedAuditSettingByGuid.TryGetValue(normalizedGuid, out var setting))
                    {
                        ObservedAuditSubcategoryNames[setting.Name] = namePart;
                    }
                }
            }
            catch (Exception ex)
            {
                LogWriter.Write(ex, "Falha ao mapear nomes localizados das subcategorias do auditpol.");
            }
            finally
            {
                _localizedAuditSubcategoriesLoaded = true;
            }
        }
    }

    private static bool IsTargetWindowsEdition(string? editionId)
    {
        if (string.IsNullOrWhiteSpace(editionId))
        {
            return false;
        }

        var normalized = editionId.Trim();
        if (AllowedEditionIds.Contains(normalized))
        {
            return true;
        }

        return normalized.Contains("Professional", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("Enterprise", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetGuidForLocalizedName(string name, out string guid)
    {
        var normalizedName = NormalizeAuditName(name);
        if (string.IsNullOrEmpty(normalizedName))
        {
            guid = string.Empty;
            return false;
        }

        lock (AuditSubcategoryNamesLock)
        {
            if (LocalizedAuditSubcategoryGuids.TryGetValue(normalizedName, out var value))
            {
                guid = value;
                return true;
            }
        }

        guid = string.Empty;
        return false;
    }

    private static string StripAuditPrefixes(string value)
    {
        foreach (var prefix in AuditNamePrefixes)
        {
            if (value.StartsWith(prefix, StringComparison.Ordinal))
            {
                return value[prefix.Length..].TrimStart();
            }
        }

        return value;
    }

    private static Dictionary<string, AuditSettingState> GetAdvancedAuditPolicySettings()
    {
        _auditPolSplitWarnings = 0;
        _auditPolStateWarnings = 0;

        try
        {
            var document = ExportAuditPolDocument();
            var parsed = ConvertAuditPolRecordsToStates(document.Records);
            if (parsed.Count > 0)
            {
                return parsed;
            }
        }
        catch (Exception ex)
        {
            LogWriter.Write(ex, "Falha ao consultar auditpol via backup. Tentando modo legado.");
        }

        return GetAdvancedAuditPolicySettingsLegacy();
    }

    private static Dictionary<string, AuditSettingState> GetAdvancedAuditPolicySettingsLegacy()
    {
        var aggregateCommands = new[]
        {
            "/get /category:* /fo csv",
            "/get /category:\"*\" /fo csv",
            "/get /category:*",
            "/get /category:\"*\""
        };

        foreach (var command in aggregateCommands)
        {
            try
            {
                var output = RunAuditPol(command);
                var parsed = ParseAuditPolOutput(output);
                if (parsed.Count > 0)
                {
                    return parsed;
                }
            }
            catch (InvalidOperationException ex)
            {
                LogWriter.Write(ex, $"Falha ao executar '{command}' via auditpol.");
            }
        }

        var result = new Dictionary<string, AuditSettingState>(StringComparer.OrdinalIgnoreCase);

        foreach (var setting in AdvancedAuditSettings)
        {
            try
            {
                var state = GetAuditSettingState(setting);
                result[setting.Name] = state;
            }
            catch (Exception ex)
            {
                LogWriter.Write(ex, $"Falha ao obter configuração da subcategoria '{setting.Name}' via auditpol.");
            }
        }

        return result;
    }

    private static AuditPolCsvDocument ExportAuditPolDocument()
    {
        var tempPath = CreateScopedTemporaryPath($"config-auditpol-{Guid.NewGuid():N}.csv");
        try
        {
            RunAuditPol($"/backup /file:\"{tempPath}\"");
            return LoadAuditPolCsvDocument(tempPath);
        }
        finally
        {
            TryDeleteFile(tempPath);
        }
    }

    private static Dictionary<string, AuditSettingState> ConvertAuditPolRecordsToStates(IEnumerable<AuditPolCsvRecord> records)
    {
        var result = new Dictionary<string, AuditSettingState>(StringComparer.OrdinalIgnoreCase);

        foreach (var record in records)
        {
            var guid = NormalizeGuid(record.SubcategoryGuid);
            if (string.IsNullOrEmpty(guid))
            {
                continue;
            }

            if (!TryParseAuditStateFromCsv(record, out var state))
            {
                continue;
            }

            if (AdvancedAuditSettingByGuid.TryGetValue(guid, out var definition))
            {
                RegisterAuditPolDisplayName(definition.Name, record.Subcategory);
                result[definition.Name] = state;
            }
            else
            {
                result[guid] = state;
            }
        }

        return result;
    }

    private static bool TryParseAuditStateFromCsv(AuditPolCsvRecord record, out AuditSettingState state)
    {
        var valueText = record.ConfigurationValue?.Trim();
        if (!string.IsNullOrEmpty(valueText) && int.TryParse(valueText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numeric))
        {
            switch (numeric)
            {
                case 0:
                    state = AuditSettingState.NoAuditing;
                    return true;
                case 1:
                    state = AuditSettingState.SuccessOnly;
                    return true;
                case 2:
                    state = AuditSettingState.FailureOnly;
                    return true;
                case 3:
                    state = AuditSettingState.SuccessAndFailure;
                    return true;
            }
        }

        if (!string.IsNullOrWhiteSpace(record.IncludeSetting) && TryParseAuditSetting(record.IncludeSetting, out state))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(record.ExcludeSetting) && TryParseAuditSetting(record.ExcludeSetting, out state))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(valueText) && string.IsNullOrWhiteSpace(record.IncludeSetting))
        {
            state = AuditSettingState.NotConfigured;
            return true;
        }

        state = default;
        return false;
    }

    private static string RunAuditPol(string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "auditpol.exe",
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = AuditPolEncoding,
            StandardErrorEncoding = AuditPolEncoding,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Não foi possível iniciar auditpol.exe.");

        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();

        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            if (!string.IsNullOrWhiteSpace(error) && error.Contains("0x00000522", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("auditpol.exe requer privilégios administrativos. Execute o aplicativo como Administrador.");
            }

            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(error)
                    ? $"auditpol.exe retornou codigo {process.ExitCode}."
                    : $"auditpol.exe retornou codigo {process.ExitCode}: {error.Trim()}");
        }

        if (string.IsNullOrWhiteSpace(output))
        {
            throw new InvalidOperationException("auditpol.exe não retornou dados.");
        }

        return output;
    }

    private static Dictionary<string, AuditSettingState> ParseAuditPolOutput(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return new Dictionary<string, AuditSettingState>(StringComparer.OrdinalIgnoreCase);
        }

        if (output.Contains("\",\"", StringComparison.Ordinal))
        {
            return ParseAuditPolCsv(output);
        }

        return ParseAuditPolTable(output);
    }

    private static Dictionary<string, AuditSettingState> ParseAuditPolCsv(string output)
    {
        var result = new Dictionary<string, AuditSettingState>(StringComparer.OrdinalIgnoreCase);
        var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        for (var i = 1; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var parts = SplitCsvLine(line);
            if (parts.Length < 3)
            {
                continue;
            }

            var subcategory = parts[1];
            var setting = parts[2];

            if (!TryParseAuditSetting(setting, out var parsed))
            {
                continue;
            }

            var definition = ResolveAdvancedAuditSetting(subcategory);
            var key = definition?.Name ?? subcategory;

            if (definition is not null)
            {
                RegisterAuditPolDisplayName(definition.Name, subcategory);
            }

            if (!result.ContainsKey(key))
            {
                result[key] = parsed;
            }
        }

        return result;
    }

    private static Dictionary<string, AuditSettingState> ParseAuditPolTable(string output)
    {
        var result = new Dictionary<string, AuditSettingState>(StringComparer.OrdinalIgnoreCase);
        var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var rawLine in lines)
        {
            if (string.IsNullOrWhiteSpace(rawLine))
            {
                continue;
            }

            if (!char.IsWhiteSpace(rawLine[0]))
            {
                // Header or category row; skip.
                continue;
            }

            var trimmed = rawLine.Trim();
            var normalized = RemoveDiacritics(trimmed).ToLowerInvariant();
            if (normalized.Contains("categoria/subcategoria") ||
                normalized.Contains("category/subcategory") ||
                normalized.Contains("configuracao") ||
                normalized.Contains("setting"))
            {
                continue;
            }

            if (!TrySplitSubcategoryAndState(trimmed, out var subcategory, out var stateText))
            {
                LogAuditPolSplitWarning(trimmed);
                continue;
            }

            if (!TryParseAuditSetting(stateText, out var parsed))
            {
                LogAuditPolStateWarning(stateText);
                continue;
            }

            var definition = ResolveAdvancedAuditSetting(subcategory);
            var key = definition?.Name ?? subcategory;

            if (definition is not null)
            {
                RegisterAuditPolDisplayName(definition.Name, subcategory);
            }

            if (!result.ContainsKey(key))
            {
                result[key] = parsed;
            }
        }

        return result;
    }

    private static string[] SplitCsvLine(string line)
    {
        if (string.IsNullOrEmpty(line))
        {
            return Array.Empty<string>();
        }

        var cleaned = line.Trim();
        if (cleaned.Length == 0)
        {
            return Array.Empty<string>();
        }

        cleaned = cleaned.TrimStart('\uFEFF');

        var parts = cleaned.Split(new[] { "\",\"" }, StringSplitOptions.None);
        for (var i = 0; i < parts.Length; i++)
        {
            parts[i] = parts[i].Trim().Trim('"');
        }

        return parts;
    }

    private static AuditPolCsvDocument LoadAuditPolCsvDocument(string path)
    {
        var lines = File.ReadAllLines(path, AuditPolEncoding);
        if (lines.Length == 0)
        {
            throw new InvalidOperationException($"O arquivo '{path}' retornado pelo auditpol esta vazio.");
        }

        var header = lines[0];
        var records = new List<AuditPolCsvRecord>(lines.Length - 1);

        for (var i = 1; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var columns = ParseCsvLineFlexible(line);
            if (columns.Length < 7)
            {
                Array.Resize(ref columns, 7);
            }

            records.Add(new AuditPolCsvRecord(columns));
        }

        return new AuditPolCsvDocument(header, records);
    }

    private static string[] ParseCsvLineFlexible(string line)
    {
        var fields = SplitCsvLine(line);
        if (fields.Length > 1)
        {
            return fields;
        }

        if (!line.Contains(','))
        {
            return new[] { line };
        }

        return line
            .Split(',')
            .Select(part => part.Trim().Trim('"'))
            .ToArray();
    }

    private static bool TryParseAuditSetting(string value, out AuditSettingState result)
    {
        var normalized = NormalizeAuditValue(value);

        switch (normalized)
        {
            case "success and failure":
            case "sucesso e falha":
            case "exito e falha":
                result = AuditSettingState.SuccessAndFailure;
                return true;
            case "success":
            case "sucesso":
            case "exito":
                result = AuditSettingState.SuccessOnly;
                return true;
            case "failure":
            case "falha":
                result = AuditSettingState.FailureOnly;
                return true;
            case "no auditing":
            case "sem auditoria":
            case "nenhuma auditoria":
                result = AuditSettingState.NoAuditing;
                return true;
            case "not configured":
            case "não configurado":
            case "não configurada":
                result = AuditSettingState.NotConfigured;
                return true;
            default:
                result = default;
                return false;
        }
    }

    private static bool TryParseSingleAuditState(string output, out AuditSettingState state)
    {
        var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line) || !char.IsWhiteSpace(line[0]))
            {
                continue;
            }

            var trimmed = line.Trim();
            if (!TrySplitSubcategoryAndState(trimmed, out _, out var stateSegment))
            {
                continue;
            }

            if (TryParseAuditSetting(stateSegment, out state))
            {
                return true;
            }
        }

        state = default;
        return false;
    }

    private static readonly string[][] StatusTokens =
    {
        new[]
        {
            "\u00CAxito e Falha",
            "\u00CAxito e falha",
            "\u00EAxito e Falha",
            "\u00EAxito e falha",
            "\u00D2xito e Falha",
            "\u00D2xito e falha",
            "Exito e Falha",
            "Exito e falha",
            "Sucesso e Falha",
            "Sucesso e falha",
            "Success and Failure"
        },
        new[]
        {
            "\u00CAxito",
            "\u00EAxito",
            "\u00D2xito",
            "Exito",
            "Sucesso",
            "Success"
        },
        new[]
        {
            "Falha",
            "Failure"
        },
        new[]
        {
            "Sem Auditoria",
            "Nenhuma Auditoria",
            "Nenhuma auditoria",
            "No Auditing",
            "sem auditoria"
        },
        new[]
        {
            "N\u00E3o configurado",
            "N\u00E3o configurada",
            "Não configurado",
            "Não configurada",
            "Not Configured"
        }
    };

    private static readonly string[] StatusTokenSearchOrder =
        StatusTokens
            .SelectMany(tokens => tokens)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(token => token.Length)
            .ToArray();

    private static Encoding InitializeAuditPolEncoding()
    {
        try
        {
            var oemCodePage = CultureInfo.CurrentCulture.TextInfo.OEMCodePage;
            return Encoding.GetEncoding(oemCodePage);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            LogWriter.Write(ex, "Não foi possível obter a page code OEM; tentando encoding do console.");
        }

        try
        {
            var consoleEncoding = Console.OutputEncoding;
            if (consoleEncoding is not null)
            {
                return consoleEncoding;
            }
        }
        catch (Exception ex) when (ex is IOException or SecurityException)
        {
            LogWriter.Write(ex, "Não foi possível ler Console.OutputEncoding; usando UTF-8.");
        }

        return Encoding.UTF8;
    }

    private static void LogAuditPolSplitWarning(string line)
    {
        if (_auditPolSplitWarnings >= AuditPolParseWarningLimit)
        {
            return;
        }

        _auditPolSplitWarnings++;
        LogWriter.Write($"Não foi possível separar nome/estado na linha do auditpol: '{line}'.");
    }

    private static void LogAuditPolStateWarning(string stateText)
    {
        if (_auditPolStateWarnings >= AuditPolParseWarningLimit)
        {
            return;
        }

        _auditPolStateWarnings++;
        LogWriter.Write($"Estado de auditoria desconhecido retornado pelo auditpol: '{stateText}'.");
    }

    private static bool TrySplitSubcategoryAndState(string line, out string subcategory, out string stateText)
    {
        subcategory = string.Empty;
        stateText = string.Empty;

        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        var trimmed = line.Trim();
        foreach (var token in StatusTokenSearchOrder)
        {
            var index = trimmed.LastIndexOf(token, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                continue;
            }

            var suffix = trimmed.Substring(index);
            if (!suffix.Equals(token, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var candidate = trimmed.Substring(0, index).TrimEnd();
            if (candidate.Length == 0)
            {
                continue;
            }

            subcategory = candidate;
            stateText = suffix;
            return true;
        }

        // Fallback to whitespace-based split.
        var parts = Regex.Split(trimmed, @"\s{2,}")
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Select(part => part.Trim())
            .ToArray();

        if (parts.Length >= 2)
        {
            stateText = parts[^1];
            subcategory = string.Join(" ", parts.Take(parts.Length - 1)).Trim();
            return subcategory.Length > 0;
        }

        return false;
    }

    private static bool TryExecuteAuditPolForSubcategory(string candidate, out string output)
    {
        foreach (var format in BuildSubcategoryCommandFormats(candidate))
        {
            try
            {
                output = RunAuditPol(string.Format(CultureInfo.InvariantCulture, format, candidate));
                return true;
            }
            catch (InvalidOperationException ex)
            {
                LogWriter.Write(ex, $"auditpol.exe falhou com comando '{format}' para '{candidate}'.");
            }
        }

        output = string.Empty;
        return false;
    }

    private static IEnumerable<string> BuildSubcategoryCommandFormats(string candidate)
    {
        var isGuid = candidate.StartsWith("{", StringComparison.Ordinal) &&
                     candidate.EndsWith("}", StringComparison.Ordinal);

        if (isGuid)
        {
            yield return "/get /subcategory:{0}";
            yield return "/get /subcategory:\"{0}\"";
            yield return "/get /subcategory:{0} /r /fo csv";
        }
        else
        {
            yield return "/get /subcategory:\"{0}\"";
            yield return "/get /subcategory:\"{0}\" /r /fo csv";
        }
    }

    private static string NormalizeAuditValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var withoutDiacritics = RemoveDiacritics(value).ToLowerInvariant();
        var parts = withoutDiacritics.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

        return string.Join(" ", parts);
    }

    private static string RemoveDiacritics(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);

        foreach (var ch in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(ch);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    private static string DescribeAuditExpectation(AuditSettingState expectation) =>
        expectation switch
        {
            AuditSettingState.SuccessAndFailure => "sucesso e falha",
            AuditSettingState.SuccessOnly => "somente sucesso",
            AuditSettingState.FailureOnly => "somente falha",
            AuditSettingState.NoAuditing => "sem auditoria",
            AuditSettingState.NotConfigured => "não configurado",
            _ => "estado não identificado"
        };

    private static string DescribeBaseAuditExpectation(int value) =>
        value switch
        {
            0 => "Sem auditoria",
            1 => "Somente sucesso",
            2 => "Somente falha",
            3 => "Sucesso e falha",
            _ => "valor desconhecido"
        };

    private static string DescribeAuditSettingForCsv(AuditSettingState state) =>
        state switch
        {
            AuditSettingState.SuccessAndFailure => "\u00CAxito e Falha",
            AuditSettingState.SuccessOnly => "\u00CAxito",
            AuditSettingState.FailureOnly => "Falha",
            AuditSettingState.NoAuditing => "Sem Auditoria",
            AuditSettingState.NotConfigured => string.Empty,
            _ => string.Empty
        };

    private static string DescribeAuditSettingNumeric(AuditSettingState state) =>
        state switch
        {
            AuditSettingState.SuccessOnly => "1",
            AuditSettingState.FailureOnly => "2",
            AuditSettingState.SuccessAndFailure => "3",
            AuditSettingState.NoAuditing => "0",
            _ => string.Empty
        };

    private static bool ApplyAuditStateToRecord(AuditPolCsvRecord record, AuditSettingState expected)
    {
        var include = DescribeAuditSettingForCsv(expected);
        var numeric = DescribeAuditSettingNumeric(expected);

        var changed = false;
        if (!string.Equals(record.IncludeSetting, include, StringComparison.OrdinalIgnoreCase))
        {
            record.IncludeSetting = include;
            changed = true;
        }

        if (!string.Equals(record.ConfigurationValue, numeric, StringComparison.OrdinalIgnoreCase))
        {
            record.ConfigurationValue = numeric;
            changed = true;
        }

        if (!string.IsNullOrEmpty(record.ExcludeSetting))
        {
            record.ExcludeSetting = string.Empty;
            changed = true;
        }

        return changed;
    }

    private static bool ApplyExpectedStatesToDocument(AuditPolCsvDocument document)
    {
        var lookup = new Dictionary<string, AuditPolCsvRecord>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in document.Records)
        {
            var guid = NormalizeGuid(record.SubcategoryGuid);
            if (string.IsNullOrEmpty(guid))
            {
                continue;
            }

            lookup[guid] = record;
        }

        var changed = false;

        foreach (var setting in AdvancedAuditSettings)
        {
            var guid = NormalizeGuid(setting.SubcategoryId);
            if (string.IsNullOrEmpty(guid) || !lookup.TryGetValue(guid, out var record))
            {
                continue;
            }

            changed |= ApplyAuditStateToRecord(record, setting.ExpectedState);
        }

        return changed;
    }

    private static async Task ApplySecurityTemplateAsync(string templatePath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(templatePath))
        {
            throw new ArgumentException("Caminho do template invalido.", nameof(templatePath));
        }

        var tempDbPath = CreateScopedTemporaryPath($"config-auditoria-secedit-{Guid.NewGuid():N}.sdb");
        var tempLogPath = CreateScopedTemporaryPath($"config-auditoria-secedit-{Guid.NewGuid():N}.log");

        try
        {
            await RunProcessAsync(
                "secedit.exe",
                $"/configure /db \"{tempDbPath}\" /cfg \"{templatePath}\" /areas SECURITYPOLICY /log \"{tempLogPath}\" /quiet",
                cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex) when (File.Exists(tempLogPath))
        {
            var logTail = TryReadTail(tempLogPath);
            if (!string.IsNullOrWhiteSpace(logTail))
            {
                throw new InvalidOperationException($"{ex.Message} Detalhes do secedit: {logTail}", ex);
            }

            throw;
        }
        finally
        {
            TryDeleteFile(tempDbPath);
            TryDeleteFile(tempLogPath);
        }
    }

    private static string TryReadTail(string path, int maxLines = 10)
    {
        try
        {
            if (!File.Exists(path))
            {
                return string.Empty;
            }

            var queue = new Queue<string>(maxLines);

            foreach (var line in File.ReadLines(path))
            {
                if (queue.Count == maxLines)
                {
                    queue.Dequeue();
                }

                queue.Enqueue(line);
            }

            return string.Join(" | ", queue.Select(line => line.Trim()).Where(line => line.Length > 0));
        }
        catch
        {
            return string.Empty;
        }
    }

    private static void TryDeleteFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch
        {
            // ignore best-effort cleanup failures
        }
    }

    private static string CreateScopedTemporaryPath(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) ||
            fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            fileName.Contains(Path.DirectorySeparatorChar) ||
            fileName.Contains(Path.AltDirectorySeparatorChar))
        {
            throw new ArgumentException("Nome de arquivo temporário inválido.", nameof(fileName));
        }

        Directory.CreateDirectory(ScopedTemporaryDirectory);
        return Path.Combine(ScopedTemporaryDirectory, fileName);
    }

    private static void ConfigureScreenSaverKey(RegistryKey? key)
    {
        using (key)
        {
            if (key is null)
            {
                throw new InvalidOperationException("Não foi possível acessar as configurações de proteção de tela.");
            }

            key.SetValue("SCRNSAVE.EXE", string.Empty, RegistryValueKind.String);
            key.SetValue("ScreenSaveActive", "1", RegistryValueKind.String);
            key.SetValue("ScreenSaverIsSecure", "1", RegistryValueKind.String);
            key.SetValue("ScreenSaveTimeout", "120", RegistryValueKind.String);
        }
    }

    private static void SetDwordValue(RegistryKey? key, string name, int value)
    {
        using (key)
        {
            if (key is null)
            {
                throw new InvalidOperationException("Não foi possível acessar a chave de configuração do Windows.");
            }

            key.SetValue(name, value, RegistryValueKind.DWord);
        }
    }

    private static void EnsureApprovedSystemExecutable(string fileName)
    {
        var executable = Path.GetFileName(fileName);
        if (!string.Equals(executable, fileName, StringComparison.Ordinal) ||
            !ApprovedSystemExecutables.Contains(executable))
        {
            throw new InvalidOperationException("A execução do utilitário solicitado não é permitida por esta edição do aplicativo.");
        }
    }

    private static void OpenWindowsSettings(string uri)
    {
        if (uri != "windowsdefender:")
        {
            throw new InvalidOperationException("A página solicitada do Windows não é permitida.");
        }

        Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
    }

    private static async Task RunProcessAsync(string fileName, string arguments, CancellationToken cancellationToken)
    {
        EnsureApprovedSystemExecutable(fileName);
        LogWriter.Write($"Executando utilitário permitido: {fileName}.");
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi) ?? throw new InvalidOperationException($"Não foi possível iniciar {fileName}.");

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            var error = await process.StandardError.ReadToEndAsync().ConfigureAwait(false);
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(error)
                    ? $"{fileName} retornou codigo {process.ExitCode}."
                    : $"{fileName} retornou codigo {process.ExitCode}: {error.Trim()}");
        }
    }

    private static void RunProcess(string fileName, string arguments)
    {
        EnsureApprovedSystemExecutable(fileName);
        LogWriter.Write($"Executando utilitário permitido: {fileName}.");
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi) ?? throw new InvalidOperationException($"Não foi possível iniciar {fileName}.");

        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            var error = process.StandardError.ReadToEnd();
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(error)
                    ? $"{fileName} retornou codigo {process.ExitCode}."
                    : $"{fileName} retornou codigo {process.ExitCode}: {error.Trim()}");
        }
    }

    private static void ShowInformation(string message)
    {
        var ui = UiService;
        if (ui is null)
        {
            return;
        }

        _ = ui.ShowInfoAsync(message);
    }

    private static void ShowError(string message)
    {
        var ui = UiService;
        if (ui is null)
        {
            return;
        }

        _ = ui.ShowErrorAsync(message);
    }

    private static void ReleaseComObject(object? value)
    {
        if (value is null)
        {
            return;
        }

        if (Marshal.IsComObject(value))
        {
            Marshal.FinalReleaseComObject(value);
        }
    }

    private sealed record WindowsUpdateAssessment(
        ConfigurationAssessmentResult Result,
        bool RequiresRestart);

    private sealed record WindowsUpdatePendingInfo(
        IReadOnlyList<string> Titles,
        int OptionalUpdates)
    {
        public int TotalUpdates => Titles.Count;
    }

    private sealed record AntivirusAssessment(
        ConfigurationAssessmentResult Result,
        bool HasProducts,
        bool HasHealthyProduct,
        bool NeedsUpdate);

    private sealed record AntivirusProductInfo(string Name, bool IsEnabled, bool IsUpToDate);

    private sealed class FirewallProfileStatus
    {
        public string? Name { get; init; }
        public int Enabled { get; init; }
        public int AllowInboundRules { get; init; }
        public int NotifyOnListen { get; init; }
        public long? LogMaxSizeKilobytes { get; init; }
        public int LogAllowed { get; init; }
        public int LogBlocked { get; init; }

        public bool IsEnabled => Enabled != 0;
        public bool AreInboundRulesAllowed => AllowInboundRules >= 2;
        public bool AreNotificationsEnabled => NotifyOnListen != 0;
        public bool AreAllowedConnectionsLogged => LogAllowed != 0;
        public bool AreBlockedConnectionsLogged => LogBlocked != 0;
        public bool IsLogSizeCompliant => LogMaxSizeKilobytes == FirewallLogSizeKilobytes;
    }

    private sealed record BuiltInAccountSidInfo(WellKnownSidType Type, string Sid);
}
