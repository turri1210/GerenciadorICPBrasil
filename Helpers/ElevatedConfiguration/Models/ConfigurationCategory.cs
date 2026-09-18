using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ConfigAuditoria.Models;

public class ConfigurationCategory : INotifyPropertyChanged
{
    private ConfigurationStatus _status;
    private string _statusMessage;
    private bool _isLoading;
    private bool _isWaiting;

    public ConfigurationCategory(string key, string displayName)
    {
        Key = key;
        DisplayName = displayName;
        _status = ConfigurationStatus.Unknown;
        _statusMessage = GetDefaultMessageForStatus(_status);
        _isWaiting = true;
    }

    public string Key { get; }

    public string DisplayName { get; }

    public ConfigurationStatus Status
    {
        get => _status;
        private set
        {
            if (_status == value)
            {
                return;
            }

            _status = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsCompliant));
            OnPropertyChanged(nameof(IsRemediationAvailable));
            OnPropertyChanged(nameof(IsInteractiveRemediationAvailable));
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set
        {
            if (_statusMessage == value)
            {
                return;
            }

            _statusMessage = value;
            OnPropertyChanged();
        }
    }

    public bool IsCompliant => Status == ConfigurationStatus.Compliant;

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (_isLoading == value)
            {
                return;
            }

            _isLoading = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsRemediationAvailable));
            OnPropertyChanged(nameof(IsInteractiveRemediationAvailable));
        }
    }

    public bool IsWaiting
    {
        get => _isWaiting;
        private set
        {
            if (_isWaiting == value)
            {
                return;
            }

            _isWaiting = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsRemediationAvailable));
            OnPropertyChanged(nameof(IsInteractiveRemediationAvailable));
        }
    }

    public bool IsRemediationAvailable => !IsLoading && !IsWaiting && Status == ConfigurationStatus.NonCompliant;

    public bool IsInteractiveRemediationAvailable =>
        !IsLoading && !IsWaiting &&
        (Status == ConfigurationStatus.NonCompliant ||
         string.Equals(Key, "contas-usuarios", StringComparison.OrdinalIgnoreCase));

    public void ApplyAssessment(ConfigurationStatus status, string? message = null)
    {
        IsLoading = false;
        IsWaiting = false;
        Status = status;
        StatusMessage = string.IsNullOrWhiteSpace(message)
            ? GetDefaultMessageForStatus(status)
            : message.Trim();
    }

    public void SetWaiting(string? message = null)
    {
        IsLoading = false;
        IsWaiting = true;
        Status = ConfigurationStatus.Unknown;
        StatusMessage = string.IsNullOrWhiteSpace(message) ? "Aguardando" : message.Trim();
    }

    public void ResetAssessment() => SetWaiting();

    public void BeginLoading(string? message = null)
    {
        IsWaiting = false;
        IsLoading = true;

        if (!string.IsNullOrWhiteSpace(message))
        {
            StatusMessage = message.Trim();
        }
    }

    public void EndLoading(string? message = null)
    {
        IsLoading = false;

        if (!string.IsNullOrWhiteSpace(message))
        {
            StatusMessage = message.Trim();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private static string GetDefaultMessageForStatus(ConfigurationStatus status) =>
        status switch
        {
            ConfigurationStatus.Compliant => "Configuração aplicada",
            ConfigurationStatus.NonCompliant => "Configuração pendente",
            ConfigurationStatus.Pending => "Aguardando ação do usuário",
            _ => "Status pendente de verificação"
        };

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
