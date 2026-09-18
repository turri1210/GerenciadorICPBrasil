using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI;
using Microsoft.UI.Xaml.Media;

namespace GerenciadorIcpBrasil.Models;

public sealed class ModuleItem : INotifyPropertyChanged
{
    private bool _isInstalled;
    private bool _isRunning;
    private string? _installedVersion;

    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;

    public string Initial
    {
        get
        {
            var value = string.IsNullOrWhiteSpace(Name) ? Id : Name;
            return string.IsNullOrWhiteSpace(value) ? "?" : value.Substring(0, 1).ToUpperInvariant();
        }
    }

    public string IconGlyph => Id.ToLowerInvariant() switch
    {
        "leitor-certificado" => "\uE8D2",
        "config-auditoria" => "\uE713",
        "installer-launcher" => "\uE896",
        "biometric-license-status" => "\uE72E",
        "links-uteis" => "\uE71B",
        "leitora-biometrica" => "\uE928",
        _ => "\uE946",
    };

    public bool ShowsRuntimeStatus
        => string.Equals(Id, "leitor-certificado", StringComparison.OrdinalIgnoreCase);

    public bool IsRunning
    {
        get => _isRunning;
        set
        {
            if (_isRunning != value)
            {
                _isRunning = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(RuntimeStatusBrush));
            }
        }
    }

    public SolidColorBrush RuntimeStatusBrush
        => new(IsRunning ? Colors.LimeGreen : Colors.IndianRed);

    public bool IsInstalled
    {
        get => _isInstalled;
        set
        {
            if (_isInstalled != value)
            {
                _isInstalled = value;
                OnPropertyChanged();
            }
        }
    }

    public string? InstalledVersion
    {
        get => _installedVersion;
        set
        {
            if (_installedVersion != value)
            {
                _installedVersion = value;
                OnPropertyChanged();
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
