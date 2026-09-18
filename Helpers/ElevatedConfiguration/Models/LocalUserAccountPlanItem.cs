namespace ConfigAuditoria.Models;

public sealed record LocalUserAccountPlanItem(
    string UserName,
    string PasswordMask,
    LocalUserProfile Profile,
    bool IsExistingAccount,
    LocalUserDefinition? NewUserDefinition);
