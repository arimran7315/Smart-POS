using System.Windows;
using System.Windows.Media;

namespace SmartPOS.App.Services;

public static class AppTheme
{
    public static void Apply(Application application, string? theme)
    {
        var dark = string.Equals(theme?.Trim(), "Dark", StringComparison.OrdinalIgnoreCase);
        var palette = dark
            ? new Dictionary<string, uint>
            {
                ["WindowBackground"] = 0x171C22, ["CardBackground"] = 0x222A33,
                ["PrimaryText"] = 0xE7EDF3, ["SecondaryText"] = 0xB2C0CD, ["MutedText"] = 0x9AA9B7,
                ["Accent"] = 0x2B91B3, ["Border"] = 0x46515E, ["Danger"] = 0xE27676,
                ["Warning"] = 0xE9B45D, ["Success"] = 0x62C28B, ["SuccessMuted"] = 0x70B88A
            }
            : new Dictionary<string, uint>
            {
                ["WindowBackground"] = 0xF3F6F8, ["CardBackground"] = 0xFFFFFF,
                ["PrimaryText"] = 0x182230, ["SecondaryText"] = 0x526272, ["MutedText"] = 0x64748B,
                ["Accent"] = 0x176B87, ["Border"] = 0xCBD5E1, ["Danger"] = 0xA23D3D,
                ["Warning"] = 0xB45309, ["Success"] = 0x2E8B57, ["SuccessMuted"] = 0x5A8B5A
            };

        foreach (var (key, value) in palette)
        {
            var color = Color.FromRgb((byte)(value >> 16), (byte)(value >> 8), (byte)value);
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            application.Resources[key] = brush;
        }
    }
}
