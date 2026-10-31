using System.Windows;
using System.Windows.Media;

namespace SmartPOS.App.Services;

public static class AppTheme
{
    /// <summary>True when the dark palette is currently applied. Updated by <see cref="Apply"/>.</summary>
    public static bool IsDark { get; private set; }

    /// <summary>The canonical name ("Light" or "Dark") of the active palette.</summary>
    public static string CurrentTheme => IsDark ? "Dark" : "Light";

    /// <summary>Raised after a new palette is applied so owner-drawn visuals (charts) can repaint.</summary>
    public static event Action? ThemeChanged;

    public static void Apply(Application application, string? theme)
    {
        var dark = string.Equals(theme?.Trim(), "Dark", StringComparison.OrdinalIgnoreCase);
        IsDark = dark;
        var palette = dark
            ? new Dictionary<string, uint>
            {
                ["WindowBackground"] = 0x171C22, ["CardBackground"] = 0x222A33,
                ["PrimaryText"] = 0xE7EDF3, ["SecondaryText"] = 0xB2C0CD, ["MutedText"] = 0x9AA9B7,
                ["Accent"] = 0x2B91B3, ["Border"] = 0x46515E, ["Danger"] = 0xE27676,
                ["Warning"] = 0xE9B45D, ["Success"] = 0x62C28B, ["SuccessMuted"] = 0x70B88A,
                ["SidebarBackground"] = 0x1C232B, ["AccentSoft"] = 0x203A44, ["NavHover"] = 0x2A333D,
                ["NavActive"] = 0x274853, ["DangerSoft"] = 0x3C2828, ["WarningSoft"] = 0x3C3320,
                ["SuccessSoft"] = 0x23372C, ["Shadow"] = 0x000000
            }
            : new Dictionary<string, uint>
            {
                ["WindowBackground"] = 0xF3F6F8, ["CardBackground"] = 0xFFFFFF,
                ["PrimaryText"] = 0x182230, ["SecondaryText"] = 0x526272, ["MutedText"] = 0x64748B,
                ["Accent"] = 0x176B87, ["Border"] = 0xCBD5E1, ["Danger"] = 0xA23D3D,
                ["Warning"] = 0xB45309, ["Success"] = 0x2E8B57, ["SuccessMuted"] = 0x5A8B5A,
                ["SidebarBackground"] = 0xFFFFFF, ["AccentSoft"] = 0xDCEBF0, ["NavHover"] = 0xEAF1F4,
                ["NavActive"] = 0xCDE4EB, ["DangerSoft"] = 0xF7E6E6, ["WarningSoft"] = 0xF9EEDD,
                ["SuccessSoft"] = 0xE1F1E8, ["Shadow"] = 0x334155
            };

        foreach (var (key, value) in palette)
        {
            var color = Color.FromRgb((byte)(value >> 16), (byte)(value >> 8), (byte)value);
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            application.Resources[key] = brush;
        }

        ThemeChanged?.Invoke();
    }
}
