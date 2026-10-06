using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace Slate.Wpf;

/// <summary>
/// Draws an icon from the shared Slate set (design/icons) as 2px round strokes in the inherited foreground.
/// <c>&lt;sl:Icon Kind="search" /&gt;</c>. Decorative unless <see cref="System.Windows.Automation.AutomationProperties.NameProperty"/> is set.
/// </summary>
public class Icon : FrameworkElement
{
    private static readonly ConcurrentDictionary<string, Geometry> Cache = new();

    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind), typeof(string), typeof(Icon),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty DataProperty = DependencyProperty.Register(
        nameof(Data), typeof(Geometry), typeof(Icon),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(
        nameof(Size), typeof(double), typeof(Icon),
        new FrameworkPropertyMetadata(16.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Stroke width in device-independent pixels at the rendered size. NaN (default) keeps the set's
    /// 2-unit stroke on the 24-unit canvas, i.e. it scales with <see cref="Size"/>.</summary>
    public static readonly DependencyProperty StrokeThicknessProperty = DependencyProperty.Register(
        nameof(StrokeThickness), typeof(double), typeof(Icon),
        new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(
        typeof(Icon), new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));

    static Icon()
    {
        FocusableProperty.OverrideMetadata(typeof(Icon), new FrameworkPropertyMetadata(false));
        SnapsToDevicePixelsProperty.OverrideMetadata(typeof(Icon), new FrameworkPropertyMetadata(true));
    }

    /// <summary>Kebab-case icon name, e.g. "chevron-down". See <see cref="SlateIcons.All"/>.</summary>
    public string? Kind
    {
        get => (string?)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    /// <summary>Custom geometry on the same 24-unit canvas; overrides <see cref="Kind"/>.</summary>
    public Geometry? Data
    {
        get => (Geometry?)GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    public double StrokeThickness
    {
        get => (double)GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    public Brush Foreground
    {
        get => (Brush)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    /// <summary>Parses (and caches, frozen) the geometry for a Slate icon name; null if unknown.</summary>
    public static Geometry? GeometryFor(string? name)
    {
        if (string.IsNullOrEmpty(name) || !SlateIcons.All.TryGetValue(name, out var data))
            return null;
        return Cache.GetOrAdd(name, _ =>
        {
            var g = Geometry.Parse(data);
            g.Freeze();
            return g;
        });
    }

    private Geometry? Resolved => Data ?? GeometryFor(Kind);

    protected override Size MeasureOverride(Size availableSize) =>
        Resolved is null ? new Size(0, 0) : new Size(Size, Size);

    protected override void OnRender(DrawingContext dc)
    {
        if (Resolved is not { } geometry)
            return;

        var scale = Size / SlateIcons.ViewBox;
        var thickness = double.IsNaN(StrokeThickness) ? SlateIcons.StrokeWidth : StrokeThickness / scale;
        var pen = new Pen(Foreground, thickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };
        dc.PushTransform(new ScaleTransform(scale, scale));
        dc.DrawGeometry(null, pen, geometry);
        dc.Pop();
    }
}
