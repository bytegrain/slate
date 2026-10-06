using System.Windows;
using System.Windows.Automation;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Slate.Wpf;

/// <summary>
/// Indeterminate busy indicator: a 2px ring rotating 0.8s/turn (docs/design/components.md#progress).
/// Colour: the inherited foreground for Neutral, else the tone's text colour.
/// </summary>
public class Spinner : FrameworkElement
{
    public static readonly DependencyProperty SizeProperty = Sl.SizeProperty.AddOwner(typeof(Spinner),
        new FrameworkPropertyMetadata(ControlSize.Medium, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ToneProperty = Sl.ToneProperty.AddOwner(typeof(Spinner));
    public static readonly DependencyProperty LabelProperty = Sl.LabelProperty.AddOwner(typeof(Spinner));

    /// <summary>Exact diameter in px; NaN (default) derives it from <see cref="Size"/> (12 / 16 / 24).</summary>
    public static readonly DependencyProperty DiameterProperty = DependencyProperty.Register(
        nameof(Diameter), typeof(double), typeof(Spinner),
        new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

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
        Sl.SetKind(this, SlKind.Spinner);
    }

    public ControlSize Size { get => (ControlSize)GetValue(SizeProperty); set => SetValue(SizeProperty, value); }
    public Tone Tone { get => (Tone)GetValue(ToneProperty); set => SetValue(ToneProperty, value); }

    /// <summary>Accessible name (default "Loading").</summary>
    public string? Label { get => (string?)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }

    public double Diameter { get => (double)GetValue(DiameterProperty); set => SetValue(DiameterProperty, value); }

    public Brush Foreground { get => (Brush)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }

    /// <summary>Diameter actually drawn.</summary>
    public double ActualDiameter => !double.IsNaN(Diameter) ? Diameter : Sl.GetActualSize(this) switch
    {
        ControlSize.Small => 12,
        ControlSize.Large => 24,
        _ => 16,
    };

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == Sl.ChromeForegroundProperty || e.Property == Sl.ActualSizeProperty)
        {
            InvalidateMeasure();
            InvalidateVisual();
        }
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

    protected override Size MeasureOverride(Size availableSize) => new(ActualDiameter, ActualDiameter);

    protected override void OnRender(DrawingContext dc)
    {
        const double stroke = 2;
        var size = ActualDiameter;
        var brush = Sl.GetChromeForeground(this) ?? Foreground;
        var r = (size - stroke) / 2;
        var center = new Point(size / 2, size / 2);

        // Faint full track + a 270° arc.
        dc.PushOpacity(0.25);
        dc.DrawEllipse(null, new Pen(brush, stroke), center, r, r);
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
        dc.DrawGeometry(null, new Pen(brush, stroke) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, arc);
    }
}
