using GerenciadorIcpBrasil.Security;

var failures = new List<string>();

static void Check(List<string> failures, bool condition, string description)
{
    if (!condition)
    {
        failures.Add(description);
    }
}

var elevatedCategories = new[]
{
    "login-remoto",
    "windows-update",
    "senha-forte",
    "bloqueio-conta",
    "auditoria-estacoes",
    "contas-usuarios",
    "visualizador-eventos",
    "firewall",
    "sincronismo-hora",
    "integridade",
    "criptografia"
};

foreach (var category in elevatedCategories)
{
    Check(failures, SecurityPolicy.RequiresElevation(category), $"Categoria elevada ausente: {category}");
}

foreach (var category in new[] { "senha-forte", "bloqueio-conta", "auditoria-estacoes", "firewall" })
{
    Check(failures, SecurityPolicy.RequiresElevatedAssessment(category), $"Verificação elevada ausente: {category}");
}

foreach (var category in new[] { "", "windows-update", "desconhecida", "../senha-forte" })
{
    Check(failures, !SecurityPolicy.RequiresElevatedAssessment(category), $"Verificação elevada inválida aceita: {category}");
}

foreach (var category in new[] { "", " ", "antivirus", "protecao-tela", "desconhecida", "../windows-update", "windows-update.exe" })
{
    Check(failures, !SecurityPolicy.RequiresElevation(category), $"Categoria inválida aceita: {category}");
}

Check(failures, SecurityPolicy.ShouldAutomaticallyInstallWindowsUpdate(false, false, true, "Atualização cumulativa do Windows 11"), "Atualização recomendada foi rejeitada.");
Check(failures, !SecurityPolicy.ShouldAutomaticallyInstallWindowsUpdate(true, false, true, "Atualização oculta"), "Atualização oculta foi aceita.");
Check(failures, !SecurityPolicy.ShouldAutomaticallyInstallWindowsUpdate(false, true, true, "Atualização beta"), "Atualização beta foi aceita.");
Check(failures, !SecurityPolicy.ShouldAutomaticallyInstallWindowsUpdate(false, false, false, "Atualização opcional"), "Atualização opcional foi aceita.");
Check(failures, !SecurityPolicy.ShouldAutomaticallyInstallWindowsUpdate(false, false, true, "2026-09 Cumulative Update Preview"), "Atualização Preview foi aceita.");
Check(failures, !SecurityPolicy.ShouldAutomaticallyInstallWindowsUpdate(false, false, true, "Pré-visualização de atualização cumulativa"), "Pré-visualização foi aceita.");
Check(failures, !SecurityPolicy.ShouldAutomaticallyInstallWindowsUpdate(false, false, true, "Feature update to Windows 11, version 26H2"), "Atualização de recurso foi aceita.");
Check(failures, !SecurityPolicy.ShouldAutomaticallyInstallWindowsUpdate(false, false, true, null), "Atualização sem título foi aceita.");

var root = args.Length == 1
    ? Path.GetFullPath(args[0])
    : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

var mainManifest = File.ReadAllText(Path.Combine(root, "app.manifest"));
var helperManifest = File.ReadAllText(Path.Combine(root, "Helpers", "ElevatedConfiguration", "app.manifest"));
var helperService = File.ReadAllText(Path.Combine(root, "Helpers", "ElevatedConfiguration", "Services", "ConfigurationAuditService.cs"));
var helperApp = File.ReadAllText(Path.Combine(root, "Helpers", "ElevatedConfiguration", "App.xaml.cs"));
var elevatedRunner = File.ReadAllText(Path.Combine(root, "Services", "ElevatedConfigurationRunner.cs"));
var backupService = File.ReadAllText(Path.Combine(root, "Features", "InstallerLauncher", "Services", "BiometricLicenseBackupService.cs"));
var auditService = File.ReadAllText(Path.Combine(root, "Services", "AuditService.cs"));
var updateService = File.ReadAllText(Path.Combine(root, "Services", "AppUpdateService.cs"));
var moduleCatalogService = File.ReadAllText(Path.Combine(root, "Services", "ModuleCatalogService.cs"));
var certificateBridgeService = File.ReadAllText(Path.Combine(root, "Services", "CertificateBridgeService.cs"));
var releaseScript = File.ReadAllText(Path.Combine(root, "scripts", "build-release.ps1"));
var releaseVerifier = File.ReadAllText(Path.Combine(root, "scripts", "verify-release.ps1"));
var mainProject = File.ReadAllText(Path.Combine(root, "GerenciadorIcpBrasil.csproj"));
var installerScript = File.ReadAllText(Path.Combine(root, "installer", "GerenciadorICPBrasil.iss"));
var signPathApplication = File.ReadAllText(Path.Combine(root, ".signpath", "artifact-configurations", "application-v1.xml"));
var signPathInstaller = File.ReadAllText(Path.Combine(root, ".signpath", "artifact-configurations", "installer-v1.xml"));
var launchSettings = File.ReadAllText(Path.Combine(root, "Properties", "launchSettings.json"));
var landingPage = File.ReadAllText(Path.Combine(root, "landing-yez", "index.html"));
var ciWorkflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "ci.yml"));
var releaseWorkflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "release.yml"));
var installerManager = File.ReadAllText(Path.Combine(root, "Features", "InstallerLauncher", "Services", "InstallerManager.cs"));
var installerCatalog = File.ReadAllText(Path.Combine(root, "Features", "InstallerLauncher", "Services", "InstallerCatalogService.cs"));

Check(failures, mainManifest.Contains("requestedExecutionLevel level=\"asInvoker\"", StringComparison.Ordinal), "O app principal não está marcado como asInvoker.");
Check(failures, helperManifest.Contains("requestedExecutionLevel level=\"requireAdministrator\"", StringComparison.Ordinal), "O helper não está marcado como requireAdministrator.");
Check(failures, helperService.Contains("CreateUpdateDownloader", StringComparison.Ordinal), "O helper não contém download pela WUA.");
Check(failures, helperService.Contains("CreateUpdateInstaller", StringComparison.Ordinal), "O helper não contém instalação pela WUA.");
Check(failures, !helperService.Contains("powershell.exe", StringComparison.OrdinalIgnoreCase), "O helper contém execução de PowerShell.");
Check(failures, !helperService.Contains("UsoClient", StringComparison.OrdinalIgnoreCase), "O helper contém UsoClient.");
Check(failures, !helperService.Contains("sl Application /e:true", StringComparison.OrdinalIgnoreCase), "O helper ainda tenta habilitar indevidamente o log clássico Application.");
Check(failures, helperService.Contains("start W32Time", StringComparison.Ordinal), "O helper não inicia o serviço W32Time antes da configuração.");
Check(failures, helperService.Contains("/resync /rediscover", StringComparison.Ordinal), "O helper não solicita a redescoberta da fonte NTP.");
Check(failures, !helperService.Contains("ConfigureScreenSaverKey(Registry.CurrentUser.CreateSubKey(@\"Software\\Policies", StringComparison.Ordinal), "O helper ainda tenta gravar a proteção de tela em HKCU\\Software\\Policies.");
Check(failures, helperService.Contains("!ReferenceEquals(owner, dialog)", StringComparison.Ordinal), "O diálogo de contas não está protegido contra Owner autorreferente.");
Check(failures, helperService.Contains("return string.Join(", StringComparison.Ordinal) && helperService.Contains("$\"Contas ativas: {activeCount};", StringComparison.Ordinal), "O resumo de contas do helper não separa as informações por linha.");
Check(failures, helperApp.Contains("--evaluate-security-policies", StringComparison.Ordinal), "O helper não aceita a verificação administrativa restrita.");
Check(failures, helperApp.Contains("FileMode.CreateNew", StringComparison.Ordinal), "O helper não protege o arquivo de resposta contra sobrescrita.");
Check(failures, elevatedRunner.Contains("EvaluateSecurityPoliciesAsync", StringComparison.Ordinal), "O app principal não solicita a verificação administrativa.");
Check(failures, backupService.Contains("ProtectedData.Protect", StringComparison.Ordinal), "O backup biométrico local não utiliza a proteção do Windows.");
Check(failures, !backupService.Contains("HttpClient", StringComparison.Ordinal), "O backup biométrico ainda contém envio pela rede.");
Check(failures, !auditService.Contains("HttpClient", StringComparison.Ordinal), "A auditoria ainda contém envio pela rede.");
Check(failures, updateService.Contains("AuthenticodeVerifier.VerifyOfficialRelease", StringComparison.Ordinal), "O atualizador não valida a assinatura do instalador.");
Check(failures, updateService.Contains("ValidateHashAsync", StringComparison.Ordinal), "O atualizador não valida o SHA-256 do instalador.");
Check(failures, updateService.Contains("ValidateDownloadUri", StringComparison.Ordinal), "O atualizador não restringe o endereço do instalador.");
Check(failures, File.ReadAllText(Path.Combine(root, "Services", "AuthenticodeVerifier.cs")).Contains("Gerenciador ICP Brasil", StringComparison.Ordinal), "A assinatura da atualização não é vinculada ao produto.");
Check(failures, !File.Exists(Path.Combine(root, "modules.json")), "O catálogo remoto de módulos ainda existe.");
Check(failures, !moduleCatalogService.Contains("HttpClient", StringComparison.Ordinal), "O catálogo de funcionalidades ainda acessa atualizações remotas.");
Check(failures, !moduleCatalogService.Contains("packageUrl", StringComparison.OrdinalIgnoreCase), "O catálogo ainda contém pacotes independentes.");
Check(failures, certificateBridgeService.Contains("http://127.0.0.1", StringComparison.Ordinal), "A ponte de certificados não está limitada ao endereço local.");
Check(failures, certificateBridgeService.Contains("IsAllowedOrigin", StringComparison.Ordinal), "A ponte de certificados não valida a origem do navegador.");
Check(failures, certificateBridgeService.Contains("if (string.IsNullOrWhiteSpace(origin))", StringComparison.Ordinal) && certificateBridgeService.Contains("return false;", StringComparison.Ordinal), "A ponte de certificados ainda aceita requisições sem origem.");
Check(failures, certificateBridgeService.Contains("TryNormalizeChallenge", StringComparison.Ordinal), "A assinatura local não exige um desafio criptográfico.");
Check(failures, !File.ReadAllText(Path.Combine(root, "Helpers", "CertificateSelector", "Program.cs")).Contains("autenticacao-certificado", StringComparison.Ordinal), "O seletor ainda assina um texto fixo reutilizável.");
Check(failures, releaseScript.Contains("Helpers\\ElevatedConfiguration", StringComparison.Ordinal), "O build unificado não publica o executor administrativo.");
Check(failures, releaseScript.Contains("Helpers\\CertificateSelector", StringComparison.Ordinal), "O build unificado não publica o seletor de certificados.");
Check(failures, mainProject.Contains("CopyWinUiResourcesToPublish", StringComparison.Ordinal), "O projeto não publica os recursos WinUI compilados.");
Check(failures, releaseVerifier.Contains("GerenciadorIcpBrasil.pri", StringComparison.Ordinal), "A validação não exige o índice de recursos WinUI.");
Check(failures, releaseVerifier.Contains("Views\\MainPage.xbf", StringComparison.Ordinal), "A validação não exige o XAML compilado da tela principal.");
Check(failures, installerScript.Contains("AppPublisher=Rede ICP Brasil", StringComparison.Ordinal), "O instalador não informa o editor Rede ICP Brasil.");
Check(failures, !signPathApplication.Contains("Assistente ICP", StringComparison.OrdinalIgnoreCase), "A configuração SignPath do aplicativo ainda usa o nome antigo.");
Check(failures, !signPathInstaller.Contains("Assistente ICP", StringComparison.OrdinalIgnoreCase), "A configuração SignPath do instalador ainda usa o nome antigo.");
Check(failures, !launchSettings.Contains("MsixPackage", StringComparison.OrdinalIgnoreCase), "O perfil de execução ainda oferece empacotamento MSIX.");
Check(failures, !landingPage.Contains("Microsoft Store", StringComparison.OrdinalIgnoreCase), "A página pública ainda menciona a Microsoft Store.");
Check(failures, installerScript.Contains("Get-AuthenticodeSignature", StringComparison.Ordinal), "O instalador não valida a assinatura dos pré-requisitos baixados.");
Check(failures, installerScript.Contains("Result := EnsurePrerequisitesInstalled(NeedsRestart);", StringComparison.Ordinal), "O instalador não executa a verificação dos pré-requisitos.");
Check(failures, installerScript.Contains("EnsureProtectedWorkDirectory", StringComparison.Ordinal) && !installerScript.Contains("{tmp}\\gerenciador_prereq", StringComparison.OrdinalIgnoreCase), "Os scripts privilegiados do instalador ainda usam uma pasta temporária gravável pelo usuário.");
Check(failures, installerScript.Contains("[System.IO.FileShare]::Read", StringComparison.Ordinal), "O instalador não bloqueia a troca dos pré-requisitos após validar a assinatura.");
Check(failures, installerScript.Contains("GetDateTimeString('yyyymmddhhnnsszzz', '-', ':')", StringComparison.Ordinal), "A criação da pasta protegida usa separadores incompatíveis com o tipo Char do Inno Setup.");
Check(failures, installerManager.Contains("AuthenticodeVerifier.VerifyTrustedSignature", StringComparison.Ordinal), "O gerenciador executa instaladores sem validar a assinatura Authenticode.");
Check(failures, installerManager.Contains("FileShare.Read", StringComparison.Ordinal), "O gerenciador não bloqueia a troca do instalador entre validação e execução.");
Check(failures, installerCatalog.Contains("ExpectedPublisherNames", StringComparison.Ordinal), "O catálogo não vincula os instaladores aos editores esperados.");
Check(failures, !installerCatalog.Contains("AutomaticInstallerPath: \"winget.exe\"", StringComparison.Ordinal), "O Java ainda executa um winget não qualificado.");
Check(failures, updateService.Contains("using var executionLock", StringComparison.Ordinal), "O atualizador não bloqueia a troca do arquivo após a validação.");
Check(failures, !ciWorkflow.Contains("uses: actions/checkout@v", StringComparison.Ordinal) && !ciWorkflow.Contains("uses: actions/setup-dotnet@v", StringComparison.Ordinal), "O CI usa ações referenciadas por tags mutáveis.");
Check(failures, !releaseWorkflow.Contains("uses: actions/checkout@v", StringComparison.Ordinal) && !releaseWorkflow.Contains("uses: actions/setup-dotnet@v", StringComparison.Ordinal) && !releaseWorkflow.Contains("uses: actions/upload-artifact@v", StringComparison.Ordinal) && !releaseWorkflow.Contains("uses: signpath/github-action-submit-signing-request@v", StringComparison.Ordinal), "A release usa ações referenciadas por tags mutáveis.");

if (failures.Count > 0)
{
    Console.Error.WriteLine("Falha nas verificações de segurança:");
    foreach (var failure in failures)
    {
        Console.Error.WriteLine($"- {failure}");
    }

    return 1;
}

Console.WriteLine("Todas as verificações de segurança foram aprovadas.");
return 0;
