namespace ConfigAuditoria.Models;

public sealed record UserAccountsPlan(IReadOnlyList<LocalUserAccountPlanItem> Accounts);
