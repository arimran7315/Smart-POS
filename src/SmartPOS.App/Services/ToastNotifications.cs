using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

namespace SmartPOS.App.Services;

public static class ToastNotifications
{
    public static void Show(Window owner, string message, ToastKind kind = ToastKind.Success,
        TimeSpan? duration = null)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        if (owner.Content is not UIElement target || !owner.IsLoaded) return;

        var brushKey = kind switch
        {
            ToastKind.Error => "Danger",
            ToastKind.Information => "Accent",
            _ => "Success"
        };
        var background = Application.Current.TryFindResource(brushKey) as Brush
            ?? new SolidColorBrush(Color.FromRgb(46, 139, 87));
        var popup = new Popup
        {
            AllowsTransparency = true,
            Child = new Border
            {
                Background = background,
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(16, 12, 16, 12),
                MaxWidth = 440,
                Child = new TextBlock
                {
                    Text = message,
                    Foreground = Brushes.White,
                    FontSize = 14,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 408
                }
            },
            PlacementTarget = target,
            Placement = PlacementMode.Relative,
            HorizontalOffset = Math.Max(16, owner.ActualWidth - 380),
            VerticalOffset = 16,
            StaysOpen = true
        };
        var timer = new DispatcherTimer(DispatcherPriority.Background, owner.Dispatcher)
        {
            Interval = duration ?? TimeSpan.FromSeconds(3.5)
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            popup.IsOpen = false;
        };
        popup.Closed += (_, _) => timer.Stop();
        popup.IsOpen = true;
        timer.Start();
    }
}

public enum ToastKind
{
    Success,
    Information,
    Error
}
