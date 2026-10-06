using System.Windows;
using System.Windows.Automation;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Slate.Wpf;

/// <summary>Indeterminate busy indicator: a 2px ring in the inherited foreground, rotating 0.8s/turn.</summary>
public class Spinner : FrameworkElement
{
    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(
        nameof(Size), typeof(double), typeof(Spinner),
        new FrameworkPropertyMetadata(16.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(
        typeof(Spinner), new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));

    private readonly RotateTransform _rotation = new();

    public Spinner()
    {
        RenderTransform = _rotation;
        RenderTransformOrigin = new Point(0.5, 0.5);
        AutomationProperties.SetName(this, "Loading");
        IsVisibleChanged += (_, _) => UpdateAnimation();
        Loaded += (_, _) => UpdateAnimation();
        Unloaded += (_, _) => _rotation.BeginAnimation(RotateTransform.AngleProperty, null);
    }

    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    public Brush Foreground
    {
        get => (Brush)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    private void UpdateAnimation()
    {
        if (IsVisible && !SlateMotion.IsReduced)
        {
            _rotation.BeginAnimation(RotateTransform.AngleProperty,
                new DoubleAnimation(0, 360, TimeSpan.FromMilliseconds(800)) { RepeatBehavior = RepeatBehavior.Forever });
        }
        else
        {
            _rotation.BeginAnimation(RotateTransform.AngleProperty, null);
        }
    }

    protected override Size MeasureOverride(Size availableSize) => new(Size, Size);

    protected override void OnRender(DrawingContext dc)
    {
        const double stroke = 2;
        var r = (Size - stroke) / 2;
        var center = new Point(Size / 2, Size / 2);

        // Faint full track + a 270° arc in the foreground.
        var track = new Pen(Foreground, stroke) { };
        dc.PushOpacity(0.25);
        dc.DrawEllipse(null, track, center, r, r);
        dc.Pop();

        var start = new Point(center.X, center.Y - r);
        var end = new Point(center.X - r, center.Y);
        var arc = new StreamGeometry();
        using (var ctx = arc.Open())
        {
            ctx.BeginFigure(start, false, false);
            ctx.ArcTo(end, new Size(r, r), 0, true, SweepDirection.Clockwise, true, false);
        }
        arc.Freeze();
        dc.DrawGeometry(null, new Pen(Foreground, stroke) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, arc);
    }
}
