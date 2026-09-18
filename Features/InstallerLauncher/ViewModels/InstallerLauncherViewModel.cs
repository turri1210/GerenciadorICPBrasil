using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using GerenciadorIcpBrasil.Modules.InstallerLauncher.Models;
using GerenciadorIcpBrasil.Modules.InstallerLauncher.Services;

namespace GerenciadorIcpBrasil.Modules.InstallerLauncher.ViewModels;

public sealed class InstallerLauncherViewModel : INotifyPropertyChanged
{
    private readonly InstallerManager _manager = new();
    private readonly Func<string, bool> _getExpandedState;
    private readonly Action<string, bool>? _onExpansionChanged;
    private readonly Func<CancellationToken, Task<string?>>? _resolveManualDownloadDirectoryAsync;
    private bool _isAutomaticInstallation = true;

    public InstallerLauncherViewModel(
        Func<string, bool>? getExpandedState = null,
        Action<string, bool>? onExpansionChanged = null,
        Func<CancellationToken, Task<string?>>? resolveManualDownloadDirectoryAsync = null)
    {
        _getExpandedState = getExpandedState ?? (_ => false);
        _onExpansionChanged = onExpansionChanged;
        _resolveManualDownloadDirectoryAsync = resolveManualDownloadDirectoryAsync;

        var categoryOrder = new[]
        {
            InstallerCategory.Java,
            InstallerCategory.Biometria,
            InstallerCategory.Midia,
            InstallerCategory.Leitora,
            InstallerCategory.LeitorA3
        };

        var packages = InstallerCatalogService.GetAllPackages()
            .OrderBy(p => Array.IndexOf(categoryOrder, p.Category))
            .ThenBy(p => p.Sequence)
            .ToList();

        var grouped = packages
            .GroupBy(p => p.Category)
            .Select(group =>
            {
                var title = GetCategoryTitle(group.Key);
                return new InstallerGroupViewModel(
                    title,
                    group.Select(p => new InstallerItemViewModel(
                        p,
                        _manager,
                        () => !IsAutomaticInstallation,
                        _resolveManualDownloadDirectoryAsync)),
                    _getExpandedState(BuildStateKey(title)),
                    _onExpansionChanged);
            })
            .ToList();

        Groups = new ObservableCollection<InstallerGroupViewModel>(grouped);
    }

    public ObservableCollection<InstallerGroupViewModel> Groups { get; }
    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsAutomaticInstallation
    {
        get => _isAutomaticInstallation;
        set
        {
            if (_isAutomaticInstallation == value)
            {
                return;
            }

            _isAutomaticInstallation = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(InstallationModeDescription));
        }
    }

    public string ModuleIntroDescription => "Baixe ou instale os componentes necessários.";

    public string InstallationModeDescription => IsAutomaticInstallation
            ? "Modo automático: baixa e executa o instalador automaticamente."
            : "Modo manual: ao clicar em instalar, escolha a pasta. O arquivo será baixado e a pasta será aberta ao concluir.";

    public void RefreshAll()
    {
        foreach (var group in Groups)
        {
            foreach (var item in group.Items)
            {
                item.RefreshState();
            }
        }
    }

    private static string GetCategoryTitle(InstallerCategory category) =>
        category switch
        {
            InstallerCategory.LeitorA3 => "Leitor A3",
            InstallerCategory.Biometria => "Pacote biométrico",
            InstallerCategory.Midia => "Gerenciadores de mídia",
            InstallerCategory.Leitora => "Drivers de leitora",
            InstallerCategory.Java => "Java",
            _ => category.ToString()
        };

    public static string BuildStateKey(string title) => $"installer:{title}";

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
