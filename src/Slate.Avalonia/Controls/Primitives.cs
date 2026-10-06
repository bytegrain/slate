using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Metadata;
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

/// <summary>
/// Indeterminate busy indicator: a 2px ring rotating unless motion is reduced. <see cref="Size"/> picks
/// 12/16/24px (or set <see cref="Diameter"/>); <see cref="Tone"/> colours it (Neutral = current foreground).
/// </summary>
public class LoadingSpinner : TemplatedControl
{
    public static readonly StyledProperty<ControlSize> SizeProperty =
        AvaloniaProperty.Register<LoadingSpinner, ControlSize>(nameof(Size), ControlSize.Medium);

    /// <summary>Exact diameter in px; overrides <see cref="Size"/> when set.</summary>
    public static readonly StyledProperty<double> DiameterProperty =
        AvaloniaProperty.Register<LoadingSpinner, double>(nameof(Diameter), double.NaN);

    public static readonly StyledProperty<Tone> ToneProperty =
        AvaloniaProperty.Register<LoadingSpinner, Tone>(nameof(Tone));

    /// <summary>Accessible name (default "Loading").</summary>
    public static readonly StyledProperty<string?> LabelProperty =
        AvaloniaProperty.Register<LoadingSpinner, string?>(nameof(Label), "Loading");

    public static readonly DirectProperty<LoadingSpinner, double> ActualDiameterProperty =
        AvaloniaProperty.RegisterDirect<LoadingSpinner, double>(nameof(ActualDiameter), s => s.ActualDiameter);

    private double _actual = 16;

    public ControlSize Size { get => GetValue(SizeProperty); set => SetValue(SizeProperty, value); }
    public double Diameter { get => GetValue(DiameterProperty); set => SetValue(DiameterProperty, value); }
    public Tone Tone { get => GetValue(ToneProperty); set => SetValue(ToneProperty, value); }
    public string? Label { get => GetValue(LabelProperty); set => SetValue(LabelProperty, value); }

    /// <summary>The diameter actually used: <see cref="Diameter"/> if set, else 12/16/24 for <see cref="Size"/>.</summary>
    public double ActualDiameter { get => _actual; private set => SetAndRaise(ActualDiameterProperty, ref _actual, value); }

    static LoadingSpinner()
    {
        FocusableProperty.OverrideDefaultValue<LoadingSpinner>(false);
        SizeProperty.Changed.AddClassHandler<LoadingSpinner>((s, _) => s.Update());
        DiameterProperty.Changed.AddClassHandler<LoadingSpinner>((s, _) => s.Update());
        ToneProperty.Changed.AddClassHandler<LoadingSpinner>((s, _) => s.Update());
        LabelProperty.Changed.AddClassHandler<LoadingSpinner>((s, e) => AutomationProperties.SetName(s, e.GetNewValue<string?>()));
    }

    public LoadingSpinner()
    {
        AutomationProperties.SetName(this, Label);
        Update();
    }

    private void Update()
    {
        ActualDiameter = !double.IsNaN(Diameter) ? Diameter : Size switch { ControlSize.Small => 12, ControlSize.Large => 24, _ => 16 };
        Slate.Avalonia.Sl.SetOne(this, Slate.Avalonia.Sl.ToneClasses, Tone == Tone.Neutral ? null : Slate.Avalonia.Sl.ClassFor(Tone));
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

/// <summary>
/// Small status label (docs/design/components.md#badge). <see cref="Tone"/> × <see cref="Variant"/> (soft tint,
/// solid fill or outlined), <see cref="Dot"/> adds a leading dot, <see cref="Icon"/> a leading icon.
/// </summary>
[PseudoClasses(":solid", ":outlined", ":small")]
public class Badge : ContentControl
{
    public static readonly StyledProperty<Tone> ToneProperty = AvaloniaProperty.Register<Badge, Tone>(nameof(Tone));
    public static readonly StyledProperty<BadgeVariant> VariantProperty = AvaloniaProperty.Register<Badge, BadgeVariant>(nameof(Variant));
    public static readonly StyledProperty<ControlSize> SizeProperty = AvaloniaProperty.Register<Badge, ControlSize>(nameof(Size), ControlSize.Medium);
    public static readonly StyledProperty<bool> DotProperty = AvaloniaProperty.Register<Badge, bool>(nameof(Dot));
    public static readonly StyledProperty<string?> IconProperty = AvaloniaProperty.Register<Badge, string?>(nameof(Icon));

    static Badge()
    {
        ToneProperty.Changed.AddClassHandler<Badge>((b, _) => b.Update());
        VariantProperty.Changed.AddClassHandler<Badge>((b, _) => b.Update());
        SizeProperty.Changed.AddClassHandler<Badge>((b, _) => b.Update());
    }

    public Badge() => Update();

    public Tone Tone { get => GetValue(ToneProperty); set => SetValue(ToneProperty, value); }
    public BadgeVariant Variant { get => GetValue(VariantProperty); set => SetValue(VariantProperty, value); }

    /// <summary>Small (18px) or Medium (20px). Large renders as Medium.</summary>
    public ControlSize Size { get => GetValue(SizeProperty); set => SetValue(SizeProperty, value); }

    public bool Dot { get => GetValue(DotProperty); set => SetValue(DotProperty, value); }
    public string? Icon { get => GetValue(IconProperty); set => SetValue(IconProperty, value); }

    private void Update()
    {
        Slate.Avalonia.Sl.SetOne(this, Slate.Avalonia.Sl.ToneClasses, Slate.Avalonia.Sl.ClassFor(Tone));
        PseudoClasses.Set(":solid", Variant == BadgeVariant.Solid);
        PseudoClasses.Set(":outlined", Variant == BadgeVariant.Outlined);
        PseudoClasses.Set(":small", Size == ControlSize.Small);
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
