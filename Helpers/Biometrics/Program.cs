using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using Futronic.SDKHelper;

namespace BiometriaFs88h;

internal static class Program
{
    private const int DefaultTimeoutMs = 15000;
    private const int MaxCaptureAttempts = 2;
    private static bool _streamMode;

    private static readonly IReadOnlyDictionary<int, string> ErrorCatalog = new Dictionary<int, string>
    {
        { 0, "Captura nao foi concluida. Posicione o dedo com firmeza e tente novamente." },
        { 5, "Permissao negada pelo driver. Execute como administrador ou reinstale o driver da leitora." },
        { 31, "O Windows nao conseguiu inicializar o dispositivo. Reconecte a leitora em uma porta USB direta." },
        { 32, "Outro processo esta usando a leitora. Feche servicos biometricos paralelos antes de tentar novamente." },
        { 87, "Os parametros enviados ao driver foram rejeitados. Utilize as DLLs fornecidas pelo driver oficial (x86)." },
        { 111, "Buffer insuficiente. Atualize o driver da Futronic." },
        { 121, "Tempo limite de comunicacao com a leitora. Verifique cabos e tente novamente." },
        { 122, "Resposta incompleta do dispositivo. Replugue a leitora e reinicie o aplicativo." },
        { 1168, "O dispositivo nao respondeu. Certifique-se de que o driver esta instalado." },
        { 1217, "O dispositivo foi removido durante a captura. Reconecte e tente novamente." },
        { 1224, "O dispositivo esta ocupado. Aguarde a operacao corrente finalizar." },
        { 1300, "O PIN requisitado para o dispositivo nao foi fornecido. Reinicie a leitora." },
        { 1314, "Nao ha privilegios suficientes para acessar a leitora. Abra o aplicativo como administrador." },
        { 1460, "Tempo esgotado aguardando o dedo. Posicione o dedo ate o LED verde apagar." },
        { unchecked((int)0x20000001), "O sensor ainda esta processando uma leitura. Aguarde alguns segundos e repita." },
        { unchecked((int)0x20000002), "A leitora nao detectou o dedo. Apoie o dedo inteiro sobre o sensor." },
        { unchecked((int)0x20000003), "A leitura nao foi aprovada pela deteccao de dedo falso. Limpe o sensor e tente novamente." },
        { unchecked((int)0x20000004), "O driver Futronic esta desatualizado. Instale a versao mais recente do pacote." }
    };

    private static bool _nativeBindingsLoaded;
    private static string? _nativeBindingsDirectory;

    [STAThread]
    private static int Main(string[] args)
    {
        _streamMode = string.Equals(
            Environment.GetEnvironmentVariable("BIOMETRIA_STREAM"),
            "1",
            StringComparison.OrdinalIgnoreCase);

        EnsureNativeLibraries();

        string command = "capture";
        int timeout = DefaultTimeoutMs;

        foreach (var arg in args)
        {
            if (string.Equals(arg, "status", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(arg, "--status", StringComparison.OrdinalIgnoreCase))
            {
                command = "status";
            }
            else if (string.Equals(arg, "capture", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(arg, "--capture", StringComparison.OrdinalIgnoreCase))
            {
                command = "capture";
            }
            else if (arg.StartsWith("--timeout=", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(arg.Split('=', 2)[1], out var parsed) && parsed > 0)
                {
                    timeout = parsed;
                }
            }
        }

        try
        {
            var response = command == "status"
                ? Status()
                : Capture(timeout);

            return WriteJson(response);
        }
        catch (DllNotFoundException dllEx)
        {
            return WriteJson(Error($"Biblioteca Futronic nao encontrada: {dllEx.Message}"));
        }
        catch (BadImageFormatException formatEx)
        {
            return WriteJson(Error($"Formato invalido ao carregar biblioteca Futronic: {formatEx.Message}"));
        }
        catch (Exception ex)
        {
            return WriteJson(Error($"Erro inesperado: {ex.Message}", ex.HResult));
        }
    }

    private static CaptureResponse Status()
    {
        try
        {
            using var enrollment = new FutronicEnrollment();
            EmitEvent("status", new { ready = true });
            return new CaptureResponse
            {
                Status = "ok",
                Message = "ready",
                Diagnostics = CollectDiagnostics()
            };
        }
        catch (FutronicException ex)
        {
            var description = FutronicSdkBase.SdkRetCode2Message(ex.ErrorCode);
            EmitEvent("status", new { ready = false, errorCode = ex.ErrorCode, message = description });
            return Error($"Nao foi possivel inicializar a leitora. {description}", ex.ErrorCode, description);
        }
        catch (DllNotFoundException dllEx)
        {
            EmitEvent("status", new { ready = false, error = dllEx.Message });
            return Error($"Biblioteca Futronic nao encontrada: {dllEx.Message}");
        }
        catch (Exception ex)
        {
            EmitEvent("status", new { ready = false, error = ex.Message });
            return Error($"Falha ao inicializar a leitora: {ex.Message}", ex.HResult);
        }
    }

    private static CaptureResponse Capture(int timeoutMs)
    {
        var effectiveTimeout = timeoutMs > 0 ? timeoutMs : DefaultTimeoutMs;
        var attemptMessages = new List<string>();

        for (var attempt = 1; attempt <= MaxCaptureAttempts; attempt++)
        {
            EmitEvent("attempt-start", new { attempt, timeoutMs = effectiveTimeout });
            using var completionEvent = new ManualResetEvent(false);
            var context = new CaptureContext();
            var imageLock = new object();
            byte[]? latestRaw = null;
            int latestWidth = 0;
            int latestHeight = 0;

            try
            {
                using var enrollment = new FutronicEnrollment();

                enrollment.FakeDetection = false;
                enrollment.FFDControl = false;
                enrollment.FastMode = true;
                enrollment.FARnLevel = FarnValues.farn_normal;
                enrollment.FARN = FutronicSdkBase.rgFARN[(int)FarnValues.farn_normal];
                enrollment.Version = VersionCompatible.ftr_version_current;
                enrollment.MIOTControlOff = true;
                enrollment.MaxModels = 1;

                enrollment.UpdateScreenImage += bitmap =>
                {
                    lock (imageLock)
                    {
                        latestWidth = bitmap.Width;
                        latestHeight = bitmap.Height;
                        latestRaw = ExtractRawPixels(bitmap);
                    }

                    try
                    {
                        var frameBase64 = EncodeBitmapToPngBase64(bitmap);
                        EmitEvent("frame", new { attempt, pngBase64 = frameBase64 });
                    }
                    catch
                    {
                    }
                };

                enrollment.OnPutOn += _ =>
                {
                    context.LastStatus = "Posicione o dedo sobre o sensor";
                    EmitEvent("put-on", new { attempt });
                };

                enrollment.OnTakeOff += _ =>
                {
                    context.LastStatus = "Pode remover o dedo";
                    EmitEvent("take-off", new { attempt });
                };

                enrollment.OnFakeSource += _ =>
                {
                    context.FakeDetected = true;
                    EmitEvent("fake-detected", new { attempt });
                    return false;
                };

                enrollment.OnEnrollmentComplete += (success, code) =>
                {
                    context.Success = success;
                    context.ErrorCode = code;
                    if (success)
                    {
                        try
                        {
                            context.Template = enrollment.Template;
                            context.Quality = (int)enrollment.Quality;
                            EmitEvent("attempt-success", new { attempt, quality = context.Quality });
                        }
                        catch (Exception inner)
                        {
                            attemptMessages.Add($"Tentativa {attempt}: falha ao copiar template ({inner.Message})");
                            EmitEvent("attempt-warning", new { attempt, message = inner.Message });
                        }
                    }
                    else
                    {
                        var description = FutronicSdkBase.SdkRetCode2Message(code);
                        attemptMessages.Add($"Tentativa {attempt}: {description}");
                        EmitEvent("attempt-warning", new { attempt, errorCode = code, message = description });
                    }
                    completionEvent.Set();
                };

                enrollment.Enrollment();

                if (!completionEvent.WaitOne(effectiveTimeout))
                {
                    try { enrollment.OnCalcel(); } catch { }
                    attemptMessages.Add($"Tentativa {attempt}: tempo esgotado aguardando finalizacao da captura.");
                    EmitEvent("timeout", new { attempt });
                    continue;
                }

                if (!context.Success)
                {
                    if (context.ErrorCode == FutronicSdkBase.RETCODE_NO_MORE_RETRIES)
                    {
                        EmitEvent("attempt-retry", new { attempt, reason = "no-more-retries" });
                        continue;
                    }

                    var description = FutronicSdkBase.SdkRetCode2Message(context.ErrorCode ?? 0);
                    EmitEvent("complete", new { success = false, errorCode = context.ErrorCode, message = description });
                    return Error($"{description} (codigo {context.ErrorCode ?? 0})", context.ErrorCode, description);
                }

                byte[]? rawCopy;
                int widthCopy;
                int heightCopy;
                lock (imageLock)
                {
                    rawCopy = latestRaw;
                    widthCopy = latestWidth;
                    heightCopy = latestHeight;
                }

                string? rawBase64 = null;
                string? pngBase64 = null;
                if (rawCopy != null && widthCopy > 0 && heightCopy > 0)
                {
                    rawBase64 = Convert.ToBase64String(rawCopy);
                    pngBase64 = EncodePngBase64(rawCopy, widthCopy, heightCopy);
                }

                var templateBase64 = context.Template != null ? Convert.ToBase64String(context.Template) : null;

                var successResponse = new CaptureResponse
                {
                    Status = "ok",
                    Message = context.LastStatus ?? "Captura realizada com sucesso.",
                    Width = widthCopy > 0 ? widthCopy : null,
                    Height = heightCopy > 0 ? heightCopy : null,
                    RawBase64 = rawBase64,
                    PngBase64 = pngBase64,
                    TemplateBase64 = templateBase64,
                    Quality = context.Quality,
                    FakeDetected = context.FakeDetected ? true : (bool?)null,
                    Diagnostics = CollectDiagnostics()
                };

                EmitEvent("complete", new { success = true, attempt, quality = context.Quality });
                return successResponse;
            }
            catch (FutronicException ex)
            {
                var description = FutronicSdkBase.SdkRetCode2Message(ex.ErrorCode);
                EmitEvent("complete", new { success = false, errorCode = ex.ErrorCode, message = description });
                return Error($"Falha durante a captura. {description}", ex.ErrorCode, description);
            }
            catch (DllNotFoundException dllEx)
            {
                EmitEvent("complete", new { success = false, error = dllEx.Message });
                return Error($"Biblioteca Futronic nao encontrada: {dllEx.Message}");
            }
            catch (Exception ex)
            {
                EmitEvent("complete", new { success = false, error = ex.Message });
                return Error($"Erro durante a captura: {ex.Message}", ex.HResult);
            }
        }

        var hint = attemptMessages.Count > 0 ? string.Join("; ", attemptMessages) : "Captura nao concluida apos multiplas tentativas.";
        EmitEvent("complete", new { success = false, errorCode = FutronicSdkBase.RETCODE_NO_MORE_RETRIES, message = hint });
        return Error("Nao foi possivel concluir a captura apos multiplas tentativas.", FutronicSdkBase.RETCODE_NO_MORE_RETRIES, hint);
    }

    private static void EmitEvent(string type, object? data = null)
    {
        if (!_streamMode)
        {
            return;
        }

        try
        {
            var payload = JsonSerializer.Serialize(new { type, data });
            Console.WriteLine("event:" + payload);
        }
        catch
        {
        }
    }

    private static CaptureResponse Error(string message, int? errorCode = null, string? hint = null) =>
        new CaptureResponse
        {
            Status = "error",
            Message = message,
            ErrorCode = errorCode,
            Hint = hint,
            Diagnostics = CollectDiagnostics()
        };

    private static CaptureResponse ErrorFromCode(int code, string fallback)
    {
        var hint = ErrorCatalog.TryGetValue(code, out var friendly)
            ? friendly
            : "Consulte o suporte tecnico da leitora Futronic para obter detalhes.";

        return Error(fallback, code, hint);
    }

    private static int WriteJson(CaptureResponse response)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        Console.WriteLine(JsonSerializer.Serialize(response, options));
        return response.Status == "ok" ? 0 : 1;
    }

    private static string CollectDiagnostics()
    {
        try
        {
            var sb = new StringBuilder();
            sb.Append("process=x86;");
            sb.Append("os=").Append(Environment.OSVersion.VersionString).Append(';');
            sb.Append("is64Proc=").Append(Environment.Is64BitProcess).Append(';');
            sb.Append("is64OS=").Append(Environment.Is64BitOperatingSystem).Append(';');
            if (!string.IsNullOrWhiteSpace(_nativeBindingsDirectory))
            {
                sb.Append("dllDir=").Append(_nativeBindingsDirectory).Append(';');
            }

            var ftrApiPath = GetModulePath("FTRAPI.dll");
            if (!string.IsNullOrWhiteSpace(ftrApiPath))
            {
                sb.Append("FTRAPI=").Append(ftrApiPath).Append(';');
            }

            var scanApiPath = GetModulePath("ftrScanAPI.dll");
            if (!string.IsNullOrWhiteSpace(scanApiPath))
            {
                sb.Append("ftrScanAPI=").Append(scanApiPath).Append(';');
            }

            var helperPath = GetModulePath("ftrSDKHelper13.dll");
            if (!string.IsNullOrWhiteSpace(helperPath))
            {
                sb.Append("ftrSDKHelper13=").Append(helperPath).Append(';');
            }

            return sb.ToString();
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string? GetModulePath(string module)
    {
        var handle = Native.GetModuleHandle(module);
        if (handle == IntPtr.Zero)
        {
            return null;
        }

        var buffer = new StringBuilder(512);
        var length = Native.GetModuleFileName(handle, buffer, buffer.Capacity);
        return length > 0 ? buffer.ToString() : null;
    }

    private static void EnsureNativeLibraries()
    {
        if (_nativeBindingsLoaded)
        {
            return;
        }

        if (Native.GetModuleHandle("ftrScanAPI.dll") != IntPtr.Zero)
        {
            _nativeBindingsLoaded = true;
            return;
        }

        foreach (var directory in EnumerateCandidateDirectories())
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                continue;
            }

            string resolvedDir;
            try
            {
                resolvedDir = Path.GetFullPath(directory);
            }
            catch
            {
                continue;
            }

            if (!Directory.Exists(resolvedDir))
            {
                continue;
            }

            TryLoadLibrary(Path.Combine(resolvedDir, "FTRAPI.dll"));
            TryLoadLibrary(Path.Combine(resolvedDir, "ftrScanAPI.dll"));
            TryLoadLibrary(Path.Combine(resolvedDir, "ftrSDKHelper13.dll"));

            if (Native.GetModuleHandle("ftrScanAPI.dll") != IntPtr.Zero)
            {
                _nativeBindingsDirectory = resolvedDir;
                _nativeBindingsLoaded = true;
                return;
            }
        }
    }

    private static void TryLoadLibrary(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return;
            }

            Native.LoadLibrary(path);
        }
        catch
        {
        }
    }

    private static IEnumerable<string> EnumerateCandidateDirectories()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        IEnumerable<string?> Enumerate()
        {
            var customPath = Environment.GetEnvironmentVariable("FUTRONIC_SDK_PATH");
            if (!string.IsNullOrWhiteSpace(customPath))
            {
                foreach (var segment in customPath.Split(Path.PathSeparator))
                {
                    if (!string.IsNullOrWhiteSpace(segment))
                    {
                        yield return segment.Trim();
                    }
                }
            }

            yield return AppContext.BaseDirectory;
            yield return Path.Combine(AppContext.BaseDirectory, "drivers");
            yield return Path.Combine(AppContext.BaseDirectory, "..", "drivers");
            yield return Path.Combine(AppContext.BaseDirectory, "..", "FutronicSDK");
            yield return Path.Combine(AppContext.BaseDirectory, "..", "FutronicSDK", "Bin");

            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            if (!string.IsNullOrWhiteSpace(programFilesX86))
            {
                yield return Path.Combine(programFilesX86, "Futronic", "SDK 4.2", "Bin");
                yield return Path.Combine(programFilesX86, "Futronic", "SDK 4.2", "Redist", "x86");
            }

            var windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (!string.IsNullOrWhiteSpace(windowsDir))
            {
                yield return Path.Combine(windowsDir, "System32");
                yield return Path.Combine(windowsDir, "SysWOW64");
            }

            var pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrWhiteSpace(pathEnv))
            {
                foreach (var segment in pathEnv.Split(Path.PathSeparator))
                {
                    if (!string.IsNullOrWhiteSpace(segment))
                    {
                        yield return segment.Trim();
                    }
                }
            }
        }

        foreach (var candidate in Enumerate())
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            var normalized = candidate.Trim('"', ' ');
            if (string.IsNullOrWhiteSpace(normalized))
            {
                continue;
            }

            if (seen.Add(normalized))
            {
                yield return normalized;
            }
        }
    }

    private static byte[]? ExtractRawPixels(Bitmap bitmap)
    {
        var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return null;
        }

        var bytesPerPixel = Image.GetPixelFormatSize(bitmap.PixelFormat) / 8;
        if (bytesPerPixel <= 0)
        {
            return null;
        }

        var raw = new byte[rect.Width * rect.Height];
        var bmpData = bitmap.LockBits(rect, ImageLockMode.ReadOnly, bitmap.PixelFormat);
        try
        {
            byte[]? rowBuffer = bytesPerPixel == 1 ? null : new byte[bmpData.Stride];
            for (int y = 0; y < rect.Height; y++)
            {
                var src = IntPtr.Add(bmpData.Scan0, y * bmpData.Stride);
                if (bytesPerPixel == 1)
                {
                    Marshal.Copy(src, raw, y * rect.Width, rect.Width);
                }
                else
                {
                    if (rowBuffer == null || rowBuffer.Length < bmpData.Stride)
                    {
                        rowBuffer = new byte[bmpData.Stride];
                    }

                    Marshal.Copy(src, rowBuffer, 0, bmpData.Stride);
                    for (int x = 0; x < rect.Width; x++)
                    {
                        raw[y * rect.Width + x] = rowBuffer[x * bytesPerPixel];
                    }
                }
            }
        }
        finally
        {
            bitmap.UnlockBits(bmpData);
        }

        return raw;
    }

    private static string EncodePngBase64(byte[] pixels, int width, int height)
    {
        using var bmp = new Bitmap(width, height, PixelFormat.Format8bppIndexed);
        var palette = bmp.Palette;
        for (var i = 0; i < 256; i++)
        {
            palette.Entries[i] = Color.FromArgb(i, i, i);
        }
        bmp.Palette = palette;

        var rect = new Rectangle(0, 0, width, height);
        var bmpData = bmp.LockBits(rect, ImageLockMode.WriteOnly, bmp.PixelFormat);
        try
        {
            for (var y = 0; y < height; y++)
            {
                var dest = IntPtr.Add(bmpData.Scan0, y * bmpData.Stride);
                Marshal.Copy(pixels, y * width, dest, width);
            }
        }
        finally
        {
            bmp.UnlockBits(bmpData);
        }

        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
    return Convert.ToBase64String(ms.ToArray());
}

private static string EncodeBitmapToPngBase64(Bitmap bitmap)
{
    using var clone = bitmap.Clone(new Rectangle(0, 0, bitmap.Width, bitmap.Height), bitmap.PixelFormat);
    using var ms = new MemoryStream();
    clone.Save(ms, ImageFormat.Png);
    return Convert.ToBase64String(ms.ToArray());
}

    private sealed class CaptureContext
    {
        public bool Success { get; set; }
        public int? ErrorCode { get; set; }
        public byte[]? Template { get; set; }
        public int? Quality { get; set; }
        public bool FakeDetected { get; set; }
        public string? LastStatus { get; set; }
    }

    private sealed class CaptureResponse
    {
        public string Status { get; set; } = "error";
        public string? Message { get; set; }
        public int? ErrorCode { get; set; }
        public string? Hint { get; set; }
        public string? Diagnostics { get; set; }
        public int? Width { get; set; }
        public int? Height { get; set; }
        public string? RawBase64 { get; set; }
        public string? PngBase64 { get; set; }
        public string? TemplateBase64 { get; set; }
        public int? Quality { get; set; }
        public bool? FakeDetected { get; set; }
    }

    private static class Native
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr LoadLibrary(string lpFileName);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern int GetModuleFileName(IntPtr hModule, StringBuilder lpFilename, int nSize);
    }
}
