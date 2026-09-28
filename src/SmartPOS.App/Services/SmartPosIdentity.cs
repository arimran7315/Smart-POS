using System.Windows;

namespace SmartPOS.App.Services;

public static class SmartPosIdentity
{
    public static string Name
    {
        get
        {
            var configured = Application.Current?.Resources["AppName"] as string;
            return string.IsNullOrWhiteSpace(configured) ? "SmartPOS" : configured;
        }
    }
}
