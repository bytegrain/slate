using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Slate.Dates;

namespace Slate.Wpf;

/// <summary>A day cell of <see cref="CalendarView"/> (Slate.Core <see cref="CalendarDay"/> plus focus).</summary>
public sealed record CalendarDayView(CalendarDay Day, bool IsFocused, string AccessibleName)
{
    public DateOnly Date => Day.Date;
    public string Text => Day.Date.Day.ToString(CultureInfo.CurrentCulture);
}

public sealed record CalendarWeekView(int IsoWeek, IReadOnlyList<CalendarDayView> Days);

public class DatePickedEventArgs(RoutedEvent e, object source, DateOnly date) : RoutedEventArgs(e, source)
{
    public DateOnly Date { get; } = date;
}

/// <summary>
/// The month calendar behind <see cref="DatePicker"/> (usable on its own): Slate.Core <see cref="CalendarModel"/> grid,
/// keyboard (arrows, Home/End week, PageUp/PageDown month, Shift+PageUp/PageDown year), range picking with hover
/// preview, presets.
/// </summary>
public class CalendarView : Control
{
    private static readonly PropertyChangedCallback Rebuild = (d, _) => ((CalendarView)d).Build();

    public static readonly DependencyProperty SelectionProperty = DependencyProperty.Register(nameof(Selection), typeof(DateSelection), typeof(CalendarView), new FrameworkPropertyMetadata(DateSelection.Single, Rebuild));
    public static readonly DependencyProperty SelectedDateProperty = DependencyProperty.Register(nameof(SelectedDate), typeof(DateOnly?), typeof(CalendarView), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, Rebuild));
    public static readonly DependencyProperty RangeProperty = DependencyProperty.Register(nameof(Range), typeof(DateRange?), typeof(CalendarView), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, Rebuild));
    public static readonly DependencyProperty MinProperty = DependencyProperty.Register(nameof(Min), typeof(DateOnly?), typeof(CalendarView), new FrameworkPropertyMetadata(null, Rebuild));
    public static readonly DependencyProperty MaxProperty = DependencyProperty.Register(nameof(Max), typeof(DateOnly?), typeof(CalendarView), new FrameworkPropertyMetadata(null, Rebuild));
    public static readonly DependencyProperty DisabledDatesProperty = DependencyProperty.Register(nameof(DisabledDates), typeof(Func<DateOnly, bool>), typeof(CalendarView), new FrameworkPropertyMetadata(null, Rebuild));
    public static readonly DependencyProperty FirstDayOfWeekProperty = DependencyProperty.Register(nameof(FirstDayOfWeek), typeof(DayOfWeek?), typeof(CalendarView), new FrameworkPropertyMetadata(null, Rebuild));
    public static readonly DependencyProperty TodayProperty = DependencyProperty.Register(nameof(Today), typeof(DateOnly?), typeof(CalendarView), new FrameworkPropertyMetadata(null, Rebuild));
    public static readonly DependencyProperty PresetsProperty = DependencyProperty.Register(nameof(Presets), typeof(IEnumerable<DatePreset>), typeof(CalendarView), new FrameworkPropertyMetadata(null));
    public static readonly DependencyProperty FocusedDateProperty = DependencyProperty.Register(nameof(FocusedDate), typeof(DateOnly), typeof(CalendarView), new FrameworkPropertyMetadata(DateOnly.FromDateTime(DateTime.Today), Rebuild));

    private static readonly DependencyPropertyKey WeeksPropertyKey = DependencyProperty.RegisterReadOnly(nameof(Weeks), typeof(IReadOnlyList<CalendarWeekView>), typeof(CalendarView), new FrameworkPropertyMetadata(Array.Empty<CalendarWeekView>()));
    public static readonly DependencyProperty WeeksProperty = WeeksPropertyKey.DependencyProperty;

    private static readonly DependencyPropertyKey WeekdaysPropertyKey = DependencyProperty.RegisterReadOnly(nameof(Weekdays), typeof(IReadOnlyList<string>), typeof(CalendarView), new FrameworkPropertyMetadata(Array.Empty<string>()));
    public static readonly DependencyProperty WeekdaysProperty = WeekdaysPropertyKey.DependencyProperty;

    private static readonly DependencyPropertyKey TitlePropertyKey = DependencyProperty.RegisterReadOnly(nameof(Title), typeof(string), typeof(CalendarView), new FrameworkPropertyMetadata(""));
    public static readonly DependencyProperty TitleProperty = TitlePropertyKey.DependencyProperty;

    public static readonly RoutedEvent DatePickedEvent = EventManager.RegisterRoutedEvent(nameof(DatePicked), RoutingStrategy.Bubble, typeof(EventHandler<DatePickedEventArgs>), typeof(CalendarView));
    public static readonly RoutedEvent RangePickedEvent = EventManager.RegisterRoutedEvent(nameof(RangePicked), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(CalendarView));

    private DateOnly? _hover;
    private bool _building;

    static CalendarView()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(CalendarView), new FrameworkPropertyMetadata(typeof(CalendarView)));
        FocusableProperty.OverrideMetadata(typeof(CalendarView), new FrameworkPropertyMetadata(false));
        KeyboardNavigation.IsTabStopProperty.OverrideMetadata(typeof(CalendarView), new FrameworkPropertyMetadata(false));
    }

    public CalendarView()
    {
        AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(OnClick));
        Build();
    }

    public DateSelection Selection { get => (DateSelection)GetValue(SelectionProperty); set => SetValue(SelectionProperty, value); }
    public DateOnly? SelectedDate { get => (DateOnly?)GetValue(SelectedDateProperty); set => SetValue(SelectedDateProperty, value); }
    public DateRange? Range { get => (DateRange?)GetValue(RangeProperty); set => SetValue(RangeProperty, value); }
    public DateOnly? Min { get => (DateOnly?)GetValue(MinProperty); set => SetValue(MinProperty, value); }
    public DateOnly? Max { get => (DateOnly?)GetValue(MaxProperty); set => SetValue(MaxProperty, value); }
    public Func<DateOnly, bool>? DisabledDates { get => (Func<DateOnly, bool>?)GetValue(DisabledDatesProperty); set => SetValue(DisabledDatesProperty, value); }

    /// <summary>Null = the current culture's first day.</summary>
    public DayOfWeek? FirstDayOfWeek { get => (DayOfWeek?)GetValue(FirstDayOfWeekProperty); set => SetValue(FirstDayOfWeekProperty, value); }

    /// <summary>"Today" for highlighting and presets (null = the system date; set it in tests).</summary>
    public DateOnly? Today { get => (DateOnly?)GetValue(TodayProperty); set => SetValue(TodayProperty, value); }

    public IEnumerable<DatePreset>? Presets { get => (IEnumerable<DatePreset>?)GetValue(PresetsProperty); set => SetValue(PresetsProperty, value); }

    /// <summary>The keyboard-focused day; its month is the one shown.</summary>
    public DateOnly FocusedDate { get => (DateOnly)GetValue(FocusedDateProperty); set => SetValue(FocusedDateProperty, value); }

    public IReadOnlyList<CalendarWeekView> Weeks => (IReadOnlyList<CalendarWeekView>)GetValue(WeeksProperty);
    public IReadOnlyList<string> Weekdays => (IReadOnlyList<string>)GetValue(WeekdaysProperty);
    public string Title => (string)GetValue(TitleProperty);

    public event EventHandler<DatePickedEventArgs> DatePicked { add => AddHandler(DatePickedEvent, value); remove => RemoveHandler(DatePickedEvent, value); }

    /// <summary>Raised when a range is completed (both ends picked) or a preset is chosen.</summary>
    public event RoutedEventHandler RangePicked { add => AddHandler(RangePickedEvent, value); remove => RemoveHandler(RangePickedEvent, value); }

    private DateOnly TodayValue => Today ?? DateOnly.FromDateTime(DateTime.Today);

    internal CalendarOptions Options() => new()
    {
        FirstDayOfWeek = FirstDayOfWeek ?? CalendarModel.FirstDayOfWeek(CultureInfo.CurrentCulture),
        Today = TodayValue,
        Min = Min,
        Max = Max,
        IsDateDisabled = DisabledDates,
        Selected = Selection == DateSelection.Single ? SelectedDate : null,
        Range = Selection == DateSelection.Range ? Range : null,
        Hover = Selection == DateSelection.Range ? _hover : null,
    };

    private void Build()
    {
        if (_building) return;
        _building = true;
        try
        {
            var focused = FocusedDate;
            var month = CalendarModel.Build(focused.Year, focused.Month, Options());
            var format = CultureInfo.CurrentCulture.DateTimeFormat;
            SetValue(WeekdaysPropertyKey, month.Weekdays.Select(d => format.GetShortestDayName(d)).ToList());
            SetValue(TitlePropertyKey, new DateTime(month.Year, month.Month, 1).ToString("MMMM yyyy", CultureInfo.CurrentCulture));
            SetValue(WeeksPropertyKey, month.Weeks.Select(w => new CalendarWeekView(w.IsoWeek,
                w.Days.Select(d => new CalendarDayView(d, d.Date == focused, Describe(d))).ToList())).ToList());
        }
        finally
        {
            _building = false;
        }
    }

    private static string Describe(CalendarDay d)
    {
        var name = d.Date.ToString("D", CultureInfo.CurrentCulture);
        if (d.IsToday) name += ", today";
        if (d.IsSelected || d.IsRangeStart || d.IsRangeEnd) name += ", selected";
        if (d.IsDisabled) name += ", unavailable";
        return name;
    }

    /// <summary>Picks a date as a click would (respects disabled days and range mode).</summary>
    public void Pick(DateOnly date)
    {
        if (Options().IsDisabled(date))
            return;
        SetCurrentValue(FocusedDateProperty, date);
        if (Selection == DateSelection.Range)
        {
            var range = CalendarModel.PickRange(Range, date);
            _hover = null;
            SetCurrentValue(RangeProperty, range);
            if (range.End is not null)
                RaiseEvent(new RoutedEventArgs(RangePickedEvent, this));
        }
        else
        {
            SetCurrentValue(SelectedDateProperty, date);
        }
        Build();
        RaiseEvent(new DatePickedEventArgs(DatePickedEvent, this, date));
    }

    public void ApplyPreset(DatePreset preset)
    {
        var range = CalendarModel.Resolve(preset.Kind, TodayValue);
        if (Selection == DateSelection.Range)
        {
            SetCurrentValue(RangeProperty, range);
            SetCurrentValue(FocusedDateProperty, range.End ?? range.Start);
            RaiseEvent(new RoutedEventArgs(RangePickedEvent, this));
        }
        else
        {
            SetCurrentValue(SelectedDateProperty, range.Start);
            SetCurrentValue(FocusedDateProperty, range.Start);
            RaiseEvent(new DatePickedEventArgs(DatePickedEvent, this, range.Start));
        }
    }

    public void ShowMonth(int delta) => SetCurrentValue(FocusedDateProperty, FocusedDate.AddMonths(delta));

    private void OnClick(object sender, RoutedEventArgs e)
    {
        switch (e.OriginalSource)
        {
            case FrameworkElement { Name: "PART_Previous" }:
                ShowMonth(-1);
                break;
            case FrameworkElement { Name: "PART_Next" }:
                ShowMonth(1);
                break;
            case FrameworkElement { DataContext: CalendarDayView day }:
                Pick(day.Date);
                FocusDay();
                break;
            case FrameworkElement { DataContext: DatePreset preset }:
                ApplyPreset(preset);
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.OriginalSource is not FrameworkElement { DataContext: CalendarDayView })
            return;

        CalendarKey? key = e.Key switch
        {
            System.Windows.Input.Key.Left => CalendarKey.Left,
            System.Windows.Input.Key.Right => CalendarKey.Right,
            System.Windows.Input.Key.Up => CalendarKey.Up,
            System.Windows.Input.Key.Down => CalendarKey.Down,
            System.Windows.Input.Key.Home => CalendarKey.Home,
            System.Windows.Input.Key.End => CalendarKey.End,
            System.Windows.Input.Key.PageUp => CalendarKey.PageUp,
            System.Windows.Input.Key.PageDown => CalendarKey.PageDown,
            _ => null,
        };
        if (key is null)
            return;

        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        SetCurrentValue(FocusedDateProperty, CalendarModel.Navigate(FocusedDate, key.Value, shift, Options()));
        FocusDay();
        e.Handled = true;
    }

    /// <summary>Range mode: previews the range ending at the hovered day.</summary>
    internal void Hover(DateOnly? date)
    {
        if (Selection != DateSelection.Range || Range is not { End: null } || _hover == date)
            return;
        _hover = date;
        Build();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        Hover(e.OriginalSource is FrameworkElement { DataContext: CalendarDayView d } ? d.Date : null);
    }

    /// <summary>Moves keyboard focus to the focused day's cell (roving tab stop).</summary>
    public void FocusDay() => Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, () =>
    {
        foreach (var button in FindDays(this))
        {
            if (button.DataContext is CalendarDayView { IsFocused: true })
            {
                button.Focus();
                return;
            }
        }
    });

    private static IEnumerable<ButtonBase> FindDays(DependencyObject root)
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is ButtonBase { DataContext: CalendarDayView } b)
                yield return b;
            foreach (var nested in FindDays(child))
                yield return nested;
        }
    }
}

/// <summary>
/// Date field with a calendar popup (design/api/components.json "DatePicker"): single date or range, typed input
/// (culture short date or <see cref="Format"/>; Enter or leaving the field commits), min/max/disabled days, presets,
/// or an <see cref="Inline"/> calendar without the field. Shares the TextField chrome.
/// </summary>
[TemplatePart(Name = PartInput, Type = typeof(TextBox))]
[TemplatePart(Name = PartPopup, Type = typeof(Popup))]
[TemplatePart(Name = PartCalendar, Type = typeof(CalendarView))]
[TemplatePart(Name = PartField, Type = typeof(FrameworkElement))]
public class DatePicker : Control, IFieldChrome
{
    public const string PartInput = "PART_Input";
    public const string PartPopup = "PART_Popup";
    public const string PartCalendar = "PART_Calendar";
    public const string PartField = "PART_Field";

    private static readonly PropertyChangedCallback SyncText = (d, _) => ((DatePicker)d).UpdateText();

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(DateOnly?), typeof(DatePicker), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnValueChanged));

    public static readonly DependencyProperty RangeProperty = DependencyProperty.Register(
        nameof(Range), typeof(DateRange?), typeof(DatePicker), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnRangeChanged));

    public static readonly DependencyProperty SelectionProperty = DependencyProperty.Register(nameof(Selection), typeof(DateSelection), typeof(DatePicker), new FrameworkPropertyMetadata(DateSelection.Single, SyncText));
    public static readonly DependencyProperty MinProperty = DependencyProperty.Register(nameof(Min), typeof(DateOnly?), typeof(DatePicker), new FrameworkPropertyMetadata(null));
    public static readonly DependencyProperty MaxProperty = DependencyProperty.Register(nameof(Max), typeof(DateOnly?), typeof(DatePicker), new FrameworkPropertyMetadata(null));
    public static readonly DependencyProperty DisabledDatesProperty = DependencyProperty.Register(nameof(DisabledDates), typeof(Func<DateOnly, bool>), typeof(DatePicker), new FrameworkPropertyMetadata(null));
    public static readonly DependencyProperty FirstDayOfWeekProperty = DependencyProperty.Register(nameof(FirstDayOfWeek), typeof(DayOfWeek?), typeof(DatePicker), new FrameworkPropertyMetadata(null));
    public static readonly DependencyProperty FormatProperty = DependencyProperty.Register(nameof(Format), typeof(string), typeof(DatePicker), new FrameworkPropertyMetadata(null, SyncText));
    public static readonly DependencyProperty PresetsProperty = DependencyProperty.Register(nameof(Presets), typeof(IEnumerable<DatePreset>), typeof(DatePicker), new FrameworkPropertyMetadata(null));
    public static readonly DependencyProperty TodayProperty = DependencyProperty.Register(nameof(Today), typeof(DateOnly?), typeof(DatePicker), new FrameworkPropertyMetadata(null));

    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label), typeof(string), typeof(DatePicker), new FrameworkPropertyMetadata(null, (d, e) => AutomationProperties.SetName(d, (string?)e.NewValue ?? "")));

    public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.Register(nameof(Placeholder), typeof(string), typeof(DatePicker), new FrameworkPropertyMetadata(null));
    public static readonly DependencyProperty HelperTextProperty = DependencyProperty.Register(nameof(HelperText), typeof(string), typeof(DatePicker), new FrameworkPropertyMetadata(null, OnHelpChanged));
    public static readonly DependencyProperty ErrorProperty = DependencyProperty.Register(nameof(Error), typeof(string), typeof(DatePicker), new FrameworkPropertyMetadata(null, OnHelpChanged));

    public static readonly DependencyProperty VariantProperty = DependencyProperty.Register(
        nameof(Variant), typeof(FieldVariant), typeof(DatePicker), new FrameworkPropertyMetadata(FieldVariant.Outlined, (d, _) => Styling.Refresh(d)));

    private static readonly DependencyPropertyKey ActualVariantPropertyKey = DependencyProperty.RegisterReadOnly(nameof(ActualVariant), typeof(FieldVariant), typeof(DatePicker), new FrameworkPropertyMetadata(FieldVariant.Outlined));
    public static readonly DependencyProperty ActualVariantProperty = ActualVariantPropertyKey.DependencyProperty;

    public static readonly DependencyProperty SizeProperty = Sl.SizeProperty.AddOwner(typeof(DatePicker));
    public static readonly DependencyProperty RadiusProperty = Sl.RadiusProperty.AddOwner(typeof(DatePicker));
    public static readonly DependencyProperty ClearableProperty = DependencyProperty.Register(nameof(Clearable), typeof(bool), typeof(DatePicker), new FrameworkPropertyMetadata(false, SyncText));
    public static readonly DependencyProperty InlineProperty = DependencyProperty.Register(nameof(Inline), typeof(bool), typeof(DatePicker), new FrameworkPropertyMetadata(false));

    public static readonly DependencyProperty IsDropDownOpenProperty = DependencyProperty.Register(
        nameof(IsDropDownOpen), typeof(bool), typeof(DatePicker), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnOpenChanged));

    public static readonly DependencyProperty InputTextProperty = DependencyProperty.Register(nameof(InputText), typeof(string), typeof(DatePicker), new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    private static readonly DependencyPropertyKey HasErrorPropertyKey = DependencyProperty.RegisterReadOnly(nameof(HasError), typeof(bool), typeof(DatePicker), new FrameworkPropertyMetadata(false));
    public static readonly DependencyProperty HasErrorProperty = HasErrorPropertyKey.DependencyProperty;

    private static readonly DependencyPropertyKey ShowClearPropertyKey = DependencyProperty.RegisterReadOnly(nameof(ShowClear), typeof(bool), typeof(DatePicker), new FrameworkPropertyMetadata(false));
    public static readonly DependencyProperty ShowClearProperty = ShowClearPropertyKey.DependencyProperty;

    public static readonly RoutedEvent ValueChangedEvent = EventManager.RegisterRoutedEvent(nameof(ValueChanged), RoutingStrategy.Bubble, typeof(RoutedPropertyChangedEventHandler<DateOnly?>), typeof(DatePicker));
    public static readonly RoutedEvent RangeChangedEvent = EventManager.RegisterRoutedEvent(nameof(RangeChanged), RoutingStrategy.Bubble, typeof(RoutedPropertyChangedEventHandler<DateRange?>), typeof(DatePicker));

    private TextBox? _input;
    private Popup? _popup;
    private CalendarView? _calendar;
    private FrameworkElement? _field;

    static DatePicker()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(DatePicker), new FrameworkPropertyMetadata(typeof(DatePicker)));
        FocusableProperty.OverrideMetadata(typeof(DatePicker), new FrameworkPropertyMetadata(false));
        KeyboardNavigation.IsTabStopProperty.OverrideMetadata(typeof(DatePicker), new FrameworkPropertyMetadata(false));
    }

    public DatePicker()
    {
        Sl.SetKind(this, SlKind.Field);
        AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(OnButtonClick));
        AddHandler(CalendarView.DatePickedEvent, new EventHandler<DatePickedEventArgs>(OnDatePicked));
        AddHandler(CalendarView.RangePickedEvent, new RoutedEventHandler(OnRangePicked));
    }

    public DateOnly? Value { get => (DateOnly?)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public DateRange? Range { get => (DateRange?)GetValue(RangeProperty); set => SetValue(RangeProperty, value); }
    public DateSelection Selection { get => (DateSelection)GetValue(SelectionProperty); set => SetValue(SelectionProperty, value); }
    public DateOnly? Min { get => (DateOnly?)GetValue(MinProperty); set => SetValue(MinProperty, value); }
    public DateOnly? Max { get => (DateOnly?)GetValue(MaxProperty); set => SetValue(MaxProperty, value); }
    public Func<DateOnly, bool>? DisabledDates { get => (Func<DateOnly, bool>?)GetValue(DisabledDatesProperty); set => SetValue(DisabledDatesProperty, value); }
    public DayOfWeek? FirstDayOfWeek { get => (DayOfWeek?)GetValue(FirstDayOfWeekProperty); set => SetValue(FirstDayOfWeekProperty, value); }

    /// <summary>Display/parse pattern (e.g. "yyyy-MM-dd"); null = the culture's short date.</summary>
    public string? Format { get => (string?)GetValue(FormatProperty); set => SetValue(FormatProperty, value); }

    /// <summary>Quick picks shown beside the calendar (e.g. <see cref="CalendarModel.DefaultPresets"/>).</summary>
    public IEnumerable<DatePreset>? Presets { get => (IEnumerable<DatePreset>?)GetValue(PresetsProperty); set => SetValue(PresetsProperty, value); }

    /// <summary>"Today" (null = system date; set in tests).</summary>
    public DateOnly? Today { get => (DateOnly?)GetValue(TodayProperty); set => SetValue(TodayProperty, value); }

    public string? Label { get => (string?)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public string? Placeholder { get => (string?)GetValue(PlaceholderProperty); set => SetValue(PlaceholderProperty, value); }
    public string? HelperText { get => (string?)GetValue(HelperTextProperty); set => SetValue(HelperTextProperty, value); }
    public string? Error { get => (string?)GetValue(ErrorProperty); set => SetValue(ErrorProperty, value); }
    public FieldVariant Variant { get => (FieldVariant)GetValue(VariantProperty); set => SetValue(VariantProperty, value); }
    public FieldVariant ActualVariant => (FieldVariant)GetValue(ActualVariantProperty);
    public ControlSize Size { get => (ControlSize)GetValue(SizeProperty); set => SetValue(SizeProperty, value); }
    public Radius Radius { get => (Radius)GetValue(RadiusProperty); set => SetValue(RadiusProperty, value); }
    public bool Clearable { get => (bool)GetValue(ClearableProperty); set => SetValue(ClearableProperty, value); }

    /// <summary>Show the calendar in place, without the field and popup.</summary>
    public bool Inline { get => (bool)GetValue(InlineProperty); set => SetValue(InlineProperty, value); }

    public bool IsDropDownOpen { get => (bool)GetValue(IsDropDownOpenProperty); set => SetValue(IsDropDownOpenProperty, value); }

    /// <summary>The text in the field (committed on Enter / focus loss).</summary>
    public string InputText { get => (string)GetValue(InputTextProperty); set => SetValue(InputTextProperty, value); }

    public bool HasError => (bool)GetValue(HasErrorProperty);
    public bool ShowClear => (bool)GetValue(ShowClearProperty);

    public event RoutedPropertyChangedEventHandler<DateOnly?> ValueChanged { add => AddHandler(ValueChangedEvent, value); remove => RemoveHandler(ValueChangedEvent, value); }
    public event RoutedPropertyChangedEventHandler<DateRange?> RangeChanged { add => AddHandler(RangeChangedEvent, value); remove => RemoveHandler(RangeChangedEvent, value); }

    DependencyProperty IFieldChrome.FieldVariantProperty => VariantProperty;
    void IFieldChrome.SetActualVariant(FieldVariant variant) => SetValue(ActualVariantPropertyKey, variant);

    /// <summary>The pattern in use for display and parsing.</summary>
    public string Pattern => Format ?? DateText.ShortPattern(CultureInfo.CurrentCulture);

    internal string FormatValue() => Selection == DateSelection.Range
        ? Range is { } r ? r.End is { } end ? $"{DateText.Format(r.Start, Pattern)} – {DateText.Format(end, Pattern)}" : DateText.Format(r.Start, Pattern) + " –" : ""
        : Value is { } v ? DateText.Format(v, Pattern) : "";

    private void UpdateText()
    {
        SetCurrentValue(InputTextProperty, FormatValue());
        var has = Selection == DateSelection.Range ? Range is not null : Value is not null;
        SetValue(ShowClearPropertyKey, Clearable && has && IsEnabled);
    }

    /// <summary>Parses <see cref="InputText"/>; returns false (and sets nothing) when it isn't a valid, enabled date.</summary>
    public bool Commit()
    {
        var text = InputText?.Trim() ?? "";
        if (text.Length == 0)
        {
            if (Selection == DateSelection.Range) SetCurrentValue(RangeProperty, null);
            else SetCurrentValue(ValueProperty, null);
            return true;
        }

        var calendar = new CalendarOptions { Min = Min, Max = Max, IsDateDisabled = DisabledDates };
        if (Selection == DateSelection.Range)
        {
            var parts = text.Split(['–', '—'], 2);
            var start = DateText.Parse(parts[0].Trim(), Pattern);
            var end = parts.Length > 1 && parts[1].Trim().Length > 0 ? DateText.Parse(parts[1].Trim(), Pattern) : null;
            if (start is null || calendar.IsDisabled(start.Value) || (end is { } e && (e < start || calendar.IsDisabled(e))))
            {
                UpdateText();
                return false;
            }
            SetCurrentValue(RangeProperty, new DateRange(start.Value, end));
        }
        else
        {
            if (DateText.Parse(text, Pattern) is not { } date || calendar.IsDisabled(date))
            {
                UpdateText();
                return false;
            }
            SetCurrentValue(ValueProperty, date);
        }
        UpdateText();
        return true;
    }

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var p = (DatePicker)d;
        p.UpdateText();
        p.RaiseEvent(new RoutedPropertyChangedEventArgs<DateOnly?>((DateOnly?)e.OldValue, (DateOnly?)e.NewValue, ValueChangedEvent));
    }

    private static void OnRangeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var p = (DatePicker)d;
        p.UpdateText();
        p.RaiseEvent(new RoutedPropertyChangedEventArgs<DateRange?>((DateRange?)e.OldValue, (DateRange?)e.NewValue, RangeChangedEvent));
    }

    private static void OnHelpChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var p = (DatePicker)d;
        var error = !string.IsNullOrEmpty(p.Error);
        p.SetValue(HasErrorPropertyKey, error);
        Sl.SetHasError(p, error);
        AutomationProperties.SetHelpText(p, error ? p.Error! : p.HelperText ?? "");
    }

    private static void OnOpenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var p = (DatePicker)d;
        var open = (bool)e.NewValue;
        if (p._popup is not null)
            p._popup.IsOpen = open;
        if (open && p._calendar is not null)
        {
            p._calendar.SetCurrentValue(CalendarView.FocusedDateProperty,
                (p.Selection == DateSelection.Range ? p.Range?.Start : p.Value) ?? p.Today ?? DateOnly.FromDateTime(DateTime.Today));
            p._calendar.FocusDay();
        }
        else if (!open && p._popup?.Child is UIElement child && child.IsKeyboardFocusWithin)
        {
            p._input?.Focus();
        }
        if (UIElementAutomationPeer.FromElement(p) is DatePickerAutomationPeer peer)
            peer.RaiseExpandCollapse((bool)e.OldValue, open);
    }

    public override void OnApplyTemplate()
    {
        if (_input is not null)
        {
            _input.PreviewKeyDown -= OnInputKeyDown;
            _input.LostKeyboardFocus -= OnInputLostFocus;
        }
        if (_popup is not null)
        {
            _popup.Closed -= OnPopupClosed;
            _popup.PreviewKeyDown -= OnPopupKeyDown;
        }

        base.OnApplyTemplate();
        _input = GetTemplateChild(PartInput) as TextBox;
        _popup = GetTemplateChild(PartPopup) as Popup;
        _calendar = GetTemplateChild(PartCalendar) as CalendarView;
        _field = GetTemplateChild(PartField) as FrameworkElement;

        if (_input is not null)
        {
            _input.PreviewKeyDown += OnInputKeyDown;
            _input.LostKeyboardFocus += OnInputLostFocus;
        }
        if (_popup is not null)
        {
            _popup.Placement = PlacementMode.Custom;
            _popup.PlacementTarget = _field;
            _popup.CustomPopupPlacementCallback = PopupPositions.Callback(() => Slate.PopoverPlacement.BottomStart, () => 4);
            _popup.Closed += OnPopupClosed;
            _popup.PreviewKeyDown += OnPopupKeyDown;
        }
        UpdateText();
    }

    private void OnPopupClosed(object? sender, EventArgs e) => SetCurrentValue(IsDropDownOpenProperty, false);

    private void OnPopupKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Escape)
        {
            SetCurrentValue(IsDropDownOpenProperty, false);
            _input?.Focus();
            e.Handled = true;
        }
    }

    private void OnInputKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case System.Windows.Input.Key.Enter:
                Commit();
                e.Handled = true;
                break;
            case System.Windows.Input.Key.Down when Keyboard.Modifiers.HasFlag(ModifierKeys.Alt) || e.SystemKey == System.Windows.Input.Key.Down:
            case System.Windows.Input.Key.System when e.SystemKey == System.Windows.Input.Key.Down:
            case System.Windows.Input.Key.F4:
                SetCurrentValue(IsDropDownOpenProperty, true);
                e.Handled = true;
                break;
            case System.Windows.Input.Key.Escape when IsDropDownOpen:
                SetCurrentValue(IsDropDownOpenProperty, false);
                e.Handled = true;
                break;
        }
    }

    private void OnInputLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (_popup?.Child is UIElement child && child.IsKeyboardFocusWithin)
            return;
        Commit();
    }

    private void OnButtonClick(object sender, RoutedEventArgs e)
    {
        switch (e.OriginalSource)
        {
            case FrameworkElement { Name: "PART_Toggle" }:
                SetCurrentValue(IsDropDownOpenProperty, !IsDropDownOpen);
                e.Handled = true;
                break;
            case FrameworkElement { Name: "PART_Clear" }:
                if (Selection == DateSelection.Range) SetCurrentValue(RangeProperty, null);
                else SetCurrentValue(ValueProperty, null);
                _input?.Focus();
                e.Handled = true;
                break;
        }
    }

    private void OnDatePicked(object? sender, DatePickedEventArgs e)
    {
        if (Selection == DateSelection.Single)
        {
            SetCurrentValue(ValueProperty, e.Date);
            if (!Inline)
            {
                SetCurrentValue(IsDropDownOpenProperty, false);
                _input?.Focus();
            }
        }
    }

    private void OnRangePicked(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is CalendarView cal && Selection == DateSelection.Range)
        {
            SetCurrentValue(RangeProperty, cal.Range);
            if (!Inline)
            {
                SetCurrentValue(IsDropDownOpenProperty, false);
                _input?.Focus();
            }
        }
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new DatePickerAutomationPeer(this);
}

/// <summary>Date picker as a combo box with expand/collapse and value patterns.</summary>
public sealed class DatePickerAutomationPeer(DatePicker owner) : FrameworkElementAutomationPeer(owner), IExpandCollapseProvider, IValueProvider
{
    private DatePicker Picker => (DatePicker)Owner;

    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.ComboBox;
    protected override string GetClassNameCore() => "DatePicker";

    public override object? GetPattern(PatternInterface patternInterface) =>
        patternInterface is PatternInterface.ExpandCollapse or PatternInterface.Value ? this : base.GetPattern(patternInterface);

    public ExpandCollapseState ExpandCollapseState => Picker.IsDropDownOpen ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed;
    public void Expand() => Picker.IsDropDownOpen = true;
    public void Collapse() => Picker.IsDropDownOpen = false;

    public bool IsReadOnly => !Picker.IsEnabled;
    public string Value => Picker.FormatValue();

    public void SetValue(string value)
    {
        Picker.InputText = value;
        if (!Picker.Commit())
            throw new ArgumentException($"'{value}' is not a valid date ({Picker.Pattern}).", nameof(value));
    }

    internal void RaiseExpandCollapse(bool oldValue, bool newValue) =>
        RaisePropertyChangedEvent(ExpandCollapsePatternIdentifiers.ExpandCollapseStateProperty,
            oldValue ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed,
            newValue ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed);
}
