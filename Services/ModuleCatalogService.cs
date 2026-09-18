using GerenciadorIcpBrasil.Models;

namespace GerenciadorIcpBrasil.Services;

/// <summary>
/// Catálogo das funcionalidades que fazem parte do Assistente ICP.
/// Não baixa, instala ou atualiza componentes separadamente.
/// </summary>
public sealed class ModuleCatalogService
{
    public const string LeitorCertificadoModuleId = "leitor-certificado";

    public ModuleCatalogService(string baseDirectory, string appDataRoot, AuditService? auditService = null)
    {
        _ = baseDirectory;
        _ = appDataRoot;
        _ = auditService;
    }

    public Task<IReadOnlyList<ModuleItem>> LoadMergedModulesAsync() =>
        Task.FromResult<IReadOnlyList<ModuleItem>>(CreateIntegratedFeatures());

    private static List<ModuleItem> CreateIntegratedFeatures()
    {
        var version = typeof(App).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        return new List<ModuleItem>
        {
            Create("leitor-certificado", "Cliente Rede ICP Brasil", "Integração local para certificados digitais A3.", version),
            Create("links-uteis", "Links úteis", "Catálogo de links importantes.", version),
            Create("leitora-biometrica", "Teste da leitora biométrica", "Teste integrado da leitora biométrica Futronic FS88H.", version),
            Create("installer-launcher", "Programas essenciais", "Instala e verifica programas complementares autorizados pelo usuário.", version),
            Create("config-auditoria", "Configuração da máquina", "Configuração e auditoria de ambientes e acessos.", version),
            Create("biometric-license-status", "Licença biométrica", "Informa o status da licença biométrica e permite backup e restauração local.", version),
        };
    }

    private static ModuleItem Create(string id, string name, string description, string version) => new()
    {
        Id = id,
        Name = name,
        Description = description,
        Version = version,
        IsInstalled = true,
        InstalledVersion = version,
    };
}
