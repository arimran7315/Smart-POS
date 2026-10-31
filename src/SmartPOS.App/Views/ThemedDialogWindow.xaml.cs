using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace SmartPOS.App.Views;

public partial class ThemedDialogWindow : Window
{
    public MessageBoxResult Result { get; private set; } = MessageBoxResult.None;

    public ThemedDialogWindow(string message, string title, MessageBoxButton button, MessageBoxImage image)
    {
        InitializeComponent();
        TitleText.Text = string.IsNullOrWhiteSpace(title) ? Services.SmartPosIdentity.Name : title;
        MessageText.Text = message ?? string.Empty;
        ApplyKind(image);
        ApplyButtons(button);
        HeaderArea.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
    }

    private void ApplyKind(MessageBoxImage image)
    {
        // Segoe MDL2 Assets glyphs, tinted with the active theme palette.
        (string glyph, string foreground, string background) = image switch
        {
            MessageBoxImage.Error    => ("", "Danger", "DangerSoft"),
            MessageBoxImage.Warning  => ("", "Warning", "WarningSoft"),
            MessageBoxImage.Information => ("", "Accent", "AccentSoft"),
            MessageBoxImage.Question => ("", "Accent", "AccentSoft"),
            _ => ("", "Accent", "AccentSoft")
        };
        IconGlyph.Text = glyph;
        if (TryFindResource(foreground) is Brush fg) IconGlyph.Foreground = fg;
        if (TryFindResource(background) is Brush bg) IconCircle.Background = bg;
    }

    private void ApplyButtons(MessageBoxButton button)
    {
        switch (button)
        {
            case MessageBoxButton.OK:
                SetPrimary("OK", MessageBoxResult.OK);
                break;
            case MessageBoxButton.OKCancel:
                SetPrimary("OK", MessageBoxResult.OK);
                SetSecondary("Cancel", MessageBoxResult.Cancel);
                break;
            case MessageBoxButton.YesNo:
                SetPrimary("Yes", MessageBoxResult.Yes);
                SetSecondary("No", MessageBoxResult.No);
                break;
            case MessageBoxButton.YesNoCancel:
                SetPrimary("Yes", MessageBoxResult.Yes);
                SetSecondary("No", MessageBoxResult.No);
                SetTertiary("Cancel", MessageBoxResult.Cancel);
                break;
        }
    }

    private MessageBoxResult _primaryResult = MessageBoxResult.OK;
    private MessageBoxResult _secondaryResult = MessageBoxResult.Cancel;
    private MessageBoxResult _tertiaryResult = MessageBoxResult.Cancel;
    private bool _hasSecondary;

    private void SetPrimary(string text, MessageBoxResult result) { PrimaryBtn.Content = text; _primaryResult = result; }
    private void SetSecondary(string text, MessageBoxResult result) { SecondaryBtn.Content = text; SecondaryBtn.Visibility = Visibility.Visible; _secondaryResult = result; _hasSecondary = true; }
    private void SetTertiary(string text, MessageBoxResult result) { TertiaryBtn.Content = text; TertiaryBtn.Visibility = Visibility.Visible; _tertiaryResult = result; }

    private void Primary_Click(object sender, RoutedEventArgs e) => Finish(_primaryResult);
    private void Secondary_Click(object sender, RoutedEventArgs e) => Finish(_secondaryResult);
    private void Tertiary_Click(object sender, RoutedEventArgs e) => Finish(_tertiaryResult);

    private void Finish(MessageBoxResult result)
    {
        Result = result;
        DialogResult = result is MessageBoxResult.OK or MessageBoxResult.Yes;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { Finish(_primaryResult); e.Handled = true; }
        else if (e.Key == Key.Escape) { Finish(_hasSecondary ? _secondaryResult : _primaryResult); e.Handled = true; }
        base.OnKeyDown(e);
    }
}
