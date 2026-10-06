using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Slate.Avalonia.Controls;

/// <summary>
/// Surface with an optional header bar (<see cref="Title"/>, <see cref="Subtitle"/>, <see cref="HeaderActions"/>, or a
/// custom <see cref="Header"/>), body (<see cref="ContentControl.Content"/>) and <see cref="Footer"/>.
/// <see cref="Variant"/>: Elevated (default), Outlined or Flat. Styled from <c>Sl.Component.Card.*</c>.
/// </summary>
[PseudoClasses(":has-header", ":has-footer", ":outlined", ":flat", ":interactive", ":flush")]
public class Card : ContentControl
{
    public static readonly StyledProperty<CardVariant> VariantProperty = AvaloniaProperty.Register<Card, CardVariant>(nameof(Variant));
    public static readonly StyledProperty<Radius> RadiusProperty = AvaloniaProperty.Register<Card, Radius>(nameof(Radius));
    public static readonly StyledProperty<string?> TitleProperty = AvaloniaProperty.Register<Card, string?>(nameof(Title));
    public static readonly StyledProperty<string?> SubtitleProperty = AvaloniaProperty.Register<Card, string?>(nameof(Subtitle));
    public static readonly StyledProperty<bool> FlushProperty = AvaloniaProperty.Register<Card, bool>(nameof(Flush));
    public static readonly StyledProperty<bool> InteractiveProperty = AvaloniaProperty.Register<Card, bool>(nameof(Interactive));

    /// <summary>Custom header content; replaces the title/subtitle block.</summary>
    public static readonly StyledProperty<object?> HeaderProperty = AvaloniaProperty.Register<Card, object?>(nameof(Header));
    public static readonly StyledProperty<object?> HeaderActionsProperty = AvaloniaProperty.Register<Card, object?>(nameof(HeaderActions));
    public static readonly StyledProperty<object?> FooterProperty = AvaloniaProperty.Register<Card, object?>(nameof(Footer));

    /// <summary>Raised when an <see cref="Interactive"/> card is clicked (or activated with Enter/Space).</summary>
    public static readonly RoutedEvent<RoutedEventArgs> ClickEvent = RoutedEvent.Register<Card, RoutedEventArgs>(nameof(Click), RoutingStrategies.Bubble);

    private bool _pressed;

    static Card()
    {
        TitleProperty.Changed.AddClassHandler<Card>((c, _) => c.UpdateParts());
        HeaderProperty.Changed.AddClassHandler<Card>((c, _) => c.UpdateParts());
        HeaderActionsProperty.Changed.AddClassHandler<Card>((c, _) => c.UpdateParts());
        FooterProperty.Changed.AddClassHandler<Card>((c, _) => c.UpdateParts());
        VariantProperty.Changed.AddClassHandler<Card>((c, _) => c.UpdateLook());
        RadiusProperty.Changed.AddClassHandler<Card>((c, _) => c.UpdateLook());
        FlushProperty.Changed.AddClassHandler<Card>((c, e) => c.PseudoClasses.Set(":flush", e.GetNewValue<bool>()));
        InteractiveProperty.Changed.AddClassHandler<Card>((c, e) =>
        {
            c.PseudoClasses.Set(":interactive", e.GetNewValue<bool>());
            c.Focusable = e.GetNewValue<bool>();
        });
        LoadedEvent.AddClassHandler<Card>((c, _) => c.UpdateLook());
    }

    public Card() => UpdateLook();

    public CardVariant Variant { get => GetValue(VariantProperty); set => SetValue(VariantProperty, value); }
    public Radius Radius { get => GetValue(RadiusProperty); set => SetValue(RadiusProperty, value); }
    public string? Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string? Subtitle { get => GetValue(SubtitleProperty); set => SetValue(SubtitleProperty, value); }
    public bool Flush { get => GetValue(FlushProperty); set => SetValue(FlushProperty, value); }
    public bool Interactive { get => GetValue(InteractiveProperty); set => SetValue(InteractiveProperty, value); }
    public object? Header { get => GetValue(HeaderProperty); set => SetValue(HeaderProperty, value); }
    public object? HeaderActions { get => GetValue(HeaderActionsProperty); set => SetValue(HeaderActionsProperty, value); }
    public object? Footer { get => GetValue(FooterProperty); set => SetValue(FooterProperty, value); }

    public event EventHandler<RoutedEventArgs>? Click
    {
        add => AddHandler(ClickEvent, value);
        remove => RemoveHandler(ClickEvent, value);
    }

    /// <summary>Variant after applying <see cref="SlateTheme.Defaults"/>.</summary>
    public CardVariant EffectiveVariant => IsSet(VariantProperty) ? Variant : SlateTheme.Defaults.Card.Variant;

    public Radius EffectiveRadius => IsSet(RadiusProperty) ? Radius : SlateTheme.Defaults.Card.Radius;

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        _pressed = Interactive && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_pressed && Interactive)
        {
            _pressed = false;
            RaiseEvent(new RoutedEventArgs(ClickEvent));
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!e.Handled && Interactive && e.Key is Key.Enter or Key.Space)
        {
            RaiseEvent(new RoutedEventArgs(ClickEvent));
            e.Handled = true;
        }
    }

    private void UpdateParts()
    {
        PseudoClasses.Set(":has-header", Header is not null || HeaderActions is not null || !string.IsNullOrEmpty(Title));
        PseudoClasses.Set(":has-footer", Footer is not null);
    }

    private void UpdateLook()
    {
        var v = EffectiveVariant;
        PseudoClasses.Set(":outlined", v == CardVariant.Outlined);
        PseudoClasses.Set(":flat", v == CardVariant.Flat);
        Slate.Avalonia.Sl.SetOne(this, Slate.Avalonia.Sl.RadiusClasses, Slate.Avalonia.Sl.ClassFor(EffectiveRadius));
    }
}

/// <summary>
/// Inline, persistent message (docs/design/components.md#alert): severity icon, title, message (Content),
/// actions and an optional dismiss button. <see cref="Variant"/>: Soft (tint, default), Outlined or Solid.
/// </summary>
[PseudoClasses(":normal", ":info", ":success", ":warning", ":error", ":dismissible", ":outlined", ":solid", ":dense", ":no-icon")]
public class Alert : ContentControl
{
    public static readonly StyledProperty<Severity> SeverityProperty = AvaloniaProperty.Register<Alert, Severity>(nameof(Severity), Severity.Info);
    public static readonly StyledProperty<AlertVariant> VariantProperty = AvaloniaProperty.Register<Alert, AlertVariant>(nameof(Variant));
    public static readonly StyledProperty<string?> TitleProperty = AvaloniaProperty.Register<Alert, string?>(nameof(Title));

    /// <summary>Icon name overriding the severity icon; "none" hides the icon.</summary>
    public static readonly StyledProperty<string?> IconProperty = AvaloniaProperty.Register<Alert, string?>(nameof(Icon));

    public static readonly StyledProperty<bool> DenseProperty = AvaloniaProperty.Register<Alert, bool>(nameof(Dense));
    public static readonly StyledProperty<bool> DismissibleProperty = AvaloniaProperty.Register<Alert, bool>(nameof(Dismissible));

    /// <summary>Announce the alert to assistive tech when it appears (errors assertively, others politely).</summary>
    public static readonly StyledProperty<bool> LiveProperty = AvaloniaProperty.Register<Alert, bool>(nameof(Live), true);

    public static readonly StyledProperty<object?> ActionsProperty = AvaloniaProperty.Register<Alert, object?>(nameof(Actions));

    public static readonly DirectProperty<Alert, string?> IconKindProperty =
        AvaloniaProperty.RegisterDirect<Alert, string?>(nameof(IconKind), a => a.IconKind);

    public static readonly RoutedEvent<RoutedEventArgs> DismissedEvent =
        RoutedEvent.Register<Alert, RoutedEventArgs>(nameof(Dismissed), RoutingStrategies.Bubble);

    private string? _iconKind = "info";

    static Alert()
    {
        SeverityProperty.Changed.AddClassHandler<Alert>((a, _) => a.Update());
        VariantProperty.Changed.AddClassHandler<Alert>((a, _) => a.Update());
        IconProperty.Changed.AddClassHandler<Alert>((a, _) => a.Update());
        LiveProperty.Changed.AddClassHandler<Alert>((a, _) => a.Update());
        DenseProperty.Changed.AddClassHandler<Alert>((a, e) => a.PseudoClasses.Set(":dense", e.GetNewValue<bool>()));
        DismissibleProperty.Changed.AddClassHandler<Alert>((a, e) => a.PseudoClasses.Set(":dismissible", e.GetNewValue<bool>()));
    }

    public Alert() => Update();

    public Severity Severity { get => GetValue(SeverityProperty); set => SetValue(SeverityProperty, value); }
    public AlertVariant Variant { get => GetValue(VariantProperty); set => SetValue(VariantProperty, value); }
    public string? Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string? Icon { get => GetValue(IconProperty); set => SetValue(IconProperty, value); }
    public bool Dense { get => GetValue(DenseProperty); set => SetValue(DenseProperty, value); }
    public bool Dismissible { get => GetValue(DismissibleProperty); set => SetValue(DismissibleProperty, value); }
    public bool Live { get => GetValue(LiveProperty); set => SetValue(LiveProperty, value); }
    public object? Actions { get => GetValue(ActionsProperty); set => SetValue(ActionsProperty, value); }

    /// <summary>The icon actually shown (severity icon unless <see cref="Icon"/> overrides it; null when hidden).</summary>
    public string? IconKind { get => _iconKind; private set => SetAndRaise(IconKindProperty, ref _iconKind, value); }

    public event EventHandler<RoutedEventArgs>? Dismissed
    {
        add => AddHandler(DismissedEvent, value);
        remove => RemoveHandler(DismissedEvent, value);
    }

    public static string IconFor(Severity s) => s switch
    {
        Severity.Success => "check-circle",
        Severity.Warning => "alert-triangle",
        Severity.Error => "alert-circle",
        _ => "info",
    };

    /// <summary>Hides the alert and raises <see cref="Dismissed"/>.</summary>
    public void Dismiss()
    {
        IsVisible = false;
        RaiseEvent(new RoutedEventArgs(DismissedEvent));
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        if (e.NameScope.Find<Button>("PART_Close") is { } close)
            close.Click += (_, _) => Dismiss();
    }

    private void Update()
    {
        foreach (var s in Enum.GetValues<Severity>())
            PseudoClasses.Set(":" + s.ToString().ToLowerInvariant(), s == Severity);
        PseudoClasses.Set(":outlined", Variant == AlertVariant.Outlined);
        PseudoClasses.Set(":solid", Variant == AlertVariant.Solid);
        Slate.Avalonia.Sl.SetOne(this, Slate.Avalonia.Sl.ToneClasses, Slate.Avalonia.Sl.ClassFor(Severity.ToTone()));

        IconKind = Icon switch
        {
            "none" => null,
            { Length: > 0 } custom => custom,
            _ => IconFor(Severity),
        };
        PseudoClasses.Set(":no-icon", IconKind is null);

        AutomationProperties.SetLiveSetting(this, !Live
            ? global::Avalonia.Automation.AutomationLiveSetting.Off
            : Severity == Severity.Error
                ? global::Avalonia.Automation.AutomationLiveSetting.Assertive
                : global::Avalonia.Automation.AutomationLiveSetting.Polite);
    }
}

/// <summary>
/// A labelled text input (docs/design/components.md#text-field) around a native <see cref="TextBox"/>: visible label,
/// required marker, helper or error text, counter, prefix/suffix segments, start/end icons or custom adornments,
/// and a clear button. <see cref="Variant"/>: Outlined (default), Filled or Underlined.
/// The label names the TextBox and the helper/error text becomes its automation help text.
/// </summary>
[PseudoClasses(":invalid", ":required", ":has-prefix", ":has-suffix", ":counter", ":clearable", ":has-value")]
public class TextField : TemplatedControl
{
    public static readonly StyledProperty<string?> ValueProperty =
        AvaloniaProperty.Register<TextField, string?>(nameof(Value), defaultBindingMode: global::Avalonia.Data.BindingMode.TwoWay);
    public static readonly StyledProperty<FieldVariant> VariantProperty = AvaloniaProperty.Register<TextField, FieldVariant>(nameof(Variant));
    public static readonly StyledProperty<ControlSize> SizeProperty = AvaloniaProperty.Register<TextField, ControlSize>(nameof(Size), ControlSize.Medium);
    public static readonly StyledProperty<Radius> RadiusProperty = AvaloniaProperty.Register<TextField, Radius>(nameof(Radius));
    public static readonly StyledProperty<string?> LabelProperty = AvaloniaProperty.Register<TextField, string?>(nameof(Label));
    public static readonly StyledProperty<string?> PlaceholderProperty = AvaloniaProperty.Register<TextField, string?>(nameof(Placeholder));
    public static readonly StyledProperty<string?> HelperTextProperty = AvaloniaProperty.Register<TextField, string?>(nameof(HelperText));
    public static readonly StyledProperty<string?> ErrorProperty = AvaloniaProperty.Register<TextField, string?>(nameof(Error));
    public static readonly StyledProperty<bool> RequiredProperty = AvaloniaProperty.Register<TextField, bool>(nameof(Required));
    public static readonly StyledProperty<bool> ReadOnlyProperty = AvaloniaProperty.Register<TextField, bool>(nameof(ReadOnly));

    /// <summary>text, password, email, number, search, tel or url. Password masks the input.</summary>
    public static readonly StyledProperty<string> InputTypeProperty = AvaloniaProperty.Register<TextField, string>(nameof(InputType), "text");

    public static readonly StyledProperty<bool> MultilineProperty = AvaloniaProperty.Register<TextField, bool>(nameof(Multiline));
    public static readonly StyledProperty<int> RowsProperty = AvaloniaProperty.Register<TextField, int>(nameof(Rows), 3);
    public static readonly StyledProperty<int?> MaxLengthProperty = AvaloniaProperty.Register<TextField, int?>(nameof(MaxLength));
    public static readonly StyledProperty<bool> CounterProperty = AvaloniaProperty.Register<TextField, bool>(nameof(Counter));
    public static readonly StyledProperty<bool> ClearableProperty = AvaloniaProperty.Register<TextField, bool>(nameof(Clearable));
    public static readonly StyledProperty<string?> PrefixProperty = AvaloniaProperty.Register<TextField, string?>(nameof(Prefix));
    public static readonly StyledProperty<string?> SuffixProperty = AvaloniaProperty.Register<TextField, string?>(nameof(Suffix));
    public static readonly StyledProperty<string?> StartIconProperty = AvaloniaProperty.Register<TextField, string?>(nameof(StartIcon));
    public static readonly StyledProperty<string?> EndIconProperty = AvaloniaProperty.Register<TextField, string?>(nameof(EndIcon));

    /// <summary>Custom leading adornment (overrides <see cref="StartIcon"/>).</summary>
    public static readonly StyledProperty<object?> StartContentProperty = AvaloniaProperty.Register<TextField, object?>(nameof(StartContent));

    /// <summary>Custom trailing adornment (overrides <see cref="EndIcon"/>).</summary>
    public static readonly StyledProperty<object?> EndContentProperty = AvaloniaProperty.Register<TextField, object?>(nameof(EndContent));

    public static readonly StyledProperty<char> PasswordCharProperty = AvaloniaProperty.Register<TextField, char>(nameof(PasswordChar));

    public static readonly DirectProperty<TextField, string?> CounterTextProperty =
        AvaloniaProperty.RegisterDirect<TextField, string?>(nameof(CounterText), f => f.CounterText);

    public static readonly DirectProperty<TextField, double> MultilineHeightProperty =
        AvaloniaProperty.RegisterDirect<TextField, double>(nameof(MultilineHeight), f => f.MultilineHeight);

    private TextBox? _input;
    private Button? _clear;
    private string? _counterText;
    private double _multilineHeight = 76;

    static TextField()
    {
        foreach (var p in new AvaloniaProperty[] { ErrorProperty, HelperTextProperty, RequiredProperty, PrefixProperty, SuffixProperty, LabelProperty,
                     CounterProperty, ClearableProperty, MaxLengthProperty, ReadOnlyProperty, VariantProperty, SizeProperty, RadiusProperty, InputTypeProperty, RowsProperty, MultilineProperty })
            p.Changed.AddClassHandler<TextField>((f, _) => f.UpdateState());
        ValueProperty.Changed.AddClassHandler<TextField>((f, e) =>
        {
            f.UpdateState();
            f.ValueChanged?.Invoke(f, EventArgs.Empty);
        });
        LoadedEvent.AddClassHandler<TextField>((f, _) => f.UpdateState());
        FocusableProperty.OverrideDefaultValue<TextField>(false);
    }

    public string? Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public FieldVariant Variant { get => GetValue(VariantProperty); set => SetValue(VariantProperty, value); }
    public ControlSize Size { get => GetValue(SizeProperty); set => SetValue(SizeProperty, value); }
    public Radius Radius { get => GetValue(RadiusProperty); set => SetValue(RadiusProperty, value); }
    public string? Label { get => GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public string? Placeholder { get => GetValue(PlaceholderProperty); set => SetValue(PlaceholderProperty, value); }
    public string? HelperText { get => GetValue(HelperTextProperty); set => SetValue(HelperTextProperty, value); }

    /// <summary>Error message. Non-empty makes the field invalid and replaces the helper text.</summary>
    public string? Error { get => GetValue(ErrorProperty); set => SetValue(ErrorProperty, value); }

    public bool Required { get => GetValue(RequiredProperty); set => SetValue(RequiredProperty, value); }
    public bool ReadOnly { get => GetValue(ReadOnlyProperty); set => SetValue(ReadOnlyProperty, value); }
    public string InputType { get => GetValue(InputTypeProperty); set => SetValue(InputTypeProperty, value); }
    public bool Multiline { get => GetValue(MultilineProperty); set => SetValue(MultilineProperty, value); }
    public int Rows { get => GetValue(RowsProperty); set => SetValue(RowsProperty, value); }
    public int? MaxLength { get => GetValue(MaxLengthProperty); set => SetValue(MaxLengthProperty, value); }
    public bool Counter { get => GetValue(CounterProperty); set => SetValue(CounterProperty, value); }
    public bool Clearable { get => GetValue(ClearableProperty); set => SetValue(ClearableProperty, value); }
    public string? Prefix { get => GetValue(PrefixProperty); set => SetValue(PrefixProperty, value); }
    public string? Suffix { get => GetValue(SuffixProperty); set => SetValue(SuffixProperty, value); }
    public string? StartIcon { get => GetValue(StartIconProperty); set => SetValue(StartIconProperty, value); }
    public string? EndIcon { get => GetValue(EndIconProperty); set => SetValue(EndIconProperty, value); }
    public object? StartContent { get => GetValue(StartContentProperty); set => SetValue(StartContentProperty, value); }
    public object? EndContent { get => GetValue(EndContentProperty); set => SetValue(EndContentProperty, value); }
    public char PasswordChar { get => GetValue(PasswordCharProperty); set => SetValue(PasswordCharProperty, value); }

    /// <summary>"12" or "12 / 50" when <see cref="Counter"/> is on.</summary>
    public string? CounterText { get => _counterText; private set => SetAndRaise(CounterTextProperty, ref _counterText, value); }

    /// <summary>Minimum height of a multiline field for <see cref="Rows"/> lines.</summary>
    public double MultilineHeight { get => _multilineHeight; private set => SetAndRaise(MultilineHeightProperty, ref _multilineHeight, value); }

    public event EventHandler? ValueChanged;

    public bool IsInvalid => !string.IsNullOrEmpty(Error);

    /// <summary>The inner TextBox (after the template is applied).</summary>
    public TextBox? Input => _input;

    /// <summary>The text shown under the field: the error if invalid, else the helper text.</summary>
    public string? Description => IsInvalid ? Error : HelperText;

    public FieldVariant EffectiveVariant => IsSet(VariantProperty) ? Variant : SlateTheme.Defaults.Field.Variant;
    public ControlSize EffectiveSize => IsSet(SizeProperty) ? Size : SlateTheme.Defaults.Field.Size;
    public Radius EffectiveRadius => IsSet(RadiusProperty) ? Radius : SlateTheme.Defaults.Field.Radius;

    /// <summary>Clears the value and keeps focus in the field.</summary>
    public void Clear()
    {
        SetCurrentValue(ValueProperty, string.Empty);
        _input?.Focus();
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _input = e.NameScope.Find<TextBox>("PART_Input");
        _clear = e.NameScope.Find<Button>("PART_Clear");
        if (_clear is not null)
            _clear.Click += (_, _) => Clear();
        UpdateState();
    }

    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
        if (e.Source == this)
            _input?.Focus();
    }

    private void UpdateState()
    {
        PseudoClasses.Set(":invalid", IsInvalid);
        PseudoClasses.Set(":required", Required);
        PseudoClasses.Set(":has-prefix", !string.IsNullOrEmpty(Prefix));
        PseudoClasses.Set(":has-suffix", !string.IsNullOrEmpty(Suffix));
        PseudoClasses.Set(":counter", Counter);
        PseudoClasses.Set(":clearable", Clearable);
        PseudoClasses.Set(":has-value", !string.IsNullOrEmpty(Value));

        var length = Value?.Length ?? 0;
        CounterText = Counter ? MaxLength is { } max ? $"{length} / {max}" : length.ToString(System.Globalization.CultureInfo.InvariantCulture) : null;
        MultilineHeight = Math.Max(1, Rows) * 21 + 14;

        // Set directly: the button sits in the TextBox's inner content, where template values outrank styles.
        if (_clear is not null)
            _clear.IsVisible = Clearable && !string.IsNullOrEmpty(Value) && !ReadOnly;

        if (_input is null)
            return;
        AutomationProperties.SetName(_input, Label);
        AutomationProperties.SetHelpText(_input, Description);
        AutomationProperties.SetIsRequiredForForm(_input, Required);
        _input.MaxLength = MaxLength ?? 0;
        _input.PasswordChar = InputType == "password" && PasswordChar == default ? '•' : PasswordChar;

        // The inner TextBox carries the look classes so its styles (NativeOverrides) can render the variant.
        _input.Classes.Set("invalid", IsInvalid);
        var variant = EffectiveVariant;
        _input.Classes.Set("filled", variant == FieldVariant.Filled);
        _input.Classes.Set("underlined", variant == FieldVariant.Underlined);
        var size = EffectiveSize;
        _input.Classes.Set("small", size == ControlSize.Small);
        _input.Classes.Set("large", size == ControlSize.Large);
        Slate.Avalonia.Sl.SetOne(_input, Slate.Avalonia.Sl.RadiusClasses, Slate.Avalonia.Sl.ClassFor(EffectiveRadius));
    }
}
