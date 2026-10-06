using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace Slate.Avalonia.Controls;

/// <summary>
/// A built-in stroke icon from <see cref="SlateIcons"/> (design/icons/icons.json). Draws in the inherited
/// foreground colour with round 2px strokes on a 24px grid, scaled to <see cref="Size"/>. Decorative unless
/// <see cref="AutomationProperties.NameProperty"/> is set.
/// </summary>
public class Icon : Control
{
    public static readonly StyledProperty<string?> KindProperty =
        AvaloniaProperty.Register<Icon, string?>(nameof(Kind));

    public static readonly StyledProperty<double> SizeProperty =
        AvaloniaProperty.Register<Icon, double>(nameof(Size), 16);

    /// <summary>Stroke width in the icon's own 24px units.</summary>
    public static readonly StyledProperty<double> StrokeThicknessProperty =
        AvaloniaProperty.Register<Icon, double>(nameof(StrokeThickness), SlateIcons.StrokeWidth);

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner<Icon>();

    private Geometry? _geometry;

    static Icon()
    {
        AffectsMeasure<Icon>(SizeProperty, KindProperty);
        AffectsRender<Icon>(KindProperty, ForegroundProperty, StrokeThicknessProperty, SizeProperty);
        KindProperty.Changed.AddClassHandler<Icon>((i, _) => i.UpdateGeometry());
        FocusableProperty.OverrideDefaultValue<Icon>(false);
        HorizontalAlignmentProperty.OverrideDefaultValue<Icon>(global::Avalonia.Layout.HorizontalAlignment.Center);
        VerticalAlignmentProperty.OverrideDefaultValue<Icon>(global::Avalonia.Layout.VerticalAlignment.Center);
    }

    public Icon() => PseudoClasses.Set(":empty", true);

    public string? Kind { get => GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public double Size { get => GetValue(SizeProperty); set => SetValue(SizeProperty, value); }
    public double StrokeThickness { get => GetValue(StrokeThicknessProperty); set => SetValue(StrokeThicknessProperty, value); }
    public IBrush? Foreground { get => GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }

    /// <summary>True when <see cref="Kind"/> names a known icon.</summary>
    public bool HasIcon => _geometry is not null;

    internal Geometry? Geometry => _geometry;

    private void UpdateGeometry()
    {
        // An unknown/unset icon measures to zero, so templates can always include one.
        _geometry = Kind is { Length: > 0 } k && SlateIcons.All.TryGetValue(k, out var data) ? StreamGeometry.Parse(data) : null;
        PseudoClasses.Set(":empty", _geometry is null);
    }

    protected override Size MeasureOverride(Size availableSize) => _geometry is null ? default : new Size(Size, Size);

    public override void Render(DrawingContext context)
    {
        if (_geometry is null || Foreground is null)
            return;

        var scale = Size / SlateIcons.ViewBox;
        var pen = new Pen(Foreground, StrokeThickness, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        using (context.PushTransform(Matrix.CreateScale(scale, scale)))
            context.DrawGeometry(null, pen, _geometry);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new IconAutomationPeer(this);

    private sealed class IconAutomationPeer(Icon owner) : ControlAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Image;
        protected override bool IsContentElementCore() => !string.IsNullOrEmpty(AutomationProperties.GetName(Owner));
        protected override bool IsControlElementCore() => !string.IsNullOrEmpty(AutomationProperties.GetName(Owner));
    }
}

/// <summary>Indeterminate busy indicator: a 2px ring in the current foreground, rotating unless motion is reduced.</summary>
public class LoadingSpinner : TemplatedControl
{
    public static readonly StyledProperty<double> SizeProperty =
        AvaloniaProperty.Register<LoadingSpinner, double>(nameof(Size), 16);

    public double Size { get => GetValue(SizeProperty); set => SetValue(SizeProperty, value); }

    static LoadingSpinner()
    {
        FocusableProperty.OverrideDefaultValue<LoadingSpinner>(false);
        AutomationProperties.NameProperty.OverrideDefaultValue<LoadingSpinner>("Loading");
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new LoadingSpinnerPeer(this);

    private sealed class LoadingSpinnerPeer(LoadingSpinner owner) : ControlAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.ProgressBar;
    }
}

/// <summary>Keyboard shortcut hint, e.g. <c>&lt;sl:Kbd Text="⌘K"/&gt;</c>.</summary>
public class Kbd : TemplatedControl
{
    public static readonly StyledProperty<string?> TextProperty = AvaloniaProperty.Register<Kbd, string?>(nameof(Text));

    public string? Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }
}

public enum BadgeTone
{
    Neutral,
    Info,
    Success,
    Warning,
    Danger,
    Accent,
}

/// <summary>Small status label. Tone maps to status tints; <see cref="ShowDot"/> adds a leading dot.</summary>
public class Badge : TemplatedControl
{
    public static readonly StyledProperty<string?> TextProperty = AvaloniaProperty.Register<Badge, string?>(nameof(Text));
    public static readonly StyledProperty<BadgeTone> ToneProperty = AvaloniaProperty.Register<Badge, BadgeTone>(nameof(Tone));
    public static readonly StyledProperty<bool> ShowDotProperty = AvaloniaProperty.Register<Badge, bool>(nameof(ShowDot));

    static Badge()
    {
        ToneProperty.Changed.AddClassHandler<Badge>((b, _) => b.UpdateTone());
    }

    public Badge() => UpdateTone();

    public string? Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public BadgeTone Tone { get => GetValue(ToneProperty); set => SetValue(ToneProperty, value); }
    public bool ShowDot { get => GetValue(ShowDotProperty); set => SetValue(ShowDotProperty, value); }

    private void UpdateTone()
    {
        foreach (var t in Enum.GetValues<BadgeTone>())
            PseudoClasses.Set(":" + t.ToString().ToLowerInvariant(), t == Tone);
    }
}

/// <summary>A 1px hairline in <c>border.default</c>, horizontal or vertical.</summary>
public class Divider : Control
{
    public static readonly StyledProperty<Orientation> OrientationProperty =
        AvaloniaProperty.Register<Divider, Orientation>(nameof(Orientation));

    public static readonly StyledProperty<IBrush?> BrushProperty =
        AvaloniaProperty.Register<Divider, IBrush?>(nameof(Brush));

    static Divider()
    {
        AffectsMeasure<Divider>(OrientationProperty);
        AffectsRender<Divider>(BrushProperty, OrientationProperty);
        FocusableProperty.OverrideDefaultValue<Divider>(false);
    }

    public Orientation Orientation { get => GetValue(OrientationProperty); set => SetValue(OrientationProperty, value); }
    public IBrush? Brush { get => GetValue(BrushProperty); set => SetValue(BrushProperty, value); }

    protected override Size MeasureOverride(Size availableSize) =>
        Orientation == Orientation.Horizontal ? new Size(0, 1) : new Size(1, 0);

    public override void Render(DrawingContext context)
    {
        if (Brush is null)
            return;
        var r = Orientation == Orientation.Horizontal
            ? new Rect(0, 0, Bounds.Width, 1)
            : new Rect(0, 0, 1, Bounds.Height);
        context.FillRectangle(Brush, r);
    }
}

/// <summary>Takes the remaining main-axis space inside a <see cref="Stack"/> (pushes siblings apart).</summary>
public class Spacer : Control
{
    static Spacer() => FocusableProperty.OverrideDefaultValue<Spacer>(false);
}
