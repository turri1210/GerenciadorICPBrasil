using System.Diagnostics;
using System.Text.Json;

namespace GerenciadorIcpBrasil.Services;

public sealed class BiometriaService
{
    public async Task<BiometriaResult> CheckStatusAsync()
    {
        var exePath = ResolveExecutablePath();
        if (exePath == null)
        {
            return BiometriaResult.Fail("Executavel da biometria nao encontrado.");
        }

        var result = await RunProcessAsync(exePath, "status", timeoutMs: 8000).ConfigureAwait(false);
        return result;
    }

    public async Task<BiometriaResult> CaptureAsync(int timeoutMs)
    {
        var exePath = ResolveExecutablePath();
        if (exePath == null)
        {
            return BiometriaResult.Fail("Executavel da biometria nao encontrado.");
        }

        var arg = $"capture --timeout={timeoutMs}";
        var result = await RunProcessAsync(exePath, arg, timeoutMs: timeoutMs + 6000).ConfigureAwait(false);
        if (!result.Success && ShouldRetryWithExtendedTimeout(result))
        {
            const int fallbackTimeoutMs = 3000;
            var retryArg = $"capture --timeout={fallbackTimeoutMs}";
            result = await RunProcessAsync(exePath, retryArg, timeoutMs: fallbackTimeoutMs + 6000).ConfigureAwait(false);
        }

        return result;
    }

    private string? ResolveExecutablePath()
    {
        var moduleDir = Path.Combine(AppContext.BaseDirectory, "Helpers", "Biometrics");
        var exePath = Path.Combine(moduleDir, "BiometriaFs88h.exe");
        return File.Exists(exePath) ? exePath : null;
    }

    private async Task<BiometriaResult> RunProcessAsync(string exePath, string args, int timeoutMs)
    {
        var moduleDir = Path.GetDirectoryName(exePath) ?? AppContext.BaseDirectory;
        var driversDir = Path.Combine(moduleDir, "drivers");

        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = args,
            WorkingDirectory = moduleDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        psi.Environment["FUTRONIC_SDK_PATH"] = driversDir;
        var currentPath = psi.Environment["PATH"] ?? string.Empty;
        if (!currentPath.Contains(driversDir, StringComparison.OrdinalIgnoreCase))
        {
            psi.Environment["PATH"] = driversDir + ";" + currentPath;
        }

        try
        {
            using var process = Process.Start(psi);
            if (process == null)
            {
                return BiometriaResult.Fail("Falha ao iniciar o executável biométrico.");
            }

            using var cts = new CancellationTokenSource(timeoutMs);
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            var exitTask = process.WaitForExitAsync(cts.Token);

            try
            {
                await exitTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(true); } catch { }
                return BiometriaResult.Fail("Timeout ao aguardar resposta da leitora biométrica.");
            }

            var output = await outputTask.ConfigureAwait(false);
            var error = await errorTask.ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(output))
            {
                var parsed = TryParse(output);
                if (parsed != null)
                {
                    return parsed;
                }
            }

            var message = string.IsNullOrWhiteSpace(error) ? "Resposta inválida da leitora biométrica." : error.Trim();
            return BiometriaResult.Fail(message);
        }
        catch (Exception ex)
        {
            return BiometriaResult.Fail(ex.Message);
        }
    }

    private static BiometriaResult? TryParse(string raw)
    {
        try
        {
            var payload = JsonSerializer.Deserialize<BiometriaPayload>(raw.Trim(), new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });

            if (payload == null)
            {
                return null;
            }

            var image = payload.PngBase64 ?? payload.RawBase64;
            if (string.Equals(payload.Status, "ok", StringComparison.OrdinalIgnoreCase))
            {
                return BiometriaResult.Ok(payload.Message ?? "Leitora pronta.", image, payload.Width, payload.Height, payload.ErrorCode);
            }

            return BiometriaResult.Fail(payload.Message ?? "Falha na leitora biométrica.", image, payload.Width, payload.Height, payload.ErrorCode);
        }
        catch
        {
            return null;
        }
    }

    private static bool ShouldRetryWithExtendedTimeout(BiometriaResult result)
    {
        if (result.ErrorCode == 202)
        {
            return true;
        }

        return result.Message.Contains("codigo 202", StringComparison.OrdinalIgnoreCase)
            || result.Message.Contains("code 202", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class BiometriaPayload
    {
        public string? Status { get; set; }
        public string? Message { get; set; }
        public int? ErrorCode { get; set; }
        public int? Width { get; set; }
        public int? Height { get; set; }
        public string? PngBase64 { get; set; }
        public string? RawBase64 { get; set; }
    }

    public sealed record BiometriaResult(bool Success, string Message, string? ImageBase64, int? Width, int? Height, int? ErrorCode = null)
    {
        public static BiometriaResult Ok(string message, string? imageBase64, int? width, int? height, int? errorCode = null)
            => new(true, message, imageBase64, width, height, errorCode);

        public static BiometriaResult Fail(string message, string? imageBase64 = null, int? width = null, int? height = null, int? errorCode = null)
            => new(false, message, imageBase64, width, height, errorCode);
    }
}
