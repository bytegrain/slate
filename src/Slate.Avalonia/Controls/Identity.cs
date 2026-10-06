using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Slate.Collections;

namespace Slate.Avalonia.Controls;

/// <summary>Presence shown as a dot on an <see cref="Avatar"/>.</summary>
public enum AvatarStatus
{
    Online,
    Away,
    Busy,
    Offline,
}

/// <summary>
/// A person or entity (docs: Avatar): image, or initials on a deterministic tone derived from the name
/// (Slate.Core AvatarText), with an optional presence dot. Canonical option <c>Name</c> is spelled
/// <see cref="DisplayName"/> here because StyledElement.Name already exists.
/// </summary>
[PseudoClasses(":has-image", ":small", ":large")]
public class Avatar : TemplatedControl
{
    public static readonly StyledProperty<string?> DisplayNameProperty = AvaloniaProperty.Register<Avatar, string?>(nameof(DisplayName));

    /// <summary>Image path or URI (avares://, file path or http(s) already downloaded to a local path).</summary>
    public static readonly StyledProperty<string?> ImageProperty = AvaloniaProperty.Register<Avatar, string?>(nameof(Image));

    public static readonly StyledProperty<ControlSize> SizeProperty = AvaloniaProperty.Register<Avatar, ControlSize>(nameof(Size), ControlSize.Medium);
    public static readonly StyledProperty<Tone?> ToneProperty = AvaloniaProperty.Register<Avatar, Tone?>(nameof(Tone));
    public static readonly StyledProperty<AvatarStatus?> StatusProperty = AvaloniaProperty.Register<Avatar, AvatarStatus?>(nameof(Status));

    public static readonly DirectProperty<Avatar, string> InitialsProperty = AvaloniaProperty.RegisterDirect<Avatar, string>(nameof(Initials), a => a.Initials);
    public static readonly DirectProperty<Avatar, IImage?> ImageSourceProperty = AvaloniaProperty.RegisterDirect<Avatar, IImage?>(nameof(ImageSource), a => a.ImageSource);

    private string _initials = "";
    private IImage? _imageSource;

    static Avatar()
    {
        DisplayNameProperty.Changed.AddClassHandler<Avatar>((a, _) => a.Update());
        ToneProperty.Changed.AddClassHandler<Avatar>((a, _) => a.Update());
        StatusProperty.Changed.AddClassHandler<Avatar>((a, _) => a.Update());
        SizeProperty.Changed.AddClassHandler<Avatar>((a, _) => a.Update());
        ImageProperty.Changed.AddClassHandler<Avatar>((a, e) => a.LoadImage(e.GetNewValue<string?>()));
        FocusableProperty.OverrideDefaultValue<Avatar>(false);
    }

    public Avatar() => Update();

    public string? DisplayName { get => GetValue(DisplayNameProperty); set => SetValue(DisplayNameProperty, value); }
    public string? Image { get => GetValue(ImageProperty); set => SetValue(ImageProperty, value); }
    public ControlSize Size { get => GetValue(SizeProperty); set => SetValue(SizeProperty, value); }
    public Tone? Tone { get => GetValue(ToneProperty); set => SetValue(ToneProperty, value); }
    public AvatarStatus? Status { get => GetValue(StatusProperty); set => SetValue(StatusProperty, value); }
    public string Initials { get => _initials; private set => SetAndRaise(InitialsProperty, ref _initials, value); }
    public IImage? ImageSource { get => _imageSource; private set => SetAndRaise(ImageSourceProperty, ref _imageSource, value); }

    /// <summary>The tone in use: <see cref="Tone"/>, else derived from <see cref="DisplayName"/>.</summary>
    public Tone EffectiveTone => Tone ?? AvatarText.ToneFor(DisplayName);

    private void Update()
    {
        Initials = AvatarText.Initials(DisplayName);
        Slate.Avalonia.Sl.SetOne(this, Slate.Avalonia.Sl.ToneClasses, Slate.Avalonia.Sl.ClassFor(EffectiveTone));
        PseudoClasses.Set(":small", Size == ControlSize.Small);
        PseudoClasses.Set(":large", Size == ControlSize.Large);
        foreach (var s in Enum.GetValues<AvatarStatus>())
            Classes.Set("status-" + s.ToString().ToLowerInvariant(), Status == s);
        Classes.Set("has-status", Status is not null);
        AutomationProperties.SetName(this, Status is { } st && DisplayName is { } n ? $"{n} ({st.ToString().ToLowerInvariant()})" : DisplayName);
    }

    private void LoadImage(string? source)
    {
        IImage? image = null;
        try
        {
            if (!string.IsNullOrEmpty(source))
            {
                if (source.StartsWith("avares://", StringComparison.Ordinal))
                    image = new Bitmap(AssetLoader.Open(new Uri(source)));
                else if (File.Exists(source))
                    image = new Bitmap(source);
            }
        }
        catch (Exception)
        {
            image = null; // fall back to initials
        }
        ImageSource = image;
        PseudoClasses.Set(":has-image", image is not null);
    }
}

public enum SkeletonShape
{
    Text,
    Rect,
    Circle,
}

/// <summary>
/// Loading placeholder (docs: Skeleton): text lines (last one shorter), a rectangle or a circle, pulsing unless
/// <see cref="Animated"/> is off or reduced motion is on. Hidden from assistive tech.
/// </summary>
[TemplatePart("PART_Lines", typeof(SkeletonLinesPanel))]
[PseudoClasses(":text", ":rect", ":circle", ":animated")]
public class Skeleton : TemplatedControl
{
    public static readonly StyledProperty<SkeletonShape> ShapeProperty = AvaloniaProperty.Register<Skeleton, SkeletonShape>(nameof(Shape));
    public static readonly StyledProperty<int> LinesProperty = AvaloniaProperty.Register<Skeleton, int>(nameof(Lines), 1, coerce: (_, v) => Math.Max(1, v));
    public static readonly StyledProperty<bool> AnimatedProperty = AvaloniaProperty.Register<Skeleton, bool>(nameof(Animated), true);

    private Panel? _lines;
    private SlateTheme? _theme;

    static Skeleton()
    {
        ShapeProperty.Changed.AddClassHandler<Skeleton>((s, _) => s.Update());
        LinesProperty.Changed.AddClassHandler<Skeleton>((s, _) => s.Update());
        AnimatedProperty.Changed.AddClassHandler<Skeleton>((s, _) => s.Update());
        FocusableProperty.OverrideDefaultValue<Skeleton>(false);
    }

    public Skeleton()
    {
        AutomationProperties.SetAccessibilityView(this, AccessibilityView.Raw);
        Update();
    }

    public SkeletonShape Shape { get => GetValue(ShapeProperty); set => SetValue(ShapeProperty, value); }
    public int Lines { get => GetValue(LinesProperty); set => SetValue(LinesProperty, value); }
    public bool Animated { get => GetValue(AnimatedProperty); set => SetValue(AnimatedProperty, value); }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _lines = e.NameScope.Find<Panel>("PART_Lines");
        Update();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _theme = SlateTheme.Current;
        if (_theme is not null) _theme.Changed += OnThemeChanged;
        Update();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_theme is not null) _theme.Changed -= OnThemeChanged;
        _theme = null;
    }

    private void OnThemeChanged(object? sender, EventArgs e) => Update();

    private void Update()
    {
        PseudoClasses.Set(":text", Shape == SkeletonShape.Text);
        PseudoClasses.Set(":rect", Shape == SkeletonShape.Rect);
        PseudoClasses.Set(":circle", Shape == SkeletonShape.Circle);
        PseudoClasses.Set(":animated", Animated && !(SlateTheme.Current?.ReduceMotion ?? false));

        if (_lines is null) return;
        _lines.Children.Clear();
        if (Shape != SkeletonShape.Text) return;
        for (var i = 0; i < Lines; i++)
            _lines.Children.Add(new Border { Classes = { "skeleton-line" } });
    }
}

/// <summary>Stacks skeleton text lines; with more than one line, the last is 60% wide.</summary>
public class SkeletonLinesPanel : Panel
{
    public static readonly StyledProperty<double> SpacingProperty = AvaloniaProperty.Register<SkeletonLinesPanel, double>(nameof(Spacing), 8);

    static SkeletonLinesPanel() => AffectsMeasure<SkeletonLinesPanel>(SpacingProperty);

    public double Spacing { get => GetValue(SpacingProperty); set => SetValue(SpacingProperty, value); }

    protected override Size MeasureOverride(Size availableSize)
    {
        double height = 0, width = 0;
        foreach (var c in Children)
        {
            c.Measure(availableSize);
            height += c.DesiredSize.Height;
            width = Math.Max(width, c.DesiredSize.Width);
        }
        height += Math.Max(0, Children.Count - 1) * Spacing;
        return new Size(double.IsInfinity(availableSize.Width) ? width : availableSize.Width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double y = 0;
        for (var i = 0; i < Children.Count; i++)
        {
            var c = Children[i];
            var w = Children.Count > 1 && i == Children.Count - 1 ? finalSize.Width * 0.6 : finalSize.Width;
            c.Arrange(new Rect(0, y, w, c.DesiredSize.Height));
            y += c.DesiredSize.Height + Spacing;
        }
        return finalSize;
    }
}
