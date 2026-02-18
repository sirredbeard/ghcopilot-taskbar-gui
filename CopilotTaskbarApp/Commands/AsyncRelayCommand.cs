using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace CopilotTaskbarApp.Commands;

/// <summary>
/// An async <see cref="ICommand"/> that wraps a <see cref="Func{Task}"/>.
/// Automatically disables itself while the task is running to prevent re-entrancy.
/// </summary>
public sealed class AsyncRelayCommand : ICommand
{
    private readonly Func<CancellationToken, Task> _execute;
    private readonly Func<bool>? _canExecute;
    private int _isRunning; // 0 = idle, 1 = running (Interlocked)

    public event EventHandler? CanExecuteChanged;

    public AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null)
        : this(_ => execute(), canExecute) { }

    public AsyncRelayCommand(Func<CancellationToken, Task> execute, Func<bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter)
        => Interlocked.CompareExchange(ref _isRunning, 0, 0) == 0
           && (_canExecute?.Invoke() ?? true);

    public async void Execute(object? parameter) => await ExecuteAsync();

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref _isRunning, 1, 0) != 0)
            return; // already running

        NotifyCanExecuteChanged();
        try
        {
            await _execute(cancellationToken);
        }
        finally
        {
            Interlocked.Exchange(ref _isRunning, 0);
            NotifyCanExecuteChanged();
        }
    }

    public void NotifyCanExecuteChanged()
        => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>
/// An async <see cref="ICommand"/> that wraps a <see cref="Func{T, Task}"/> with a typed parameter.
/// </summary>
public sealed class AsyncRelayCommand<T> : ICommand
{
    private readonly Func<T?, CancellationToken, Task> _execute;
    private readonly Func<T?, bool>? _canExecute;
    private int _isRunning;

    public event EventHandler? CanExecuteChanged;

    public AsyncRelayCommand(Func<T?, Task> execute, Func<T?, bool>? canExecute = null)
        : this((arg, _) => execute(arg), canExecute) { }

    public AsyncRelayCommand(Func<T?, CancellationToken, Task> execute, Func<T?, bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter)
        => Interlocked.CompareExchange(ref _isRunning, 0, 0) == 0
           && (_canExecute?.Invoke(CastParameter(parameter)) ?? true);

    public async void Execute(object? parameter) => await ExecuteAsync(CastParameter(parameter));

    public async Task ExecuteAsync(T? parameter, CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref _isRunning, 1, 0) != 0)
            return;

        NotifyCanExecuteChanged();
        try
        {
            await _execute(parameter, cancellationToken);
        }
        finally
        {
            Interlocked.Exchange(ref _isRunning, 0);
            NotifyCanExecuteChanged();
        }
    }

    public void NotifyCanExecuteChanged()
        => CanExecuteChanged?.Invoke(this, EventArgs.Empty);

    private static T? CastParameter(object? parameter)
        => parameter is T typed ? typed : default;
}
