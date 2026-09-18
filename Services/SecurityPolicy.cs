using System.IO;

namespace GerenciadorIcpBrasil.Security;

public static class SecurityPolicy
{
    private static readonly string[] PrivilegedAssessmentCategoryValues =
    {
        "senha-forte",
        "bloqueio-conta",
        "auditoria-estacoes",
        "firewall"
    };

    private static readonly HashSet<string> PrivilegedAssessmentCategorySet =
        new(PrivilegedAssessmentCategoryValues, StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> ElevatedConfigurationCategories = new(StringComparer.OrdinalIgnoreCase)
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

    private static readonly string[] ExcludedWindowsUpdateTitleMarkers =
    {
        "Preview",
        "Pré-visualização",
        "Versão prévia",
        "Insider",
        "Feature update to Windows",
        "Atualização de recursos para Windows"
    };

    public static bool RequiresElevation(string? categoryKey) =>
        !string.IsNullOrWhiteSpace(categoryKey) &&
        ElevatedConfigurationCategories.Contains(categoryKey);

    public static IReadOnlyList<string> PrivilegedAssessmentCategories =>
        PrivilegedAssessmentCategoryValues;

    public static bool RequiresElevatedAssessment(string? categoryKey) =>
        !string.IsNullOrWhiteSpace(categoryKey) &&
        PrivilegedAssessmentCategorySet.Contains(categoryKey);

    public static string GetElevatedResultPath(string requestId)
    {
        if (!Guid.TryParseExact(requestId, "N", out _))
        {
            throw new ArgumentException("O identificador do resultado elevado não é válido.", nameof(requestId));
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Gerenciador ICP Brasil",
            "elevated-results",
            $"{requestId}.json");
    }

    public static bool ShouldAutomaticallyInstallWindowsUpdate(
        bool isHidden,
        bool isBeta,
        bool isAutomaticallySelected,
        string? title)
    {
        if (isHidden || isBeta || !isAutomaticallySelected || string.IsNullOrWhiteSpace(title))
        {
            return false;
        }

        return !ExcludedWindowsUpdateTitleMarkers.Any(marker =>
            title.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }
}
