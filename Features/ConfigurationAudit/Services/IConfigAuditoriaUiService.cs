using GerenciadorIcpBrasil.Modules.ConfigAuditoria.Models;

namespace GerenciadorIcpBrasil.Modules.ConfigAuditoria.Services;

public interface IConfigAuditoriaUiService
{
    Task<bool> ConfirmAsync(string message);
    Task<LocalUserDefinition?> PromptNewUserAsync(bool isAdmin);
    Task<UserAccountsPlan?> PromptUserAccountsPlanAsync(IReadOnlyList<LocalUserAccountPlanItem> existingAccounts);
    Task ShowInfoAsync(string message);
    Task ShowErrorAsync(string message);
    Task SaveEvidenceAsync(Func<string, string> reportBuilder);
}
