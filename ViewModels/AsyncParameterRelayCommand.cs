using System.Diagnostics;
using System.Windows.Input;

namespace WolfEQ.ViewModels;

public sealed class AsyncParameterRelayCommand(Func<object?, Task> execute, Func<object?, bool>? canExecute = null) : ICommand
{
    private bool _isExecuting;

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter)
        => !_isExecuting && (canExecute?.Invoke(parameter) ?? true);

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
            await execute(parameter);
        }
        catch (Exception ex)
        {
            // Execute is async void, so no awaiter can observe this exception.
            // Log and swallow it here instead of letting it crash the app.
            Trace.TraceError($"AsyncParameterRelayCommand execution failed: {ex}");
        }
        finally
        {
            _isExecuting = false;
            RaiseCanExecuteChanged();
        }
    }

    public void RaiseCanExecuteChanged()
        => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
