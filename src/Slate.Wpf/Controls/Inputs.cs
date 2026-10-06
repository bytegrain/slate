using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;

namespace Slate.Wpf;

/// <summary>
/// Labelled text input (docs/design/components.md#text-field): visible label, helper text, error state,
/// adornments, counter and clear button. Wires the label, description and invalid state into UI Automation.
/// Options follow design/api/components.json (TextField).
/// </summary>
[TemplatePart(Name = PartTextBox, Type = typeof(TextBox))]
[TemplatePart(Name = PartPasswordBox, Type = typeof(PasswordBox))]
[TemplatePart(Name = PartClear, Type = typeof(ButtonBase))]
public class TextField : Control, IFieldChrome
{
    public const string PartTextBox = "PART_TextBox";
    public const string PartPasswordBox = "PART_PasswordBox";
    public const string PartClear = "PART_Clear";

    private static FrameworkPropertyMetadata Meta(object? defaultValue, PropertyChangedCallback? changed = null) => new(defaultValue, changed);

    private static readonly PropertyChangedCallback Sync = (d, _) => ((TextField)d).SyncState();

    public static readonly DependencyProperty VariantProperty = DependencyProperty.Register(
        nameof(Variant), typeof(FieldVariant), typeof(TextField), Meta(FieldVariant.Outlined, (d, _) => Styling.Refresh(d)));

    private static readonly DependencyPropertyKey ActualVariantPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(ActualVariant), typeof(FieldVariant), typeof(TextField), Meta(FieldVariant.Outlined));

    public static readonly DependencyProperty ActualVariantProperty = ActualVariantPropertyKey.DependencyProperty;

    public static readonly DependencyProperty SizeProperty = Sl.SizeProperty.AddOwner(typeof(TextField));
    public static readonly DependencyProperty RadiusProperty = Sl.RadiusProperty.AddOwner(typeof(TextField));

    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(nameof(Label), typeof(string), typeof(TextField), Meta(null, Sync));

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(string), typeof(TextField),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnValueChanged, null, true, UpdateSourceTrigger.PropertyChanged));

    public static readonly RoutedEvent ValueChangedEvent = EventManager.RegisterRoutedEvent(
        nameof(ValueChanged), RoutingStrategy.Bubble, typeof(RoutedPropertyChangedEventHandler<string>), typeof(TextField));

    public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.Register(nameof(Placeholder), typeof(string), typeof(TextField), Meta(null));
    public static readonly DependencyProperty HelperTextProperty = DependencyProperty.Register(nameof(HelperText), typeof(string), typeof(TextField), Meta(null, Sync));
    public static readonly DependencyProperty ErrorProperty = DependencyProperty.Register(nameof(Error), typeof(string), typeof(TextField), Meta(null, OnErrorChanged));

    private static readonly DependencyPropertyKey HasErrorPropertyKey = DependencyProperty.RegisterReadOnly(nameof(HasError), typeof(bool), typeof(TextField), Meta(false));
    public static readonly DependencyProperty HasErrorProperty = HasErrorPropertyKey.DependencyProperty;

    public static readonly DependencyProperty RequiredProperty = DependencyProperty.Register(nameof(Required), typeof(bool), typeof(TextField), Meta(false, Sync));
    public static readonly DependencyProperty ReadOnlyProperty = DependencyProperty.Register(nameof(ReadOnly), typeof(bool), typeof(TextField), Meta(false));
    public static readonly DependencyProperty InputTypeProperty = DependencyProperty.Register(nameof(InputType), typeof(InputType), typeof(TextField), Meta(InputType.Text));
    public static readonly DependencyProperty MultilineProperty = DependencyProperty.Register(nameof(Multiline), typeof(bool), typeof(TextField), Meta(false));
    public static readonly DependencyProperty RowsProperty = DependencyProperty.Register(nameof(Rows), typeof(int), typeof(TextField), Meta(3));
    public static readonly DependencyProperty MaxLengthProperty = DependencyProperty.Register(nameof(MaxLength), typeof(int?), typeof(TextField), Meta(null, Sync));
    public static readonly DependencyProperty CounterProperty = DependencyProperty.Register(nameof(Counter), typeof(bool), typeof(TextField), Meta(false, Sync));
    public static readonly DependencyProperty ClearableProperty = DependencyProperty.Register(nameof(Clearable), typeof(bool), typeof(TextField), Meta(false, Sync));
    public static readonly DependencyProperty PrefixProperty = DependencyProperty.Register(nameof(Prefix), typeof(string), typeof(TextField), Meta(null));
    public static readonly DependencyProperty SuffixProperty = DependencyProperty.Register(nameof(Suffix), typeof(string), typeof(TextField), Meta(null));
    public static readonly DependencyProperty StartIconProperty = DependencyProperty.Register(nameof(StartIcon), typeof(string), typeof(TextField), Meta(null));
    public static readonly DependencyProperty EndIconProperty = DependencyProperty.Register(nameof(EndIcon), typeof(string), typeof(TextField), Meta(null));
    public static readonly DependencyProperty StartContentProperty = DependencyProperty.Register(nameof(StartContent), typeof(object), typeof(TextField), Meta(null));
    public static readonly DependencyProperty EndContentProperty = DependencyProperty.Register(nameof(EndContent), typeof(object), typeof(TextField), Meta(null));

    private static readonly DependencyPropertyKey CounterTextPropertyKey = DependencyProperty.RegisterReadOnly(nameof(CounterText), typeof(string), typeof(TextField), Meta(""));
    public static readonly DependencyProperty CounterTextProperty = CounterTextPropertyKey.DependencyProperty;

    private static readonly DependencyPropertyKey ShowClearPropertyKey = DependencyProperty.RegisterReadOnly(nameof(ShowClear), typeof(bool), typeof(TextField), Meta(false));
    public static readonly DependencyProperty ShowClearProperty = ShowClearPropertyKey.DependencyProperty;

    private TextBox? _textBox;
    private PasswordBox? _passwordBox;
    private bool _syncingPassword;

    static TextField()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(TextField), new FrameworkPropertyMetadata(typeof(TextField)));
        FocusableProperty.OverrideMetadata(typeof(TextField), new FrameworkPropertyMetadata(false));
        KeyboardNavigation.IsTabStopProperty.OverrideMetadata(typeof(TextField), new FrameworkPropertyMetadata(false));
    }

    public TextField() => Sl.SetKind(this, SlKind.Field);

    /// <summary>Outlined (default), Filled or Underlined. Unset = Defaults.Field.Variant.</summary>
    public FieldVariant Variant { get => (FieldVariant)GetValue(VariantProperty); set => SetValue(VariantProperty, value); }

    /// <summary><see cref="Variant"/> with defaults applied (what the template renders).</summary>
    public FieldVariant ActualVariant => (FieldVariant)GetValue(ActualVariantProperty);

    public ControlSize Size { get => (ControlSize)GetValue(SizeProperty); set => SetValue(SizeProperty, value); }
    public Radius Radius { get => (Radius)GetValue(RadiusProperty); set => SetValue(RadiusProperty, value); }
    public string? Label { get => (string?)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }

    /// <summary>The text. Binds two-way by default.</summary>
    public string Value { get => (string)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }

    public event RoutedPropertyChangedEventHandler<string> ValueChanged { add => AddHandler(ValueChangedEvent, value); remove => RemoveHandler(ValueChangedEvent, value); }

    public string? Placeholder { get => (string?)GetValue(PlaceholderProperty); set => SetValue(PlaceholderProperty, value); }
    public string? HelperText { get => (string?)GetValue(HelperTextProperty); set => SetValue(HelperTextProperty, value); }

    /// <summary>Non-empty marks the field invalid and replaces the helper text.</summary>
    public string? Error { get => (string?)GetValue(ErrorProperty); set => SetValue(ErrorProperty, value); }

    public bool HasError => (bool)GetValue(HasErrorProperty);
    public bool Required { get => (bool)GetValue(RequiredProperty); set => SetValue(RequiredProperty, value); }
    public bool ReadOnly { get => (bool)GetValue(ReadOnlyProperty); set => SetValue(ReadOnlyProperty, value); }

    /// <summary>Text (default), Password (masked), Email, Number, Search, Tel or Url (sets the input scope).</summary>
    public InputType InputType { get => (InputType)GetValue(InputTypeProperty); set => SetValue(InputTypeProperty, value); }

    public bool Multiline { get => (bool)GetValue(MultilineProperty); set => SetValue(MultilineProperty, value); }
    public int Rows { get => (int)GetValue(RowsProperty); set => SetValue(RowsProperty, value); }
    public int? MaxLength { get => (int?)GetValue(MaxLengthProperty); set => SetValue(MaxLengthProperty, value); }

    /// <summary>Shows the value length (and MaxLength) under the field.</summary>
    public bool Counter { get => (bool)GetValue(CounterProperty); set => SetValue(CounterProperty, value); }

    /// <summary>Shows a clear button while the field has text and is editable.</summary>
    public bool Clearable { get => (bool)GetValue(ClearableProperty); set => SetValue(ClearableProperty, value); }

    public string? Prefix { get => (string?)GetValue(PrefixProperty); set => SetValue(PrefixProperty, value); }
    public string? Suffix { get => (string?)GetValue(SuffixProperty); set => SetValue(SuffixProperty, value); }
    public string? StartIcon { get => (string?)GetValue(StartIconProperty); set => SetValue(StartIconProperty, value); }
    public string? EndIcon { get => (string?)GetValue(EndIconProperty); set => SetValue(EndIconProperty, value); }

    /// <summary>Custom leading adornment (overrides <see cref="StartIcon"/>).</summary>
    public object? StartContent { get => GetValue(StartContentProperty); set => SetValue(StartContentProperty, value); }

    /// <summary>Custom trailing adornment (overrides <see cref="EndIcon"/>).</summary>
    public object? EndContent { get => GetValue(EndContentProperty); set => SetValue(EndContentProperty, value); }

    /// <summary>"12" or "12 / 80" when <see cref="Counter"/> is on.</summary>
    public string CounterText => (string)GetValue(CounterTextProperty);

    public bool ShowClear => (bool)GetValue(ShowClearProperty);

    /// <summary>The inner TextBox once the template is applied.</summary>
    public TextBox? TextBox => _textBox;

    /// <summary>Text announced as the field's description: the error when invalid, else the helper.</summary>
    public string? Description => HasError ? Error : HelperText;

    internal void SetActualVariant(FieldVariant v) => SetValue(ActualVariantPropertyKey, v);

    DependencyProperty IFieldChrome.FieldVariantProperty => VariantProperty;
    void IFieldChrome.SetActualVariant(FieldVariant variant) => SetActualVariant(variant);

    /// <summary>Clears the value (what the clear button does) and keeps focus in the field.</summary>
    public void Clear()
    {
        Value = "";
        if (_passwordBox is not null) _passwordBox.Password = "";
        (_passwordBox as UIElement ?? _textBox)?.Focus();
    }

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var field = (TextField)d;
        field.SyncState();
        if (field._passwordBox is { } pb && !field._syncingPassword && pb.Password != (string?)e.NewValue)
            pb.Password = (string?)e.NewValue ?? "";
        field.RaiseEvent(new RoutedPropertyChangedEventArgs<string>((string?)e.OldValue ?? "", (string?)e.NewValue ?? "", ValueChangedEvent));
    }

    private static void OnErrorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var field = (TextField)d;
        field.SetValue(HasErrorPropertyKey, !string.IsNullOrWhiteSpace((string?)e.NewValue));
        field.SyncState();
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _textBox = GetTemplateChild(PartTextBox) as TextBox;

        if (_passwordBox is not null)
            _passwordBox.PasswordChanged -= OnPasswordChanged;
        _passwordBox = GetTemplateChild(PartPasswordBox) as PasswordBox;
        if (_passwordBox is not null)
        {
            _passwordBox.Password = Value ?? "";
            _passwordBox.PasswordChanged += OnPasswordChanged;
        }

        if (GetTemplateChild(PartClear) is ButtonBase clear)
            clear.Click += (_, _) => Clear();

        SyncState();
    }

    private void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        _syncingPassword = true;
        try { Value = _passwordBox!.Password; }
        finally { _syncingPassword = false; }
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        if (ReferenceEquals(e.NewFocus, this))
            (InputType == InputType.Password ? _passwordBox as UIElement : _textBox)?.Focus();
    }

    internal void SyncState()
    {
        var length = (Value ?? "").Length;
        SetValue(CounterTextPropertyKey, MaxLength is { } max ? $"{length} / {max}" : length.ToString(System.Globalization.CultureInfo.CurrentCulture));
        SetValue(ShowClearPropertyKey, Clearable && length > 0 && !ReadOnly && IsEnabled);

        foreach (var input in new Control?[] { _textBox, _passwordBox })
        {
            if (input is null) continue;
            AutomationProperties.SetName(input, Label ?? "");
            AutomationProperties.SetHelpText(input, Description ?? "");
            AutomationProperties.SetIsRequiredForForm(input, Required);
            AutomationProperties.SetItemStatus(input, HasError ? "Invalid" : "");
            Sl.SetHasError(input, HasError);
        }
    }
}

/// <summary>TextField input types (contract: text|password|email|number|search|tel|url).</summary>
public enum InputType
{
    Text,
    Password,
    Email,
    Number,
    Search,
    Tel,
    Url,
}

/// <summary>
/// On/off switch (docs/design/components.md#switch). A ToggleButton exposed to UI Automation as a
/// "switch"; Space toggles, Enter does not.
/// </summary>
public class Switch : ToggleButton
{
    public static readonly DependencyProperty ToneProperty = Sl.ToneProperty.AddOwner(typeof(Switch));
    public static readonly DependencyProperty SizeProperty = Sl.SizeProperty.AddOwner(typeof(Switch));
    public static readonly DependencyProperty LabelPlacementProperty = Sl.LabelPlacementProperty.AddOwner(typeof(Switch));
    public static readonly DependencyProperty DescriptionProperty = Sl.DescriptionProperty.AddOwner(typeof(Switch));
    public static readonly DependencyProperty LabelProperty = Sl.LabelProperty.AddOwner(typeof(Switch));

    public static readonly DependencyProperty SpreadProperty = DependencyProperty.Register(
        nameof(Spread), typeof(bool), typeof(Switch), new FrameworkPropertyMetadata(false));

    /// <summary>0 = off position, 1 = on position (animated by the template).</summary>
    public static readonly DependencyProperty ThumbPositionProperty = DependencyProperty.Register(
        nameof(ThumbPosition), typeof(double), typeof(Switch), new FrameworkPropertyMetadata(0.0));

    private static readonly DependencyPropertyKey ThumbTravelPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(ThumbTravel), typeof(double), typeof(Switch), new FrameworkPropertyMetadata(16.0));

    /// <summary>How far the thumb moves (track width − thumb − margins), measured from the template.</summary>
    public static readonly DependencyProperty ThumbTravelProperty = ThumbTravelPropertyKey.DependencyProperty;

    private static readonly DependencyPropertyKey HasLabelPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(HasLabel), typeof(bool), typeof(Switch), new FrameworkPropertyMetadata(false));

    public static readonly DependencyProperty HasLabelProperty = HasLabelPropertyKey.DependencyProperty;

    private FrameworkElement? _track;
    private FrameworkElement? _thumb;

    static Switch()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(Switch), new FrameworkPropertyMetadata(typeof(Switch)));
        IsThreeStateProperty.OverrideMetadata(typeof(Switch), new FrameworkPropertyMetadata(false));
    }

    public Switch() => Sl.SetKind(this, SlKind.Selection);

    public Tone Tone { get => (Tone)GetValue(ToneProperty); set => SetValue(ToneProperty, value); }
    public ControlSize Size { get => (ControlSize)GetValue(SizeProperty); set => SetValue(SizeProperty, value); }

    /// <summary>Start puts the label before the switch; End (default) after it. Unset = Defaults.Selection.LabelPlacement.</summary>
    public Placement LabelPlacement { get => (Placement)GetValue(LabelPlacementProperty); set => SetValue(LabelPlacementProperty, value); }

    public string? Description { get => (string?)GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }

    /// <summary>Text label (alternative to Content; also the accessible name).</summary>
    public string? Label { get => (string?)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }

    /// <summary>Label and switch at opposite ends of the row.</summary>
    public bool Spread { get => (bool)GetValue(SpreadProperty); set => SetValue(SpreadProperty, value); }

    public double ThumbPosition { get => (double)GetValue(ThumbPositionProperty); set => SetValue(ThumbPositionProperty, value); }

    public double ThumbTravel => (double)GetValue(ThumbTravelProperty);

    public bool HasLabel => (bool)GetValue(HasLabelProperty);

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        if (_track is not null) _track.SizeChanged -= OnPartSizeChanged;
        if (_thumb is not null) _thumb.SizeChanged -= OnPartSizeChanged;
        _track = GetTemplateChild("TrackSite") as FrameworkElement;
        _thumb = GetTemplateChild("Thumb") as FrameworkElement;
        if (_track is not null) _track.SizeChanged += OnPartSizeChanged;
        if (_thumb is not null) _thumb.SizeChanged += OnPartSizeChanged;
        ThumbPosition = IsChecked == true ? 1 : 0;
    }

    private void OnPartSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_track is not null && _thumb is not null)
            SetValue(ThumbTravelPropertyKey, Math.Max(0, _track.ActualWidth - _thumb.ActualWidth - _thumb.Margin.Left - _thumb.Margin.Right));
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == LabelProperty)
            SetValue(HasLabelPropertyKey, !string.IsNullOrEmpty((string?)e.NewValue));
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        // Enter must not toggle (it submits forms / activates default buttons); Space toggles.
        if (e.Key == Key.Enter)
            return;
        base.OnKeyDown(e);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new SwitchAutomationPeer(this);

    private sealed class SwitchAutomationPeer(Switch owner) : ToggleButtonAutomationPeer(owner)
    {
        protected override string GetLocalizedControlTypeCore() => "switch";
        protected override string GetClassNameCore() => nameof(Switch);
    }
}

/// <summary>
/// A group of radio buttons (docs/design/components.md#radio-group) with a label, a bound <see cref="Value"/>,
/// one tab stop and arrow-key selection. Items may be RadioButtons (their Tag, else Content, is the value) or plain
/// values (rendered as RadioButtons).
/// </summary>
public class RadioGroup : ItemsControl
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(object), typeof(RadioGroup),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, e) => ((RadioGroup)d).OnValueChanged(e)));

    public static readonly RoutedEvent ValueChangedEvent = EventManager.RegisterRoutedEvent(
        nameof(ValueChanged), RoutingStrategy.Bubble, typeof(RoutedPropertyChangedEventHandler<object?>), typeof(RadioGroup));

    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label), typeof(string), typeof(RadioGroup), new FrameworkPropertyMetadata(null, (d, e) => AutomationProperties.SetName(d, (string?)e.NewValue ?? "")));

    public static readonly DependencyProperty DirectionProperty = DependencyProperty.Register(
        nameof(Direction), typeof(Direction), typeof(RadioGroup), new FrameworkPropertyMetadata(Direction.Column));

    public static readonly DependencyProperty ToneProperty = Sl.ToneProperty.AddOwner(typeof(RadioGroup));
    public static readonly DependencyProperty SizeProperty = Sl.SizeProperty.AddOwner(typeof(RadioGroup));

    private readonly string _groupName = "slate-radio-" + Guid.NewGuid().ToString("N");
    private bool _updating;

    static RadioGroup()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(RadioGroup), new FrameworkPropertyMetadata(typeof(RadioGroup)));
        FocusableProperty.OverrideMetadata(typeof(RadioGroup), new FrameworkPropertyMetadata(false));
    }

    public RadioGroup()
    {
        KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.Once);
        AddHandler(ToggleButton.CheckedEvent, new RoutedEventHandler(OnItemChecked));
    }

    public object? Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }

    public event RoutedPropertyChangedEventHandler<object?> ValueChanged { add => AddHandler(ValueChangedEvent, value); remove => RemoveHandler(ValueChangedEvent, value); }

    public string? Label { get => (string?)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public Direction Direction { get => (Direction)GetValue(DirectionProperty); set => SetValue(DirectionProperty, value); }
    public Tone Tone { get => (Tone)GetValue(ToneProperty); set => SetValue(ToneProperty, value); }
    public ControlSize Size { get => (ControlSize)GetValue(SizeProperty); set => SetValue(SizeProperty, value); }

    /// <summary>The value a radio stands for: Tag when set, else Content.</summary>
    public static object? ValueOf(RadioButton radio) => radio.Tag ?? radio.Content;

    protected override bool IsItemItsOwnContainerOverride(object item) => item is RadioButton;

    protected override DependencyObject GetContainerForItemOverride() => new RadioButton();

    protected override void PrepareContainerForItemOverride(DependencyObject element, object item)
    {
        base.PrepareContainerForItemOverride(element, item);
        if (element is not RadioButton radio)
            return;

        radio.GroupName = _groupName;
        if (!ReferenceEquals(radio, item) && radio.Tag is null)
            radio.Tag = item;
        if (!Styling.IsSet(radio, Sl.ToneProperty))
            radio.SetBinding(Sl.ToneProperty, new Binding(nameof(Tone)) { Source = this });
        if (!Styling.IsSet(radio, Sl.SizeProperty))
            radio.SetBinding(Sl.SizeProperty, new Binding(nameof(Size)) { Source = this });
        radio.IsChecked = Equals(ValueOf(radio), Value);
        KeyboardNavigation.SetIsTabStop(radio, radio.IsChecked == true || (Value is null && Items.IndexOf(item) == 0));
    }

    private IEnumerable<RadioButton> Radios =>
        Items.Cast<object>().Select(i => ItemContainerGenerator.ContainerFromItem(i)).OfType<RadioButton>();

    private void OnItemChecked(object sender, RoutedEventArgs e)
    {
        if (_updating || e.OriginalSource is not RadioButton radio)
            return;
        Value = ValueOf(radio);
    }

    private void OnValueChanged(DependencyPropertyChangedEventArgs e)
    {
        _updating = true;
        try
        {
            foreach (var radio in Radios)
            {
                var on = Equals(ValueOf(radio), e.NewValue);
                radio.IsChecked = on;
                KeyboardNavigation.SetIsTabStop(radio, on);
            }
        }
        finally { _updating = false; }
        RaiseEvent(new RoutedPropertyChangedEventArgs<object?>(e.OldValue, e.NewValue, ValueChangedEvent));
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var step = e.Key switch
        {
            Key.Down or Key.Right => 1,
            Key.Up or Key.Left => -1,
            _ => 0,
        };
        if (step == 0) return;

        var radios = Radios.Where(r => r.IsEnabled).ToList();
        if (radios.Count == 0) return;
        var index = radios.FindIndex(r => r.IsChecked == true);
        var next = radios[((index < 0 ? 0 : index + step) % radios.Count + radios.Count) % radios.Count];
        next.IsChecked = true;
        next.Focus();
        e.Handled = true;
    }
}
