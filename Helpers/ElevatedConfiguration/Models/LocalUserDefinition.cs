namespace ConfigAuditoria.Models;

public sealed record LocalUserDefinition(
    string UserName,
    string? FullName,
    string? Description,
    string Password,
    bool MustChangePassword,
    bool UserCannotChangePassword,
    bool PasswordNeverExpires,
    bool IsDisabled);
