using System.Collections;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using SmartPOS.App.ViewModels;

namespace SmartPOS.App.Controls;

/// <summary>
/// A compact line + area trend chart (sparkline) for stat cards. Dependency-free and theme-aware.
/// </summary>
public sealed class Sparkline : FrameworkElement
{
    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource), typeof(IEnumerable), typeof(Sparkline),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnItemsSourceChanged));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(Sparkline),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public IEnumerable? ItemsSource { get => (IEnumerable?)GetValue(ItemsSourceProperty); set => SetValue(ItemsSourceProperty, value); }
    public Brush? Stroke { get => (Brush?)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }

    public Sparkline() => SizeChanged += (_, _) => InvalidateVisual();

    private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var chart = (Sparkline)d;
        if (e.OldValue is INotifyCollectionChanged oldItems) oldItems.CollectionChanged -= chart.OnCollectionChanged;
        if (e.NewValue is INotifyCollectionChanged newItems) newItems.CollectionChanged += chart.OnCollectionChanged;
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => InvalidateVisual();

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 2 || h <= 2) return;
        var points = ItemsSource?.OfType<IChartColumn>().ToList() ?? [];
        if (points.Count < 2) return;

        var stroke = Stroke ?? (TryFindResource("Accent") as Brush) ?? Brushes.SteelBlue;
        const double pad = 3;
        double plotW = w - pad * 2, plotH = h - pad * 2;
        double step = plotW / (points.Count - 1);

        var figure = new PathFigure { StartPoint = PointAt(0) };
        for (int i = 1; i < points.Count; i++) figure.Segments.Add(new LineSegment(PointAt(i), true));
        figure.Freeze();
        var line = new PathGeometry([figure]);

        // area under the line
        var areaFigure = figure.Clone();
        areaFigure.Segments.Add(new LineSegment(new Point(pad + step * (points.Count - 1), h - pad), true));
        areaFigure.Segments.Add(new LineSegment(new Point(pad, h - pad), true));
        areaFigure.IsClosed = true;
        var area = new PathGeometry([areaFigure]);
        var fill = stroke.Clone(); fill.Opacity = 0.14; fill.Freeze();
        dc.DrawGeometry(fill, null, area);

        var pen = new Pen(stroke, 1.8) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        dc.DrawGeometry(null, pen, line);

        // highlight the last point
        var last = PointAt(points.Count - 1);
        dc.DrawEllipse(stroke, null, last, 2.6, 2.6);

        Point PointAt(int i)
        {
            double frac = Math.Clamp(points[i].Magnitude / (points[i].Scale > 0 ? points[i].Scale : 1), 0, 1);
            return new Point(pad + step * i, pad + (1 - frac) * plotH);
        }
    }
}
