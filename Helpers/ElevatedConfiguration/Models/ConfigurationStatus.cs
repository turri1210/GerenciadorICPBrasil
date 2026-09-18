namespace ConfigAuditoria.Models;

/// <summary>
/// Represents the evaluation result for a configuration category.
/// </summary>
public enum ConfigurationStatus
{
    Unknown = 0,
    Compliant = 1,
    NonCompliant = 2,
    Pending = 3
}
