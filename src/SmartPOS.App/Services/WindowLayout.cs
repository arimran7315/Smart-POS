using System.Windows;

namespace SmartPOS.App.Services;

public static class WindowLayout
{
    public static void FitToWorkArea(Window window, double widthFraction = 0.96, double heightFraction = 0.94)
    {
        ArgumentNullException.ThrowIfNull(window);
        var area = SystemParameters.WorkArea;
        window.MinWidth = Math.Min(window.MinWidth, area.Width);
        window.MinHeight = Math.Min(window.MinHeight, area.Height);
        window.Width = Math.Min(window.Width, area.Width * widthFraction);
        window.Height = Math.Min(window.Height, area.Height * heightFraction);
    }
}
