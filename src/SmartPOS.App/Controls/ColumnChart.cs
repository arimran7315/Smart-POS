using System.Collections;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using SmartPOS.App.ViewModels;

namespace SmartPOS.App.Controls;

/// <summary>
/// A lightweight, dependency-free vertical bar/column chart drawn with <see cref="OnRender"/>.
/// Binds to any <see cref="IEnumerable"/> of <see cref="IChartColumn"/> and follows the active theme.
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

    public IEnumerable? ItemsSource { get => (IEnumerable?)GetValue(ItemsSourceProperty); set => SetValue(ItemsSourceProperty, value); }
    public Brush? Fill { get => (Brush?)GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public Brush? NegativeFill { get => (Brush?)GetValue(NegativeFillProperty); set => SetValue(NegativeFillProperty, value); }
    public bool ShowValueLabels { get => (bool)GetValue(ShowValueLabelsProperty); set => SetValue(ShowValueLabelsProperty, value); }
    public bool ShowAxisLabels { get => (bool)GetValue(ShowAxisLabelsProperty); set => SetValue(ShowAxisLabelsProperty, value); }

    public ColumnChart() => SizeChanged += (_, _) => InvalidateVisual();

    private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var chart = (ColumnChart)d;
        if (e.OldValue is INotifyCollectionChanged oldItems) oldItems.CollectionChanged -= chart.OnCollectionChanged;
        if (e.NewValue is INotifyCollectionChanged newItems) newItems.CollectionChanged += chart.OnCollectionChanged;
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => InvalidateVisual();

    private Brush Resolve(string key, Brush? fallback) =>
        (TryFindResource(key) as Brush) ?? fallback ?? Brushes.Gray;

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 2 || h <= 2) return;

        var items = ItemsSource?.OfType<IChartColumn>().ToList() ?? [];
        var axisBrush = Resolve("Border", Brushes.LightGray);
        var mutedBrush = Resolve("MutedText", Brushes.Gray);
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

        // baseline
        var axisPen = new Pen(axisBrush, 1);
        dc.DrawLine(axisPen, new Point(leftPad, baseY + 0.5), new Point(w - rightPad, baseY + 0.5));

        bool labelEach = items.Count <= 16;
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            double fraction = item.Scale > 0 ? Math.Clamp(item.Magnitude / item.Scale, 0, 1) : 0;
            double barH = Math.Max(fraction > 0 ? 2 : 0, fraction * plotH);
            double x = leftPad + slot * i + (slot - barW) / 2;
            double y = baseY - barH;
            var rect = new Rect(x, y, barW, barH);
            var brush = item.IsNegative ? negBrush : barBrush;
            if (barH > 0) dc.DrawRoundedRectangle(brush, null, rect, 3, 3);

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
                dc.DrawText(lt, new Point(leftPad + slot * i + 1, baseY + 4));
            }
        }
    }
}
