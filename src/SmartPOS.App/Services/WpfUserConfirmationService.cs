using System.Windows;

namespace SmartPOS.App.Services;

public sealed class WpfUserConfirmationService(Window owner) : IUserConfirmationService
{
    public Task<bool> ConfirmAsync(string title, string message, ConfirmationKind kind = ConfirmationKind.Question)
    {
        var image = kind == ConfirmationKind.Warning ? MessageBoxImage.Warning : MessageBoxImage.Question;
        return Task.FromResult(MessageBox.Show(owner, message, title, MessageBoxButton.YesNo, image) == MessageBoxResult.Yes);
    }
}
