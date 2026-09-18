using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace ConfigAuditoria.Commands;

public class AsyncRelayCommand : ICommand
{
    private readonly Func<Task> _execute;
    private readonly Func<bool>? _canExecute;
    private readonly DispatcherInvoker _dispatcherInvoker;
    private bool _isExecuting;

    public AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
        _dispatcherInvoker = new DispatcherInvoker();
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

    public void RaiseCanExecuteChanged() =>
        _dispatcherInvoker.Invoke(() => CanExecuteChanged?.Invoke(this, EventArgs.Empty));
}

public class AsyncRelayCommand<T> : ICommand
{
    private readonly Func<T?, Task> _execute;
    private readonly Predicate<T?>? _canExecute;
    private readonly DispatcherInvoker _dispatcherInvoker;
    private bool _isExecuting;

    public AsyncRelayCommand(Func<T?, Task> execute, Predicate<T?>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
        _dispatcherInvoker = new DispatcherInvoker();
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter)
    {
        if (_isExecuting)
        {
            return false;
        }

        if (_canExecute is null)
        {
            return true;
        }

        return parameter is T typed ? _canExecute(typed) : _canExecute(default);
    }

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

            var argument = parameter is T typed ? typed : default;
            await _execute(argument);
        }
        finally
        {
            _isExecuting = false;
            RaiseCanExecuteChanged();
        }
    }

    public void RaiseCanExecuteChanged() =>
        _dispatcherInvoker.Invoke(() => CanExecuteChanged?.Invoke(this, EventArgs.Empty));
}

internal sealed class DispatcherInvoker
{
    private readonly System.Windows.Threading.Dispatcher? _dispatcher = Application.Current?.Dispatcher;

    public void Invoke(Action action)
    {
        if (action is null)
        {
            return;
        }

        if (_dispatcher is null || _dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            _dispatcher.BeginInvoke(action);
        }
    }
}
