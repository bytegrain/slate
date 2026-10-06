using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Slate.Collections;

namespace Slate.Wpf;

/// <summary>
/// Alloy slider (design/api/components.json "Slider"): one thumb, or two when <see cref="RangeEnd"/> is set. Snapping,
/// keyboard steps and range-thumb rules come from Slate.Core <see cref="SliderMath"/>.
/// </summary>
/// <remarks>WPF spellings: canonical <c>Min</c>/<c>Max</c> are RangeBase's <c>Minimum</c>/<c>Maximum</c>.</remarks>
[TemplatePart(Name = PartCanvas, Type = typeof(Canvas))]
[TemplatePart(Name = PartFill, Type = typeof(FrameworkElement))]
[TemplatePart(Name = PartThumb, Type = typeof(Thumb))]
[TemplatePart(Name = PartThumb2, Type = typeof(Thumb))]
[TemplatePart(Name = PartTicks, Type = typeof(SliderTicks))]
public class Slider : RangeBase
{
    public const string PartCanvas = "PART_Canvas";
    public const string PartFill = "PART_Fill";
    public const string PartThumb = "PART_Thumb";
    public const string PartThumb2 = "PART_Thumb2";
    public const string PartTicks = "PART_Ticks";
    private const double ThumbSize = 16;

    public static readonly DependencyProperty RangeEndProperty = DependencyProperty.Register(
        nameof(RangeEnd), typeof(double?), typeof(Slider),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((Slider)d).Sync()));

    public static readonly DependencyProperty StepProperty = DependencyProperty.Register(
        nameof(Step), typeof(double), typeof(Slider), new FrameworkPropertyMetadata(1.0, (d, _) => ((Slider)d).Sync()));

    public static readonly DependencyProperty TicksProperty = DependencyProperty.Register(
        nameof(Ticks), typeof(bool), typeof(Slider), new FrameworkPropertyMetadata(false, (d, _) => ((Slider)d).Sync()));

    public static readonly DependencyProperty ShowValueProperty = Sl.ShowValueProperty.AddOwner(typeof(Slider));
    public static readonly DependencyProperty LabelProperty = Sl.LabelProperty.AddOwner(typeof(Slider));
    public static readonly DependencyProperty ToneProperty = Sl.ToneProperty.AddOwner(typeof(Slider));

    private static readonly DependencyPropertyKey IsRangePropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(IsRange), typeof(bool), typeof(Slider), new FrameworkPropertyMetadata(false));

    public static readonly DependencyProperty IsRangeProperty = IsRangePropertyKey.DependencyProperty;

    private static readonly DependencyPropertyKey ValueTextPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(ValueText), typeof(string), typeof(Slider), new FrameworkPropertyMetadata(""));

    public static readonly DependencyProperty ValueTextProperty = ValueTextPropertyKey.DependencyProperty;

    private Canvas? _canvas;
    private FrameworkElement? _fill;
    private Thumb? _thumb;
    private Thumb? _thumb2;
    private SliderTicks? _ticks;

    static Slider()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(Slider), new FrameworkPropertyMetadata(typeof(Slider)));
        MaximumProperty.OverrideMetadata(typeof(Slider), new FrameworkPropertyMetadata(100.0));
        SmallChangeProperty.OverrideMetadata(typeof(Slider), new FrameworkPropertyMetadata(1.0));
        FocusableProperty.OverrideMetadata(typeof(Slider), new FrameworkPropertyMetadata(false));
        KeyboardNavigation.IsTabStopProperty.OverrideMetadata(typeof(Slider), new FrameworkPropertyMetadata(false));
    }

    public Slider() => Sl.SetKind(this, SlKind.Progress);

    /// <summary>End of a two-thumb range (null = single thumb). <see cref="RangeBase.Value"/> is the start.</summary>
    public double? RangeEnd { get => (double?)GetValue(RangeEndProperty); set => SetValue(RangeEndProperty, value); }

    public double Step { get => (double)GetValue(StepProperty); set => SetValue(StepProperty, value); }

    /// <summary>Tick marks at each step (when there are at most 50).</summary>
    public bool Ticks { get => (bool)GetValue(TicksProperty); set => SetValue(TicksProperty, value); }

    public bool ShowValue { get => (bool)GetValue(ShowValueProperty); set => SetValue(ShowValueProperty, value); }
    public string? Label { get => (string?)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public Tone Tone { get => (Tone)GetValue(ToneProperty); set => SetValue(ToneProperty, value); }

    public bool IsRange => (bool)GetValue(IsRangeProperty);
    public string ValueText => (string)GetValue(ValueTextProperty);

    public override void OnApplyTemplate()
    {
        Detach(_thumb);
        Detach(_thumb2);
        if (_canvas is not null)
        {
            _canvas.SizeChanged -= OnCanvasSizeChanged;
            _canvas.MouseLeftButtonDown -= OnTrackPressed;
        }

        base.OnApplyTemplate();
        _canvas = GetTemplateChild(PartCanvas) as Canvas;
        _fill = GetTemplateChild(PartFill) as FrameworkElement;
        _thumb = GetTemplateChild(PartThumb) as Thumb;
        _thumb2 = GetTemplateChild(PartThumb2) as Thumb;
        _ticks = GetTemplateChild(PartTicks) as SliderTicks;

        Attach(_thumb);
        Attach(_thumb2);
        if (_canvas is not null)
        {
            _canvas.SizeChanged += OnCanvasSizeChanged;
            _canvas.MouseLeftButtonDown += OnTrackPressed;
        }
        Sync();
    }

    private void Attach(Thumb? t)
    {
        if (t is null) return;
        t.DragDelta += OnDrag;
        t.PreviewKeyDown += OnThumbKeyDown;
    }

    private void Detach(Thumb? t)
    {
        if (t is null) return;
        t.DragDelta -= OnDrag;
        t.PreviewKeyDown -= OnThumbKeyDown;
    }

    protected override void OnValueChanged(double oldValue, double newValue)
    {
        base.OnValueChanged(oldValue, newValue);
        Sync();
    }

    protected override void OnMinimumChanged(double oldMinimum, double newMinimum)
    {
        base.OnMinimumChanged(oldMinimum, newMinimum);
        Sync();
    }

    protected override void OnMaximumChanged(double oldMaximum, double newMaximum)
    {
        base.OnMaximumChanged(oldMaximum, newMaximum);
        Sync();
    }

    private void OnCanvasSizeChanged(object sender, SizeChangedEventArgs e) => Sync();

    private static string Format(double v) => v.ToString("0.##", CultureInfo.CurrentCulture);

    /// <summary>Lays thumbs, fill and ticks out for the current values.</summary>
    private void Sync()
    {
        SetValue(IsRangePropertyKey, RangeEnd is not null);
        SetValue(ValueTextPropertyKey, RangeEnd is { } end ? $"{Format(Value)} – {Format(end)}" : Format(Value));

        if (_thumb2 is not null)
            _thumb2.Visibility = RangeEnd is null ? Visibility.Collapsed : Visibility.Visible;
        if (_thumb is not null)
            AutomationProperties.SetName(_thumb, RangeEnd is null ? Label ?? "Value" : $"{Label ?? "Range"} start");
        if (_thumb2 is not null)
            AutomationProperties.SetName(_thumb2, $"{Label ?? "Range"} end");

        if (_canvas is null || _canvas.ActualWidth <= 0)
            return;

        var lane = Math.Max(0, _canvas.ActualWidth - ThumbSize);
        double X(double v) => SliderMath.ToFraction(v, Minimum, Maximum) * lane;

        var start = RangeEnd is null ? 0 : X(Value);
        var stop = RangeEnd is { } e ? X(e) : X(Value);
        if (_thumb is not null) Canvas.SetLeft(_thumb, X(Value));
        if (_thumb2 is not null && RangeEnd is { } re) Canvas.SetLeft(_thumb2, X(re));
        if (_fill is not null)
        {
            Canvas.SetLeft(_fill, start + ThumbSize / 2);
            _fill.Width = Math.Max(0, stop - start);
        }

        if (_ticks is not null)
        {
            var count = Step > 0 ? (int)Math.Round((Maximum - Minimum) / Step) : 0;
            _ticks.Positions = Ticks && count is > 0 and <= 50
                ? Enumerable.Range(0, count + 1).Select(i => ThumbSize / 2 + lane * i / count).ToArray()
                : [];
        }
    }

    private void SetThumb(int thumb, double value)
    {
        if (RangeEnd is { } end)
        {
            var (s, e) = SliderMath.SetRangeThumb((Value, end), thumb, value, Minimum, Maximum, Step);
            SetCurrentValue(ValueProperty, s);
            SetCurrentValue(RangeEndProperty, e);
        }
        else
        {
            SetCurrentValue(ValueProperty, SliderMath.Snap(value, Minimum, Maximum, Step));
        }
    }

    private double ValueAt(double x)
    {
        var lane = Math.Max(1, (_canvas?.ActualWidth ?? 1) - ThumbSize);
        return SliderMath.FromFraction(Math.Clamp((x - ThumbSize / 2) / lane, 0, 1), Minimum, Maximum, Step);
    }

    private void OnDrag(object sender, DragDeltaEventArgs e)
    {
        if (_canvas is null) return;
        SetThumb(ReferenceEquals(sender, _thumb2) ? 1 : 0, ValueAt(Mouse.GetPosition(_canvas).X));
    }

    private void OnTrackPressed(object sender, MouseButtonEventArgs e)
    {
        if (_canvas is null || e.OriginalSource is Thumb) return;
        var value = ValueAt(e.GetPosition(_canvas).X);
        var thumb = RangeEnd is { } end && Math.Abs(value - end) < Math.Abs(value - Value) ? 1 : 0;
        SetThumb(thumb, value);
        (thumb == 1 ? _thumb2 : _thumb)?.Focus();
        e.Handled = true;
    }

    private void OnThumbKeyDown(object sender, KeyEventArgs e)
    {
        SliderKey? key = e.Key switch
        {
            Key.Right or Key.Up => SliderKey.Increase,
            Key.Left or Key.Down => SliderKey.Decrease,
            Key.PageUp => SliderKey.PageIncrease,
            Key.PageDown => SliderKey.PageDecrease,
            Key.Home => SliderKey.Home,
            Key.End => SliderKey.End,
            _ => null,
        };
        if (key is null) return;
        var thumb = ReferenceEquals(sender, _thumb2) ? 1 : 0;
        var current = thumb == 1 ? RangeEnd ?? Value : Value;
        SetThumb(thumb, SliderMath.Key(current, key.Value, Minimum, Maximum, Step));
        e.Handled = true;
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new SlateSliderAutomationPeer(this);

    private sealed class SlateSliderAutomationPeer(Slider owner) : RangeBaseAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Slider;
        protected override string GetClassNameCore() => "Slider";
        protected override string GetNameCore() => base.GetNameCore() is { Length: > 0 } n ? n : ((Slider)Owner).Label ?? "";
    }
}

/// <summary>Draws slider tick marks at the given x positions.</summary>
public class SliderTicks : FrameworkElement
{
    public static readonly DependencyProperty PositionsProperty = DependencyProperty.Register(
        nameof(Positions), typeof(double[]), typeof(SliderTicks), new FrameworkPropertyMetadata(Array.Empty<double>(), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BrushProperty = DependencyProperty.Register(
        nameof(Brush), typeof(Brush), typeof(SliderTicks), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public double[] Positions { get => (double[])GetValue(PositionsProperty); set => SetValue(PositionsProperty, value); }
    public Brush? Brush { get => (Brush?)GetValue(BrushProperty); set => SetValue(BrushProperty, value); }

    protected override void OnRender(DrawingContext dc)
    {
        if (Brush is null) return;
        foreach (var x in Positions)
            dc.DrawRectangle(Brush, null, new Rect(Math.Round(x) - 0.5, 0, 1, ActualHeight));
    }
}

public enum AvatarStatus
{
    None,
    Online,
    Away,
    Busy,
    Offline,
}

/// <summary>
/// Person avatar (design/api/components.json "Avatar"): image, or initials on a tone derived deterministically from the
/// name (Slate.Core <see cref="AvatarText"/>), with an optional presence dot.
/// </summary>
/// <remarks>The canonical <c>Name</c> option is spelled <see cref="DisplayName"/>: FrameworkElement.Name is the element's identifier.</remarks>
public class Avatar : Control
{
    private static readonly PropertyChangedCallback Update = (d, _) => ((Avatar)d).Sync();

    public static readonly DependencyProperty DisplayNameProperty = DependencyProperty.Register(
        nameof(DisplayName), typeof(string), typeof(Avatar), new FrameworkPropertyMetadata(null, Update));

    public static readonly DependencyProperty ImageProperty = DependencyProperty.Register(
        nameof(Image), typeof(string), typeof(Avatar), new FrameworkPropertyMetadata(null, Update));

    public static readonly DependencyProperty SizeProperty = Sl.SizeProperty.AddOwner(typeof(Avatar), new FrameworkPropertyMetadata(ControlSize.Medium, Update));

    public static readonly DependencyProperty ToneProperty = DependencyProperty.Register(
        nameof(Tone), typeof(Slate.Tone?), typeof(Avatar), new FrameworkPropertyMetadata(null, Update));

    public static readonly DependencyProperty StatusProperty = DependencyProperty.Register(
        nameof(Status), typeof(AvatarStatus), typeof(Avatar), new FrameworkPropertyMetadata(AvatarStatus.None, Update));

    private static readonly DependencyPropertyKey InitialsPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(Initials), typeof(string), typeof(Avatar), new FrameworkPropertyMetadata(""));

    public static readonly DependencyProperty InitialsProperty = InitialsPropertyKey.DependencyProperty;

    private static readonly DependencyPropertyKey ImageSourcePropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(ImageSource), typeof(ImageSource), typeof(Avatar), new FrameworkPropertyMetadata(null));

    public static readonly DependencyProperty ImageSourceProperty = ImageSourcePropertyKey.DependencyProperty;

    static Avatar()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(Avatar), new FrameworkPropertyMetadata(typeof(Avatar)));
        FocusableProperty.OverrideMetadata(typeof(Avatar), new FrameworkPropertyMetadata(false));
        KeyboardNavigation.IsTabStopProperty.OverrideMetadata(typeof(Avatar), new FrameworkPropertyMetadata(false));
    }

    public Avatar() => Sync();

    /// <summary>The person's name: initials, tone and accessible name come from it.</summary>
    public string? DisplayName { get => (string?)GetValue(DisplayNameProperty); set => SetValue(DisplayNameProperty, value); }

    /// <summary>Image URI (pack, file or http). Falls back to initials if it can't be loaded.</summary>
    public string? Image { get => (string?)GetValue(ImageProperty); set => SetValue(ImageProperty, value); }

    public ControlSize Size { get => (ControlSize)GetValue(SizeProperty); set => SetValue(SizeProperty, value); }

    /// <summary>Colour role; null derives one from <see cref="DisplayName"/>.</summary>
    public Slate.Tone? Tone { get => (Slate.Tone?)GetValue(ToneProperty); set => SetValue(ToneProperty, value); }

    public AvatarStatus Status { get => (AvatarStatus)GetValue(StatusProperty); set => SetValue(StatusProperty, value); }

    public string Initials => (string)GetValue(InitialsProperty);
    public ImageSource? ImageSource => (ImageSource?)GetValue(ImageSourceProperty);

    private void Sync()
    {
        SetValue(InitialsPropertyKey, AvatarText.Initials(DisplayName));
        var tone = Tone ?? AvatarText.ToneFor(DisplayName);
        Sl.SetActualTone(this, tone);
        Sl.SetActualSize(this, Size);

        var keys = Styling.Keys(tone);
        SetResourceReference(Sl.ChromeBackgroundProperty, tone == Slate.Tone.Neutral ? "Sl.Brush.Background.Muted" : keys.Subtle);
        SetResourceReference(Sl.ChromeForegroundProperty, tone == Slate.Tone.Neutral ? "Sl.Brush.Text.Secondary" : keys.Text);
        SetResourceReference(Sl.FillProperty, Status switch
        {
            AvatarStatus.Online => "Sl.Brush.Status.Success.Solid",
            AvatarStatus.Away => "Sl.Brush.Status.Warning.Solid",
            AvatarStatus.Busy => "Sl.Brush.Status.Danger.Solid",
            _ => "Sl.Brush.Border.Control",
        });

        SetValue(ImageSourcePropertyKey, LoadImage(Image));
        var status = Status == AvatarStatus.None ? "" : $" ({Status.ToString().ToLowerInvariant()})";
        AutomationProperties.SetName(this, (DisplayName ?? "") + status);
    }

    private static ImageSource? LoadImage(string? uri)
    {
        if (string.IsNullOrWhiteSpace(uri) || !Uri.TryCreate(uri, UriKind.RelativeOrAbsolute, out var parsed))
            return null;
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = parsed;
            image.CacheOption = BitmapCacheOption.OnDemand;
            image.EndInit();
            return image;
        }
        catch (Exception ex) when (ex is NotSupportedException or System.IO.IOException or UriFormatException or ArgumentException)
        {
            return null;
        }
    }
}

public enum SkeletonShape
{
    Text,
    Rect,
    Circle,
}

/// <summary>
/// Loading placeholder (design/api/components.json "Skeleton"): text lines, a rectangle or a circle, with a gentle pulse
/// (off when Animated is false or motion is reduced). Hidden from assistive technology — announce loading elsewhere.
/// </summary>
[TemplatePart(Name = PartRoot, Type = typeof(FrameworkElement))]
public class Skeleton : Control
{
    public const string PartRoot = "PART_Root";

    public static readonly DependencyProperty ShapeProperty = DependencyProperty.Register(
        nameof(Shape), typeof(SkeletonShape), typeof(Skeleton), new FrameworkPropertyMetadata(SkeletonShape.Text));

    public static readonly DependencyProperty LinesProperty = DependencyProperty.Register(
        nameof(Lines), typeof(int), typeof(Skeleton), new FrameworkPropertyMetadata(1, (d, _) => ((Skeleton)d).SyncLines()));

    public static readonly DependencyProperty AnimatedProperty = DependencyProperty.Register(
        nameof(Animated), typeof(bool), typeof(Skeleton), new FrameworkPropertyMetadata(true, (d, _) => ((Skeleton)d).SyncAnimation()));

    private static readonly DependencyPropertyKey LineWidthsPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(LineWidths), typeof(IReadOnlyList<double>), typeof(Skeleton), new FrameworkPropertyMetadata(new[] { 1.0 }));

    /// <summary>Relative width of each text line (the last of several is shorter).</summary>
    public static readonly DependencyProperty LineWidthsProperty = LineWidthsPropertyKey.DependencyProperty;

    private FrameworkElement? _root;

    static Skeleton()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(Skeleton), new FrameworkPropertyMetadata(typeof(Skeleton)));
        FocusableProperty.OverrideMetadata(typeof(Skeleton), new FrameworkPropertyMetadata(false));
        KeyboardNavigation.IsTabStopProperty.OverrideMetadata(typeof(Skeleton), new FrameworkPropertyMetadata(false));
    }

    public Skeleton()
    {
        Loaded += (_, _) => SyncAnimation();
        Unloaded += (_, _) => _root?.BeginAnimation(OpacityProperty, null);
    }

    public SkeletonShape Shape { get => (SkeletonShape)GetValue(ShapeProperty); set => SetValue(ShapeProperty, value); }
    public int Lines { get => (int)GetValue(LinesProperty); set => SetValue(LinesProperty, value); }
    public bool Animated { get => (bool)GetValue(AnimatedProperty); set => SetValue(AnimatedProperty, value); }
    public IReadOnlyList<double> LineWidths => (IReadOnlyList<double>)GetValue(LineWidthsProperty);

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _root = GetTemplateChild(PartRoot) as FrameworkElement;
        SyncAnimation();
    }

    private void SyncLines()
    {
        var n = Math.Max(1, Lines);
        SetValue(LineWidthsPropertyKey, Enumerable.Range(0, n).Select(i => n > 1 && i == n - 1 ? 0.6 : 1.0).ToArray());
    }

    private void SyncAnimation()
    {
        if (_root is null)
            return;
        if (!Animated || SlateMotion.IsReduced || !IsLoaded)
        {
            _root.BeginAnimation(OpacityProperty, null);
            return;
        }
        _root.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0.55, TimeSpan.FromMilliseconds(900))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        });
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new HiddenPeer(this);

    private sealed class HiddenPeer(Skeleton owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override bool IsControlElementCore() => false;
        protected override bool IsContentElementCore() => false;
    }
}
