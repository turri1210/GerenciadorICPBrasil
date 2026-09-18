using System.Text.Json;
using System.Text.Json.Serialization;

namespace GerenciadorIcpBrasil.Services;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    private readonly string _settingsPath;
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private SettingsData _data = new();

    public SettingsService(string appDataRoot)
    {
        Directory.CreateDirectory(appDataRoot);
        _settingsPath = Path.Combine(appDataRoot, "settings.json");
    }

    public bool FirstRunCompleted { get; private set; }

    public async Task LoadAsync()
    {
        if (!File.Exists(_settingsPath))
        {
            FirstRunCompleted = false;
            return;
        }

        try
        {
            var json = await File.ReadAllTextAsync(_settingsPath).ConfigureAwait(false);
            _data = JsonSerializer.Deserialize<SettingsData>(json, SerializerOptions) ?? new SettingsData();
            FirstRunCompleted = _data.FirstRunCompleted;
        }
        catch
        {
            _data = new SettingsData();
            FirstRunCompleted = false;
        }
    }

    public async Task MarkFirstRunCompletedAsync()
    {
        FirstRunCompleted = true;
        _data.FirstRunCompleted = true;
        await SaveAsync().ConfigureAwait(false);
    }

    public bool GetCategoryExpandedState(string key, bool defaultValue = false)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return defaultValue;
        }

        return _data.CategoryExpandedStates.TryGetValue(key, out var value) ? value : defaultValue;
    }

    public async Task SetCategoryExpandedStateAsync(string key, bool isExpanded)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        _data.CategoryExpandedStates[key] = isExpanded;
        await SaveAsync().ConfigureAwait(false);
    }

    private async Task SaveAsync()
    {
        await _saveLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var json = JsonSerializer.Serialize(_data, SerializerOptions);
            await File.WriteAllTextAsync(_settingsPath, json).ConfigureAwait(false);
        }
        finally
        {
            _saveLock.Release();
        }
    }

    private sealed class SettingsData
    {
        [JsonPropertyName("firstRunCompleted")]
        public bool FirstRunCompleted { get; set; }

        [JsonPropertyName("categoryExpandedStates")]
        public Dictionary<string, bool> CategoryExpandedStates { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
