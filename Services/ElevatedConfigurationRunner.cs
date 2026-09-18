using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using GerenciadorIcpBrasil.Modules.ConfigAuditoria.Models;
using GerenciadorIcpBrasil.Security;

namespace GerenciadorIcpBrasil.Services;

public sealed class ElevatedConfigurationRunner
{
    public static bool RequiresElevation(string categoryKey) =>
        SecurityPolicy.RequiresElevation(categoryKey);

    public async Task<ElevatedConfigurationResult> ApplyAsync(
        string categoryKey,
        CancellationToken cancellationToken = default)
    {
        if (!SecurityPolicy.RequiresElevation(categoryKey))
        {
            throw new InvalidOperationException("A configuração não faz parte do escopo administrativo permitido.");
        }

        var requestId = Guid.NewGuid().ToString("N");
        var resultPath = PrepareResultPath(requestId);

        try
        {
            var exitCode = await RunHelperAsync(
                $"--apply-category {categoryKey} --result-id {requestId}",
                cancellationToken).ConfigureAwait(false);

            if (exitCode != 0)
            {
                return ElevatedConfigurationResult.Failure($"O executor administrativo terminou com o código {exitCode}.");
            }

            var payload = ReadPayload(resultPath);
            var assessment = payload.Results
                .Where(result => string.Equals(result.CategoryKey, categoryKey, StringComparison.OrdinalIgnoreCase))
                .Select(ToAssessment)
                .FirstOrDefault();

            return ElevatedConfigurationResult.Success(assessment);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return ElevatedConfigurationResult.Cancelled();
        }
        finally
        {
            TryDelete(resultPath);
        }
    }

    public async Task<ElevatedAssessmentBatchResult> EvaluateSecurityPoliciesAsync(
        CancellationToken cancellationToken = default)
    {
        var requestId = Guid.NewGuid().ToString("N");
        var resultPath = PrepareResultPath(requestId);

        try
        {
            var exitCode = await RunHelperAsync(
                $"--evaluate-security-policies --result-id {requestId}",
                cancellationToken).ConfigureAwait(false);

            if (exitCode != 0)
            {
                return ElevatedAssessmentBatchResult.Failure(
                    $"O executor administrativo terminou com o código {exitCode}.");
            }

            var assessments = ReadPayload(resultPath).Results.ToDictionary(
                result => result.CategoryKey,
                ToAssessment,
                StringComparer.OrdinalIgnoreCase);

            return ElevatedAssessmentBatchResult.Success(assessments);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return ElevatedAssessmentBatchResult.Cancelled();
        }
        catch (Exception ex)
        {
            return ElevatedAssessmentBatchResult.Failure(ex.Message);
        }
        finally
        {
            TryDelete(resultPath);
        }
    }

    private static async Task<int> RunHelperAsync(string arguments, CancellationToken cancellationToken)
    {
        var helperPath = Path.Combine(
            AppContext.BaseDirectory,
            "Helpers",
            "ElevatedConfiguration",
            "ConfigAuditoria.exe");

        if (!File.Exists(helperPath))
        {
            throw new FileNotFoundException("O executor administrativo não foi encontrado no pacote.", helperPath);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = helperPath,
            Arguments = arguments,
            UseShellExecute = true,
            Verb = "runas",
            WorkingDirectory = Path.GetDirectoryName(helperPath)!
        };

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Não foi possível iniciar o executor administrativo.");

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return process.ExitCode;
    }

    private static string PrepareResultPath(string requestId)
    {
        var resultPath = SecurityPolicy.GetElevatedResultPath(requestId);
        Directory.CreateDirectory(Path.GetDirectoryName(resultPath)!);
        TryDelete(resultPath);
        return resultPath;
    }

    private static ElevatedAssessmentPayload ReadPayload(string resultPath)
    {
        if (!File.Exists(resultPath))
        {
            throw new InvalidOperationException("O executor administrativo não retornou o resultado da verificação.");
        }

        var payload = JsonSerializer.Deserialize<ElevatedAssessmentPayload>(
            File.ReadAllText(resultPath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        return payload is { Results.Count: > 0 }
            ? payload
            : throw new InvalidOperationException("O resultado da verificação administrativa é inválido.");
    }

    private static ConfigurationAssessmentResult ToAssessment(ElevatedAssessmentItem item)
    {
        if (!Enum.TryParse<ConfigurationStatus>(item.Status, true, out var status))
        {
            status = ConfigurationStatus.Unknown;
        }

        return new ConfigurationAssessmentResult(status, item.Message);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // O arquivo também é eliminado na próxima solicitação com o mesmo caminho.
        }
    }
}

public sealed record ElevatedConfigurationResult(
    bool Succeeded,
    bool WasCancelled,
    string? Error,
    ConfigurationAssessmentResult? Assessment)
{
    public static ElevatedConfigurationResult Success(ConfigurationAssessmentResult? assessment) =>
        new(true, false, null, assessment);

    public static ElevatedConfigurationResult Cancelled() =>
        new(false, true, "A elevação foi cancelada pelo usuário.", null);

    public static ElevatedConfigurationResult Failure(string error) => new(false, false, error, null);
}

public sealed record ElevatedAssessmentBatchResult(
    bool Succeeded,
    bool WasCancelled,
    string? Error,
    IReadOnlyDictionary<string, ConfigurationAssessmentResult> Assessments)
{
    public static ElevatedAssessmentBatchResult Success(
        IReadOnlyDictionary<string, ConfigurationAssessmentResult> assessments) =>
        new(true, false, null, assessments);

    public static ElevatedAssessmentBatchResult Cancelled() =>
        new(false, true, "A elevação foi cancelada pelo usuário.", EmptyAssessments);

    public static ElevatedAssessmentBatchResult Failure(string error) =>
        new(false, false, error, EmptyAssessments);

    private static IReadOnlyDictionary<string, ConfigurationAssessmentResult> EmptyAssessments { get; } =
        new Dictionary<string, ConfigurationAssessmentResult>();
}

internal sealed class ElevatedAssessmentPayload
{
    public List<ElevatedAssessmentItem> Results { get; set; } = new();
}

internal sealed class ElevatedAssessmentItem
{
    public string CategoryKey { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string? Message { get; set; }
}
