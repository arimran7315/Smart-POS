using System.Windows.Input;
using System.Windows;
using SmartPOS.App.Services;

namespace SmartPOS.App.ViewModels;

public sealed class AsyncCommand(Func<Task> execute, Func<bool>? canExecute = null, Action<Exception>? onError = null) : ICommand
{
    private bool _running;
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => !_running && (canExecute?.Invoke() ?? true);
    public async void Execute(object? parameter) => await ExecuteAsync(parameter);

    public async Task ExecuteAsync(object? parameter = null)
    {
        if (!CanExecute(parameter)) return;
        _running = true; CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        try { await execute(); }
        catch (Exception exception)
        {
            try
            {
                if (onError is not null) onError(exception);
                else ShowUnexpectedFailure(exception);
            }
            catch (Exception handlerException)
            {
                Serilog.Log.Error(handlerException, "The command error handler failed");
            }
        }
        finally { _running = false; CanExecuteChanged?.Invoke(this, EventArgs.Empty); }
    }

    public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);

    private static void ShowUnexpectedFailure(Exception exception)
    {
        var message = UserErrorHandler.GetMessage(exception, "Running this action");
        var application = Application.Current;
        if (application is null) return;

        void ShowMessage() => MessageBox.Show(application.MainWindow, message, SmartPosIdentity.Name,
            MessageBoxButton.OK, MessageBoxImage.Warning);

        if (application.Dispatcher.CheckAccess()) ShowMessage();
        else application.Dispatcher.Invoke(ShowMessage);
    }
}
