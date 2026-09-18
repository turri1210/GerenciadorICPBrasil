using System.Collections.Generic;
using System.Threading;
using ConfigAuditoria.Models;

namespace ConfigAuditoria.Services;

public interface IConfigurationAuditService
{
    Task<IReadOnlyDictionary<string, ConfigurationAssessmentResult>> EvaluateAsync(
        IProgress<(string Key, ConfigurationAssessmentResult Result)>? progress = null,
        IProgress<string>? stageProgress = null,
        CancellationToken cancellationToken = default);

    Task<ConfigurationAssessmentResult> EvaluateCategoryAsync(string categoryKey, CancellationToken cancellationToken = default);

    Task ApplyFixAsync(string categoryKey, CancellationToken cancellationToken = default);
}
