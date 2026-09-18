using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace GerenciadorIcpBrasil.Models;

public sealed class UsefulLinkGroup : INotifyPropertyChanged
{
    private readonly Action<string, bool>? _onExpansionChanged;
    private bool _isExpanded;

    public UsefulLinkGroup(string category, bool isExpanded = false, Action<string, bool>? onExpansionChanged = null)
    {
        Category = category;
        _isExpanded = isExpanded;
        _onExpansionChanged = onExpansionChanged;
    }

    public string Category { get; }
    public ObservableCollection<UsefulLinkItem> Items { get; } = new();

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value)
            {
                return;
            }

            _isExpanded = value;
            OnPropertyChanged();
            _onExpansionChanged?.Invoke(Category, value);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
