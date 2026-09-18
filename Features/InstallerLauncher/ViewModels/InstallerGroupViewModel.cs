using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using GerenciadorIcpBrasil.Modules.InstallerLauncher.Models;

namespace GerenciadorIcpBrasil.Modules.InstallerLauncher.ViewModels;

public sealed class InstallerGroupViewModel : INotifyPropertyChanged
{
    private readonly Action<string, bool>? _onExpansionChanged;
    private bool _isExpanded;

    public InstallerGroupViewModel(
        string title,
        IEnumerable<InstallerItemViewModel> items,
        bool isExpanded = false,
        Action<string, bool>? onExpansionChanged = null)
    {
        Title = title;
        Items = new ObservableCollection<InstallerItemViewModel>(items);
        _isExpanded = isExpanded;
        _onExpansionChanged = onExpansionChanged;
    }

    public string Title { get; }

    public ObservableCollection<InstallerItemViewModel> Items { get; }

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
            _onExpansionChanged?.Invoke(Title, value);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
