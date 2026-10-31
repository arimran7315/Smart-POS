using System.Collections;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using SmartPOS.App.Services;
using SmartPOS.App.ViewModels;

namespace SmartPOS.App.Controls;

/// <summary>
/// A lightweight, dependency-free vertical bar/column chart drawn with <see cref="OnRender"/>.
/// Binds to any <see cref="IEnumerable"/> of <see cref="IChartColumn"/>, follows the active theme,
/// shows a hover tooltip per bar, and (when <see cref="IsInteractive"/>) raises <see cref="ColumnClicked"/>.
/// </summary>
public sealed class ColumnChart : FrameworkElement
{
    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource), typeof(IEnumerable), typeof(ColumnChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnItemsSourceChanged));

    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Brush), typeof(ColumnChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty NegativeFillProperty = DependencyProperty.Register(
        nameof(NegativeFill), typeof(Brush), typeof(ColumnChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ShowValueLabelsProperty = DependencyProperty.Register(
        nameof(ShowValueLabels), typeof(bool), typeof(ColumnChart),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ShowAxisLabelsProperty = DependencyProperty.Register(
        nameof(ShowAxisLabels), typeof(bool), typeof(ColumnChart),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty IsInteractiveProperty = DependencyProperty.Register(
        nameof(IsInteractive), typeof(bool), typeof(ColumnChart),
        new FrameworkPropertyMetadata(false));

    public IEnumerable? ItemsSource { get => (IEnumerable?)GetValue(ItemsSourceProperty); set => SetValue(ItemsSourceProperty, value); }
    public Brush? Fill { get => (Brush?)GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public Brush? NegativeFill { get => (Brush?)GetValue(NegativeFillProperty); set => SetValue(NegativeFillProperty, value); }
    public bool ShowValueLabels { get => (bool)GetValue(ShowValueLabelsProperty); set => SetValue(ShowValueLabelsProperty, value); }
    public bool ShowAxisLabels { get => (bool)GetValue(ShowAxisLabelsProperty); set => SetValue(ShowAxisLabelsProperty, value); }
    public bool IsInteractive { get => (bool)GetValue(IsInteractiveProperty); set => SetValue(IsInteractiveProperty, value); }

    /// <summary>Raised when a bar is clicked and <see cref="IsInteractive"/> is true.</summary>
    public event EventHandler<IChartColumn>? ColumnClicked;

    private readonly List<(Rect bar, double slotX, double slotW, IChartColumn item)> _layout = [];
    private int _hover = -1;

    public ColumnChart()
    {
        SizeChanged += (_, _) => InvalidateVisual();
        Loaded += (_, _) => AppTheme.ThemeChanged += OnThemeChanged;
        Unloaded += (_, _) => AppTheme.ThemeChanged -= OnThemeChanged;
    }

    private void OnThemeChanged() => InvalidateVisual();

    private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var chart = (ColumnChart)d;
        if (e.OldValue is INotifyCollectionChanged oldItems) oldItems.CollectionChanged -= chart.OnCollectionChanged;
        if (e.NewValue is INotifyCollectionChanged newItems) newItems.CollectionChanged += chart.OnCollectionChanged;
        chart._hover = -1;
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) { _hover = -1; InvalidateVisual(); }

    private Brush Resolve(string key, Brush? fallback) => (TryFindResource(key) as Brush) ?? fallback ?? Brushes.Gray;

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_layout.Count == 0) return;
        var x = e.GetPosition(this).X;
        int hit = -1;
        for (int i = 0; i < _layout.Count; i++)
        {
            var l = _layout[i];
            if (x >= l.slotX && x < l.slotX + l.slotW) { hit = i; break; }
        }
        if (hit != _hover)
        {
            _hover = hit;
            Cursor = IsInteractive && hit >= 0 ? Cursors.Hand : Cursors.Arrow;
            InvalidateVisual();
        }
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hover != -1) { _hover = -1; Cursor = Cursors.Arrow; InvalidateVisual(); }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (IsInteractive && _hover >= 0 && _hover < _layout.Count)
            ColumnClicked?.Invoke(this, _layout[_hover].item);
    }

    protected override void OnRender(DrawingContext dc)
    {
        _layout.Clear();
        double w = ActualWidth, h = ActualHeight;
        if (w <= 2 || h <= 2) return;

        // Transparent backdrop so the whole surface is hit-testable for hover.
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));

        var items = ItemsSource?.OfType<IChartColumn>().ToList() ?? [];
        var axisBrush = Resolve("Border", Brushes.LightGray);
        var mutedBrush = Resolve("MutedText", Brushes.Gray);
        var textBrush = Resolve("PrimaryText", Brushes.Black);
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var typeface = new Typeface("Segoe UI");

        if (items.Count == 0)
        {
            var empty = new FormattedText("No data for this range", CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                typeface, 13, mutedBrush, dpi);
            dc.DrawText(empty, new Point((w - empty.Width) / 2, (h - empty.Height) / 2));
            return;
        }

        var barBrush = Fill ?? Resolve("Accent", Brushes.SteelBlue);
        var negBrush = NegativeFill ?? Resolve("Warning", Brushes.OrangeRed);

        const double leftPad = 6, rightPad = 6;
        double topPad = ShowValueLabels ? 20 : 8;
        double bottomPad = ShowAxisLabels ? 22 : 6;
        double plotW = w - leftPad - rightPad;
        double plotH = h - topPad - bottomPad;
        if (plotH <= 4 || plotW <= 4) return;

        double baseY = topPad + plotH;
        double slot = plotW / items.Count;
        double barW = Math.Min(46, slot * 0.62);
        bool labelEach = items.Count <= 16;

        // hovered guide column (drawn behind bars)
        if (_hover >= 0 && _hover < items.Count)
        {
            var hoverBrush = Resolve("NavHover", Brushes.Gainsboro);
            dc.DrawRectangle(hoverBrush, null, new Rect(leftPad + slot * _hover, topPad, slot, plotH));
        }

        var axisPen = new Pen(axisBrush, 1);
        dc.DrawLine(axisPen, new Point(leftPad, baseY + 0.5), new Point(w - rightPad, baseY + 0.5));

        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            double fraction = item.Scale > 0 ? Math.Clamp(item.Magnitude / item.Scale, 0, 1) : 0;
            double barH = Math.Max(fraction > 0 ? 2 : 0, fraction * plotH);
            double slotX = leftPad + slot * i;
            double x = slotX + (slot - barW) / 2;
            double y = baseY - barH;
            var rect = new Rect(x, y, barW, barH);
            _layout.Add((rect, slotX, slot, item));

            var brush = item.IsNegative ? negBrush : barBrush;
            if (barH > 0) dc.DrawRoundedRectangle(brush, null, rect, 3, 3);
            if (i == _hover && barH > 0)
                dc.DrawRoundedRectangle(null, new Pen(textBrush, 1.3), rect, 3, 3);

            if (ShowValueLabels && labelEach && !string.IsNullOrEmpty(item.Display))
            {
                var vt = new FormattedText(item.Display, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                    typeface, 10, mutedBrush, dpi) { TextAlignment = TextAlignment.Center, MaxTextWidth = Math.Max(barW + slot * 0.3, 24), MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis };
                dc.DrawText(vt, new Point(x + barW / 2 - vt.MaxTextWidth / 2, Math.Max(2, y - vt.Height - 2)));
            }

            if (ShowAxisLabels && labelEach && !string.IsNullOrEmpty(item.Label))
            {
                var lt = new FormattedText(item.Label, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                    typeface, 10, mutedBrush, dpi) { TextAlignment = TextAlignment.Center, MaxTextWidth = slot - 2, MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis };
                dc.DrawText(lt, new Point(slotX + 1, baseY + 4));
            }
        }

        if (_hover >= 0 && _hover < items.Count)
            DrawTooltip(dc, items[_hover], _layout[_hover].bar, w, typeface, dpi, textBrush, mutedBrush);
    }

    private void DrawTooltip(DrawingContext dc, IChartColumn item, Rect bar, double w, Typeface typeface, double dpi, Brush textBrush, Brush mutedBrush)
    {
        var cardBrush = Resolve("CardBackground", Brushes.White);
        var borderBrush = Resolve("Border", Brushes.Gray);
        var title = new FormattedText(item.Label ?? "", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, 11, mutedBrush, dpi) { MaxLineCount = 1 };
        var value = new FormattedText(item.Display ?? "", CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(typeface.FontFamily, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal), 13, textBrush, dpi) { MaxLineCount = 1 };
        double padX = 9, padY = 6, gap = 2;
        double boxW = Math.Max(title.Width, value.Width) + padX * 2;
        double boxH = title.Height + value.Height + gap + padY * 2;
        double cx = bar.X + bar.Width / 2;
        double bx = Math.Clamp(cx - boxW / 2, 2, Math.Max(2, w - boxW - 2));
        double by = Math.Max(2, bar.Y - boxH - 6);
        var box = new Rect(bx, by, boxW, boxH);
        dc.DrawRoundedRectangle(cardBrush, new Pen(borderBrush, 1), box, 7, 7);
        dc.DrawText(title, new Point(bx + padX, by + padY));
        dc.DrawText(value, new Point(bx + padX, by + padY + title.Height + gap));
    }
}
