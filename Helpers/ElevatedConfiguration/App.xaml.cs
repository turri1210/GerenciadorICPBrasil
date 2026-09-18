using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using ConfigAuditoria.Logging;
using ConfigAuditoria.Models;
using ConfigAuditoria.Services;
using GerenciadorIcpBrasil.Security;

namespace ConfigAuditoria;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        if (e.Args.Length > 0)
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var exitCode = await RunOneShotAsync(e.Args);
            Shutdown(exitCode);
            return;
        }

        MainWindow = new MainWindow();
        MainWindow.Show();
    }

    private static async Task<int> RunOneShotAsync(string[] args)
    {
        var isApplyRequest = args.Length == 4 &&
            string.Equals(args[0], "--apply-category", StringComparison.Ordinal) &&
            string.Equals(args[2], "--result-id", StringComparison.Ordinal);
        var isAssessmentRequest = args.Length == 3 &&
            string.Equals(args[0], "--evaluate-security-policies", StringComparison.Ordinal) &&
            string.Equals(args[1], "--result-id", StringComparison.Ordinal);

        if (!isApplyRequest && !isAssessmentRequest)
        {
            LogWriter.Write("Solicitação elevada rejeitada: contrato de argumentos inválido.");
            MessageBox.Show(
                "A solicitação de configuração não é válida.",
                "Assistente ICP",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return 2;
        }

        try
        {
            var service = new ConfigurationAuditService();

            if (isAssessmentRequest)
            {
                var results = new List<ElevatedAssessmentItem>();
                foreach (var categoryKey in SecurityPolicy.PrivilegedAssessmentCategories)
                {
                    var assessment = await service.EvaluateCategoryAsync(categoryKey);
                    results.Add(ToPayload(categoryKey, assessment));
                }

                WriteResult(args[2], results);
                return 0;
            }

            var requestedCategory = args[1];
            var requestId = args[3];
            await service.ApplyFixAsync(requestedCategory);
            var result = await service.EvaluateCategoryAsync(requestedCategory);
            WriteResult(requestId, new[] { ToPayload(requestedCategory, result) });
            return 0;
        }
        catch (Exception ex)
        {
            var operation = isApplyRequest ? args[1] : "verificação de políticas de segurança";
            LogWriter.Write(ex, $"Falha na operação elevada '{operation}'.");
            MessageBox.Show(
                $"Não foi possível concluir a operação administrativa. Detalhes: {ex.Message}",
                "Assistente ICP",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return 1;
        }
    }

    private static ElevatedAssessmentItem ToPayload(
        string categoryKey,
        ConfigurationAssessmentResult assessment) =>
        new(categoryKey, assessment.Status.ToString(), assessment.Message);

    private static void WriteResult(string requestId, IEnumerable<ElevatedAssessmentItem> results)
    {
        var resultPath = SecurityPolicy.GetElevatedResultPath(requestId);
        Directory.CreateDirectory(Path.GetDirectoryName(resultPath)!);

        var payload = JsonSerializer.Serialize(
            new ElevatedAssessmentPayload(results.ToArray()),
            new JsonSerializerOptions { WriteIndented = false });

        using var stream = new FileStream(
            resultPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.Write(payload);
    }

    private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        LogWriter.Write(e.Exception, "DispatcherUnhandledException");
        MessageBox.Show(
            $"Ocorreu um erro inesperado: {e.Exception.Message}",
            "ConfigAuditoria",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }

    private void OnDomainUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            LogWriter.Write(ex, "DomainUnhandledException");
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        LogWriter.Write(e.Exception, "UnobservedTaskException");
        e.SetObserved();
    }
}

internal sealed record ElevatedAssessmentPayload(IReadOnlyList<ElevatedAssessmentItem> Results);

internal sealed record ElevatedAssessmentItem(string CategoryKey, string Status, string? Message);
