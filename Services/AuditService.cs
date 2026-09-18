using System.Text.Json;
using System.Text.Json.Serialization;

namespace GerenciadorIcpBrasil.Services;

/// <summary>
/// Registra eventos técnicos somente no computador. A edição pública não transmite telemetria.
/// </summary>
public sealed class AuditService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    private readonly string _auditDirectory;
    private readonly string _eventsPath;
    private readonly string _installationPath;
    private readonly string _appVersion;

    public AuditService(string appVersion)
    {
        _auditDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Gerenciador ICP Brasil",
            "audit");
        _eventsPath = Path.Combine(_auditDirectory, "events.jsonl");
        _installationPath = Path.Combine(_auditDirectory, "installation.json");
        _appVersion = appVersion;
        Directory.CreateDirectory(_auditDirectory);
    }

    public async Task EnsureAppInstallEventAsync()
    {
        var installationId = GetOrCreateInstallationId();
        if (await HasEventAsync(installationId, "app.install").ConfigureAwait(false))
        {
            return;
        }

        await TrackEventAsync(new AuditEvent
        {
            InstallationId = installationId,
            EventType = "app.install",
            Source = "app"
        }).ConfigureAwait(false);
    }

    public Task TrackModuleInstallAsync(string moduleId, string? moduleVersion) =>
        TrackEventAsync(new AuditEvent
        {
            InstallationId = GetOrCreateInstallationId(),
            EventType = "module.install",
            ModuleId = moduleId,
            ModuleVersion = moduleVersion,
            Source = "app"
        });

    public Task TrackModuleUninstallAsync(string moduleId, string? moduleVersion) =>
        TrackEventAsync(new AuditEvent
        {
            InstallationId = GetOrCreateInstallationId(),
            EventType = "module.uninstall",
            ModuleId = moduleId,
            ModuleVersion = moduleVersion,
            Source = "app"
        });

    public async Task TrackEventAsync(AuditEvent auditEvent)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);
        var localEvent = auditEvent with
        {
            InstallationId = string.IsNullOrWhiteSpace(auditEvent.InstallationId)
                ? GetOrCreateInstallationId()
                : auditEvent.InstallationId,
            EventId = string.IsNullOrWhiteSpace(auditEvent.EventId)
                ? Guid.NewGuid().ToString("N")
                : auditEvent.EventId,
            Timestamp = auditEvent.Timestamp == default ? DateTime.UtcNow : auditEvent.Timestamp,
            Hostname = null,
            Macs = null,
            Os = string.IsNullOrWhiteSpace(auditEvent.Os) ? Environment.OSVersion.Platform.ToString() : auditEvent.Os,
            OsVersion = string.IsNullOrWhiteSpace(auditEvent.OsVersion) ? Environment.OSVersion.VersionString : auditEvent.OsVersion,
            Arch = string.IsNullOrWhiteSpace(auditEvent.Arch)
                ? System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString()
                : auditEvent.Arch,
            AppVersion = string.IsNullOrWhiteSpace(auditEvent.AppVersion) ? _appVersion : auditEvent.AppVersion
        };

        Directory.CreateDirectory(_auditDirectory);
        await File.AppendAllTextAsync(
            _eventsPath,
            JsonSerializer.Serialize(localEvent, SerializerOptions) + Environment.NewLine).ConfigureAwait(false);
        await PurgeOldEventsAsync().ConfigureAwait(false);
    }

    public Task SendPendingAsync() => Task.CompletedTask;

    private async Task<bool> HasEventAsync(string installationId, string eventType)
    {
        if (!File.Exists(_eventsPath))
        {
            return false;
        }

        foreach (var line in await File.ReadAllLinesAsync(_eventsPath).ConfigureAwait(false))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                var item = JsonSerializer.Deserialize<AuditEvent>(line, SerializerOptions);
                if (item?.InstallationId == installationId && item.EventType == eventType)
                {
                    return true;
                }
            }
            catch (JsonException)
            {
                // Um registro inválido não deve impedir a inicialização.
            }
        }

        return false;
    }

    private async Task PurgeOldEventsAsync()
    {
        if (!File.Exists(_eventsPath))
        {
            return;
        }

        var cutoff = DateTime.UtcNow.AddMonths(-12);
        var retained = new List<string>();
        foreach (var line in await File.ReadAllLinesAsync(_eventsPath).ConfigureAwait(false))
        {
            try
            {
                var item = JsonSerializer.Deserialize<AuditEvent>(line, SerializerOptions);
                if (item is not null && item.Timestamp >= cutoff)
                {
                    retained.Add(line);
                }
            }
            catch (JsonException)
            {
                // Descarta registros locais corrompidos.
            }
        }

        await File.WriteAllLinesAsync(_eventsPath, retained).ConfigureAwait(false);
    }

    private string GetOrCreateInstallationId()
    {
        try
        {
            if (File.Exists(_installationPath))
            {
                var current = JsonSerializer.Deserialize<InstallationData>(File.ReadAllText(_installationPath), SerializerOptions);
                if (!string.IsNullOrWhiteSpace(current?.InstallationId))
                {
                    return current.InstallationId;
                }
            }
        }
        catch (JsonException)
        {
            // Gera um identificador local novo se o arquivo estiver corrompido.
        }

        var installationId = Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(_auditDirectory);
        File.WriteAllText(
            _installationPath,
            JsonSerializer.Serialize(new InstallationData { InstallationId = installationId }, SerializerOptions));
        return installationId;
    }

    private sealed class InstallationData
    {
        [JsonPropertyName("installationId")]
        public string InstallationId { get; set; } = string.Empty;
    }

    public sealed record AuditEvent
    {
        [JsonPropertyName("installationId")]
        public string InstallationId { get; init; } = string.Empty;

        [JsonPropertyName("eventId")]
        public string EventId { get; init; } = string.Empty;

        [JsonPropertyName("eventType")]
        public string EventType { get; init; } = string.Empty;

        [JsonPropertyName("timestamp")]
        public DateTime Timestamp { get; init; }

        [JsonPropertyName("hostname")]
        public string? Hostname { get; init; }

        [JsonPropertyName("macs")]
        public List<string>? Macs { get; init; }

        [JsonPropertyName("os")]
        public string? Os { get; init; }

        [JsonPropertyName("osVersion")]
        public string? OsVersion { get; init; }

        [JsonPropertyName("arch")]
        public string? Arch { get; init; }

        [JsonPropertyName("appVersion")]
        public string? AppVersion { get; init; }

        [JsonPropertyName("moduleId")]
        public string? ModuleId { get; init; }

        [JsonPropertyName("moduleVersion")]
        public string? ModuleVersion { get; init; }

        [JsonPropertyName("source")]
        public string? Source { get; init; }

        [JsonPropertyName("payload")]
        public Dictionary<string, object?>? Payload { get; init; }
    }
}
