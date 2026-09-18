using System;
using System.IO;
using System.Reflection;
using System.Text;

namespace GerenciadorIcpBrasil.Modules.ConfigAuditoria.Logging;

public static class LogWriter
{
    private static readonly string LogDirectory =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ConfigAuditoria", "Logs");

    private static readonly string LogFilePath = Path.Combine(LogDirectory, "app.log");
    private static readonly object SyncRoot = new();
    private static bool _versionEntryWritten;

    public static void Write(string message)
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);
            EnsureVersionEntry();
            var entry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}";

            lock (SyncRoot)
            {
                File.AppendAllText(LogFilePath, entry, Encoding.UTF8);
            }
        }
        catch
        {
            // Logging must never throw.
        }
    }

    public static void Write(Exception exception, string context) =>
        Write($"{context}: {exception}");

    public static string LogPath => LogFilePath;

    private static void EnsureVersionEntry()
    {
        if (_versionEntryWritten)
        {
            return;
        }

        lock (SyncRoot)
        {
            if (_versionEntryWritten)
            {
                return;
            }

            var versionMessage = $"Aplicativo iniciado - versão {ResolveAppVersion()}";
            var header = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {versionMessage}{Environment.NewLine}";
            File.AppendAllText(LogFilePath, header, Encoding.UTF8);
            _versionEntryWritten = true;
        }
    }

    private static string ResolveAppVersion()
    {
        try
        {
            var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
            if (assembly is null)
            {
                return "desconhecida";
            }

            var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrWhiteSpace(informational))
            {
                return informational!;
            }

            var fileVersion = assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version;
            if (!string.IsNullOrWhiteSpace(fileVersion))
            {
                return fileVersion!;
            }

            var version = assembly.GetName().Version;
            return version?.ToString() ?? "desconhecida";
        }
        catch
        {
            return "desconhecida";
        }
    }
}
