using System.Linq;
using System.Windows;
using SmartPOS.App.Views;

namespace SmartPOS.App.Services;

/// <summary>
/// Drop-in replacement for <see cref="MessageBox"/> that renders a themed, owner-centred
/// dialog honouring the active light/dark palette. Signatures mirror the WPF overloads in use.
/// </summary>
public static class ThemedMessageBox
{
    public static MessageBoxResult Show(string message)
        => Show(null, message, string.Empty, MessageBoxButton.OK, MessageBoxImage.None);

    public static MessageBoxResult Show(string message, string title)
        => Show(null, message, title, MessageBoxButton.OK, MessageBoxImage.None);

    public static MessageBoxResult Show(string message, string title, MessageBoxButton button, MessageBoxImage image)
        => Show(null, message, title, button, image);

    public static MessageBoxResult Show(Window? owner, string message, string title, MessageBoxButton button, MessageBoxImage image)
    {
        var application = Application.Current;
        if (application is null)
            return MessageBox.Show(message, title, button, image); // no WPF app context (e.g. tests)

        if (!application.Dispatcher.CheckAccess())
            return application.Dispatcher.Invoke(() => Show(owner, message, title, button, image));

        var dialog = new ThemedDialogWindow(message, title, button, image);
        var host = ResolveOwner(owner, application);
        if (host is not null && !ReferenceEquals(host, dialog) && host.IsVisible)
            dialog.Owner = host;
        else
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;

        dialog.ShowDialog();
        return dialog.Result;
    }

    private static Window? ResolveOwner(Window? owner, Application application)
    {
        if (owner is not null) return owner;
        return application.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive && w.IsVisible)
            ?? application.MainWindow;
    }
}
