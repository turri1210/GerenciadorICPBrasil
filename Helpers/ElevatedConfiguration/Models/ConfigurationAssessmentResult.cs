namespace ConfigAuditoria.Models;

public sealed class ConfigurationAssessmentResult
{
    public ConfigurationAssessmentResult(ConfigurationStatus status, string? message = null)
    {
        Status = status;
        Message = message;
    }

    public ConfigurationStatus Status { get; }

    public string? Message { get; }
}
