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
/// A compact line + area trend chart (sparkline) for stat cards. Dependency-free and theme-aware,
/// with a hover tooltip per point and an optional <see cref="PointClicked"/> drill-down.
/// </summary>
public sealed class Sparkline : FrameworkElement
{
    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource), typeof(IEnumerable), typeof(Sparkline),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnItemsSourceChanged));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(Sparkline),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty IsInteractiveProperty = DependencyProperty.Register(
        nameof(IsInteractive), typeof(bool), typeof(Sparkline), new FrameworkPropertyMetadata(false));

    public IEnumerable? ItemsSource { get => (IEnumerable?)GetValue(ItemsSourceProperty); set => SetValue(ItemsSourceProperty, value); }
    public Brush? Stroke { get => (Brush?)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
    public bool IsInteractive { get => (bool)GetValue(IsInteractiveProperty); set => SetValue(IsInteractiveProperty, value); }

    public event EventHandler<IChartColumn>? PointClicked;

    private readonly List<Point> _points = [];
    private List<IChartColumn> _items = [];
    private int _hover = -1;

    public Sparkline()
    {
        SizeChanged += (_, _) => InvalidateVisual();
        Loaded += (_, _) => AppTheme.ThemeChanged += OnThemeChanged;
        Unloaded += (_, _) => AppTheme.ThemeChanged -= OnThemeChanged;
    }

    private void OnThemeChanged() => InvalidateVisual();

    private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var chart = (Sparkline)d;
        if (e.OldValue is INotifyCollectionChanged oldItems) oldItems.CollectionChanged -= chart.OnCollectionChanged;
        if (e.NewValue is INotifyCollectionChanged newItems) newItems.CollectionChanged += chart.OnCollectionChanged;
        chart._hover = -1;
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) { _hover = -1; InvalidateVisual(); }

    private Brush Resolve(string key, Brush? fallback) => (TryFindResource(key) as Brush) ?? fallback ?? Brushes.Gray;

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_points.Count == 0) return;
        var x = e.GetPosition(this).X;
        int nearest = 0; double best = double.MaxValue;
        for (int i = 0; i < _points.Count; i++)
        {
            var d = Math.Abs(_points[i].X - x);
            if (d < best) { best = d; nearest = i; }
        }
        if (nearest != _hover)
        {
            _hover = nearest;
            Cursor = IsInteractive ? Cursors.Hand : Cursors.Arrow;
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
        if (IsInteractive && _hover >= 0 && _hover < _items.Count) PointClicked?.Invoke(this, _items[_hover]);
    }

    protected override void OnRender(DrawingContext dc)
    {
        _points.Clear();
        double w = ActualWidth, h = ActualHeight;
        if (w <= 2 || h <= 2) return;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h)); // hit-test surface
        _items = ItemsSource?.OfType<IChartColumn>().ToList() ?? [];
        if (_items.Count < 2) return;

        var stroke = Stroke ?? Resolve("Accent", Brushes.SteelBlue);
        const double pad = 3;
        double plotW = w - pad * 2, plotH = h - pad * 2;
        double step = plotW / (_items.Count - 1);
        for (int i = 0; i < _items.Count; i++)
        {
            double frac = Math.Clamp(_items[i].Magnitude / (_items[i].Scale > 0 ? _items[i].Scale : 1), 0, 1);
            _points.Add(new Point(pad + step * i, pad + (1 - frac) * plotH));
        }

        var figure = new PathFigure { StartPoint = _points[0] };
        for (int i = 1; i < _points.Count; i++) figure.Segments.Add(new LineSegment(_points[i], true));
        var line = new PathGeometry([figure]);

        var areaFigure = figure.Clone();
        areaFigure.Segments.Add(new LineSegment(new Point(_points[^1].X, h - pad), true));
        areaFigure.Segments.Add(new LineSegment(new Point(pad, h - pad), true));
        areaFigure.IsClosed = true;
        var fill = stroke.Clone(); fill.Opacity = 0.14; fill.Freeze();
        dc.DrawGeometry(fill, null, new PathGeometry([areaFigure]));

        var pen = new Pen(stroke, 1.8) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        dc.DrawGeometry(null, pen, line);
        dc.DrawEllipse(stroke, null, _points[^1], 2.6, 2.6);

        if (_hover >= 0 && _hover < _points.Count)
        {
            var cardBrush = Resolve("CardBackground", Brushes.White);
            dc.DrawEllipse(cardBrush, new Pen(stroke, 2), _points[_hover], 3.4, 3.4);
            DrawTooltip(dc, _items[_hover], _points[_hover], w);
        }
    }

    private void DrawTooltip(DrawingContext dc, IChartColumn item, Point at, double w)
    {
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var typeface = new Typeface("Segoe UI");
        var cardBrush = Resolve("CardBackground", Brushes.White);
        var borderBrush = Resolve("Border", Brushes.Gray);
        var textBrush = Resolve("PrimaryText", Brushes.Black);
        var mutedBrush = Resolve("MutedText", Brushes.Gray);
        var title = new FormattedText(item.Label ?? "", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, 10, mutedBrush, dpi) { MaxLineCount = 1 };
        var value = new FormattedText(item.Display ?? "", CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(typeface.FontFamily, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal), 12, textBrush, dpi) { MaxLineCount = 1 };
        double padX = 7, padY = 4, gap = 1;
        double boxW = Math.Max(title.Width, value.Width) + padX * 2;
        double boxH = title.Height + value.Height + gap + padY * 2;
        double bx = Math.Clamp(at.X - boxW / 2, 1, Math.Max(1, w - boxW - 1));
        double by = Math.Max(1, at.Y - boxH - 6);
        var box = new Rect(bx, by, boxW, boxH);
        dc.DrawRoundedRectangle(cardBrush, new Pen(borderBrush, 1), box, 6, 6);
        dc.DrawText(title, new Point(bx + padX, by + padY));
        dc.DrawText(value, new Point(bx + padX, by + padY + title.Height + gap));
    }
}
