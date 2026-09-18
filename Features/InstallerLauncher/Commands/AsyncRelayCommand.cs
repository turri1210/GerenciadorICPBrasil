using Microsoft.UI.Dispatching;
using System.Windows.Input;

namespace GerenciadorIcpBrasil.Modules.InstallerLauncher.Commands;

public sealed class AsyncRelayCommand : ICommand
{
    private readonly Func<Task> _execute;
    private readonly Func<bool>? _canExecute;
    private bool _isExecuting;
    private readonly DispatcherQueue? _dispatcherQueue =
        GerenciadorIcpBrasil.App.MainWindow?.DispatcherQueue ?? DispatcherQueue.GetForCurrentThread();

    public AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => !_isExecuting && (_canExecute?.Invoke() ?? true);

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
        {
            return;
        }

        try
        {
            _isExecuting = true;
            RaiseCanExecuteChanged();
            await _execute();
        }
        finally
        {
            _isExecuting = false;
            RaiseCanExecuteChanged();
        }
    }

    public void RaiseCanExecuteChanged()
    {
        if (_dispatcherQueue is null || _dispatcherQueue.HasThreadAccess)
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            _dispatcherQueue.TryEnqueue(() => CanExecuteChanged?.Invoke(this, EventArgs.Empty));
        }
    }
}
