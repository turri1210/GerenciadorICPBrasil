using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GerenciadorIcpBrasil.Services;

/// <summary>
/// Expõe a integração local usada pelos portais ICP-Brasil sem depender do antigo aplicativo Electron.
/// O serviço aceita conexões somente em 127.0.0.1 e valida a origem dos navegadores.
/// </summary>
public sealed class CertificateBridgeService : IAsyncDisposable
{
    public const int Port = 8357;

    private static readonly string[] AllowedDomainSuffixes =
    {
        "redeicpbrasil.com.br",
        "redeicpbrasil.com",
        "acbr.com.br",
        "acnotarial.com.br",
        "acsincorrio.com.br",
        "acsincor.com.br",
        "acfenacor.com.br",
        "aureaid.com.br",
    };

    private readonly BiometriaService _biometriaService;
    private readonly SemaphoreSlim _signatureLock = new(1, 1);
    private readonly SemaphoreSlim _biometricLock = new(1, 1);
    private WebApplication? _application;

    public CertificateBridgeService(BiometriaService biometriaService)
    {
        _biometriaService = biometriaService;
    }

    public bool IsRunning => _application is not null;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_application is not null)
        {
            return;
        }

        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(CertificateBridgeService).Assembly.FullName,
            ContentRootPath = AppContext.BaseDirectory,
        });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls($"http://127.0.0.1:{Port}");

        var application = builder.Build();
        ConfigurePipeline(application);

        await application.StartAsync(cancellationToken).ConfigureAwait(false);
        _application = application;
    }

    public async Task RestartAsync(CancellationToken cancellationToken = default)
    {
        await StopAsync(cancellationToken).ConfigureAwait(false);
        await StartAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        var application = Interlocked.Exchange(ref _application, null);
        if (application is null)
        {
            return;
        }

        await application.StopAsync(cancellationToken).ConfigureAwait(false);
        await application.DisposeAsync().ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _signatureLock.Dispose();
        _biometricLock.Dispose();
    }

    private void ConfigurePipeline(WebApplication application)
    {
        application.Use(async (context, next) =>
        {
            var origin = context.Request.Headers.Origin.ToString();
            if (!IsAllowedOrigin(origin))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new { erro = "Origem não autorizada." }).ConfigureAwait(false);
                return;
            }

            if (!string.IsNullOrWhiteSpace(origin))
            {
                context.Response.Headers.AccessControlAllowOrigin = origin;
                context.Response.Headers.Vary = "Origin";
                context.Response.Headers.AccessControlAllowCredentials = "true";
            }
            context.Response.Headers.AccessControlAllowHeaders = "Content-Type, Authorization";
            context.Response.Headers.AccessControlAllowMethods = "GET, POST, OPTIONS";
            context.Response.Headers.AccessControlMaxAge = "600";
            context.Response.Headers["Access-Control-Allow-Private-Network"] = "true";

            if (HttpMethods.IsOptions(context.Request.Method))
            {
                context.Response.StatusCode = StatusCodes.Status204NoContent;
                return;
            }

            await next().ConfigureAwait(false);
        });

        application.MapGet("/health", () => Results.Json(new { status = "ok", product = "Gerenciador ICP Brasil" }));
        application.MapGet("/certificados", ListCertificatesAsync);
        application.MapPost("/assinar", SignAsync);
        application.MapPost("/biometria/capturar", CaptureBiometricsAsync);
        application.MapGet("/biometria/capturar/stream", StreamBiometricsAsync);
    }

    private static bool IsAllowedOrigin(string? origin)
    {
        if (string.IsNullOrWhiteSpace(origin))
        {
            return false;
        }

        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return false;
        }

        if (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            uri.Host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return AllowedDomainSuffixes.Any(domain =>
            uri.Host.Equals(domain, StringComparison.OrdinalIgnoreCase) ||
            uri.Host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<IResult> ListCertificatesAsync(CancellationToken cancellationToken)
    {
        var result = await RunCertificateSelectorAsync(new[] { "--json" }, cancellationToken).ConfigureAwait(false);
        return result.Success
            ? Results.Text(result.Output, "application/json", Encoding.UTF8)
            : Results.Json(new { erro = "Falha ao buscar certificados.", detalhe = result.Error }, statusCode: 500);
    }

    private async Task<IResult> SignAsync(HttpContext context, CancellationToken cancellationToken)
    {
        SignatureRequest? request;
        try
        {
            request = await context.Request.ReadFromJsonAsync<SignatureRequest>(cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            return Results.Json(new { erro = "Requisição inválida." }, statusCode: 400);
        }

        if (request is null || request.Index < 0 ||
            !TryNormalizeChallenge(request.Challenge, out var normalizedChallenge))
        {
            return Results.Json(new { erro = "Índice ou desafio criptográfico inválido." }, statusCode: 400);
        }

        if (!await _signatureLock.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return Results.Json(new { erro = "Já existe uma assinatura em andamento." }, statusCode: 409);
        }

        try
        {
            var result = await RunCertificateSelectorAsync(
                new[] { "--sign", request.Index.ToString(), normalizedChallenge },
                cancellationToken).ConfigureAwait(false);
            return result.Success
                ? Results.Text(result.Output, "application/json", Encoding.UTF8)
                : Results.Json(new { erro = "Erro ao assinar com o certificado.", detalhe = result.Error }, statusCode: 500);
        }
        finally
        {
            _signatureLock.Release();
        }
    }

    private async Task<IResult> CaptureBiometricsAsync(HttpContext context, CancellationToken cancellationToken)
    {
        if (!await _biometricLock.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return Results.Json(new { status = "busy", message = "Já existe uma operação biométrica em andamento." }, statusCode: 409);
        }

        try
        {
            var request = await context.Request.ReadFromJsonAsync<BiometricRequest>(cancellationToken).ConfigureAwait(false);
            var timeout = Math.Clamp(request?.TimeoutMs ?? 15000, 1000, 60000);
            var result = await _biometriaService.CaptureAsync(timeout).ConfigureAwait(false);
            return Results.Json(result, statusCode: result.Success ? 200 : 500);
        }
        finally
        {
            _biometricLock.Release();
        }
    }

    private async Task StreamBiometricsAsync(HttpContext context, CancellationToken cancellationToken)
    {
        context.Response.ContentType = "text/event-stream";
        context.Response.Headers.CacheControl = "no-cache";

        if (!await _biometricLock.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            await WriteServerEventAsync(context, "result", new { status = "busy", message = "Captura biométrica em andamento." }, cancellationToken).ConfigureAwait(false);
            await WriteServerEventAsync(context, "end", new { success = false }, cancellationToken).ConfigureAwait(false);
            return;
        }

        try
        {
            var timeout = int.TryParse(context.Request.Query["timeoutMs"], out var requested)
                ? Math.Clamp(requested, 1000, 60000)
                : 15000;
            var result = await _biometriaService.CaptureAsync(timeout).ConfigureAwait(false);
            await WriteServerEventAsync(context, "result", result, cancellationToken).ConfigureAwait(false);
            await WriteServerEventAsync(context, "end", new { success = result.Success }, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _biometricLock.Release();
        }
    }

    private static async Task WriteServerEventAsync(HttpContext context, string eventName, object payload, CancellationToken cancellationToken)
    {
        await context.Response.WriteAsync($"event:{eventName}\n", cancellationToken).ConfigureAwait(false);
        await context.Response.WriteAsync($"data:{JsonSerializer.Serialize(payload)}\n\n", cancellationToken).ConfigureAwait(false);
        await context.Response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<ProcessResult> RunCertificateSelectorAsync(IEnumerable<string> arguments, CancellationToken cancellationToken)
    {
        var executable = Path.Combine(AppContext.BaseDirectory, "Helpers", "CertificateSelector", "CertSelector.exe");
        if (!File.Exists(executable))
        {
            return new ProcessResult(false, string.Empty, "O seletor de certificados não foi encontrado no pacote.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = Path.GetDirectoryName(executable)!,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return new ProcessResult(false, string.Empty, "Não foi possível iniciar o seletor de certificados.");
            }

            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var output = (await outputTask.ConfigureAwait(false)).Trim();
            var error = (await errorTask.ConfigureAwait(false)).Trim();
            return new ProcessResult(process.ExitCode == 0 && IsValidJson(output), output, error);
        }
        catch (Exception ex)
        {
            return new ProcessResult(false, string.Empty, ex.Message);
        }
    }

    private static bool IsValidJson(string value)
    {
        try
        {
            using var document = JsonDocument.Parse(value);
            return document.RootElement.ValueKind is JsonValueKind.Object or JsonValueKind.Array;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryNormalizeChallenge(string? challenge, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(challenge) || challenge.Length > 8192)
        {
            return false;
        }

        try
        {
            var bytes = Convert.FromBase64String(challenge);
            if (bytes.Length is < 16 or > 4096)
            {
                return false;
            }

            normalized = Convert.ToBase64String(bytes);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private sealed record SignatureRequest(int Index, string? Challenge);
    private sealed record BiometricRequest(int TimeoutMs);
    private sealed record ProcessResult(bool Success, string Output, string Error);
}
