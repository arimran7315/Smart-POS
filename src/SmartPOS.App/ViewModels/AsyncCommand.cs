using System.Windows.Input;
using System.Windows;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using SmartPOS.App.Services;

namespace SmartPOS.App.ViewModels;

public sealed class AsyncCommand(Func<Task> execute, Func<bool>? canExecute = null, Action<Exception>? onError = null) : ICommand, INotifyPropertyChanged
{
    private bool _running;
    public bool IsRunning => _running;
    public event EventHandler? CanExecuteChanged;
    public event PropertyChangedEventHandler? PropertyChanged;
    public bool CanExecute(object? parameter) => !_running && (canExecute?.Invoke() ?? true);
    public async void Execute(object? parameter) => await ExecuteAsync(parameter);

    public async Task ExecuteAsync(object? parameter = null)
    {
        if (!CanExecute(parameter)) return;
        SetRunning(true);
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
        finally { SetRunning(false); }
    }

    public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);

    private void SetRunning(bool value)
    {
        if (_running == value) return;
        _running = value;
        Changed(nameof(IsRunning));
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));

    private static void ShowUnexpectedFailure(Exception exception)
    {
        var message = UserErrorHandler.GetMessage(exception, "Running this action");
        var application = Application.Current;
        if (application is null) return;

        void ShowMessage() => ThemedMessageBox.Show(application.MainWindow, message, SmartPosIdentity.Name,
            MessageBoxButton.OK, MessageBoxImage.Warning);

        if (application.Dispatcher.CheckAccess()) ShowMessage();
        else application.Dispatcher.Invoke(ShowMessage);
    }
}
