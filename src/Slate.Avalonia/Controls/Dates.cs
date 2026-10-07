using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Slate.Dates;

namespace Slate.Avalonia.Controls;

/// <summary>A day cell in a <see cref="CalendarView"/>.</summary>
[PseudoClasses(":today", ":outside", ":selected", ":range-start", ":range-end", ":in-range", ":preview")]
public class DayButton : Button
{
    protected override Type StyleKeyOverride => typeof(DayButton);

    public DateOnly Date { get; internal set; }

    internal void Apply(CalendarDay day, CultureInfo culture)
    {
        Date = day.Date;
        Content = day.Date.Day.ToString(culture);
        IsEnabled = !day.IsDisabled;
        PseudoClasses.Set(":today", day.IsToday);
        PseudoClasses.Set(":outside", !day.InMonth);
        PseudoClasses.Set(":selected", day.IsSelected || day.IsRangeStart || day.IsRangeEnd);
        PseudoClasses.Set(":range-start", day.IsRangeStart);
        PseudoClasses.Set(":range-end", day.IsRangeEnd);
        PseudoClasses.Set(":in-range", day.InRange);
        PseudoClasses.Set(":preview", day.InPreview);
        AutomationProperties.SetName(this, day.Date.ToDateTime(TimeOnly.MinValue).ToString("D", culture));
        AutomationProperties.SetItemStatus(this, day.IsSelected || day.IsRangeStart || day.IsRangeEnd ? "selected" : day.IsToday ? "today" : null);
    }
}

/// <summary>
/// The calendar grid behind <see cref="DatePicker"/> (also usable on its own): 6×7 month grid, ISO weeks, keyboard
/// navigation (arrows, Home/End, PageUp/Down months, Shift+PageUp/Down years) that skips disabled days, single or
/// range selection with hover preview, and optional presets. All logic comes from Slate.Core's CalendarModel.
/// </summary>
[TemplatePart("PART_Grid", typeof(Grid))]
[TemplatePart("PART_Title", typeof(TextBlock))]
[TemplatePart("PART_Previous", typeof(Button))]
[TemplatePart("PART_Next", typeof(Button))]
[TemplatePart("PART_Presets", typeof(StackPanel))]
[PseudoClasses(":has-presets")]
public class CalendarView : TemplatedControl
{
    public static readonly StyledProperty<DateOnly?> ValueProperty =
        AvaloniaProperty.Register<CalendarView, DateOnly?>(nameof(Value), defaultBindingMode: global::Avalonia.Data.BindingMode.TwoWay);
    public static readonly StyledProperty<DateRange?> RangeProperty =
        AvaloniaProperty.Register<CalendarView, DateRange?>(nameof(Range), defaultBindingMode: global::Avalonia.Data.BindingMode.TwoWay);
    public static readonly StyledProperty<DateSelection> SelectionProperty = AvaloniaProperty.Register<CalendarView, DateSelection>(nameof(Selection));
    public static readonly StyledProperty<DateOnly?> MinProperty = AvaloniaProperty.Register<CalendarView, DateOnly?>(nameof(Min));
    public static readonly StyledProperty<DateOnly?> MaxProperty = AvaloniaProperty.Register<CalendarView, DateOnly?>(nameof(Max));
    public static readonly StyledProperty<Func<DateOnly, bool>?> DisabledDatesProperty = AvaloniaProperty.Register<CalendarView, Func<DateOnly, bool>?>(nameof(DisabledDates));
    public static readonly StyledProperty<DayOfWeek?> FirstDayOfWeekProperty = AvaloniaProperty.Register<CalendarView, DayOfWeek?>(nameof(FirstDayOfWeek));
    public static readonly StyledProperty<IEnumerable<DatePreset>?> PresetsProperty = AvaloniaProperty.Register<CalendarView, IEnumerable<DatePreset>?>(nameof(Presets));

    /// <summary>"Today" for highlighting and presets; defaults to the system date (settable for tests and demos).</summary>
    public static readonly StyledProperty<DateOnly?> TodayProperty = AvaloniaProperty.Register<CalendarView, DateOnly?>(nameof(Today));

    public static readonly StyledProperty<DateOnly> FocusedDateProperty = AvaloniaProperty.Register<CalendarView, DateOnly>(nameof(FocusedDate));

    public static readonly RoutedEvent<RoutedEventArgs> DatePickedEvent = RoutedEvent.Register<CalendarView, RoutedEventArgs>(nameof(DatePicked), RoutingStrategies.Bubble);

    private readonly DayButton[] _days = new DayButton[CalendarModel.Rows * 7];
    private readonly TextBlock[] _weekdays = new TextBlock[7];
    private Grid? _grid;
    private TextBlock? _title;
    private StackPanel? _presets;
    private DateOnly? _hover;
    private int _year;
    private int _month;

    static CalendarView()
    {
        FocusableProperty.OverrideDefaultValue<CalendarView>(false);
        foreach (var p in new AvaloniaProperty[] { ValueProperty, RangeProperty, SelectionProperty, MinProperty, MaxProperty, DisabledDatesProperty, FirstDayOfWeekProperty, TodayProperty })
            p.Changed.AddClassHandler<CalendarView>((c, _) => c.Render());
        PresetsProperty.Changed.AddClassHandler<CalendarView>((c, _) => c.BuildPresets());
        ValueProperty.Changed.AddClassHandler<CalendarView>((c, e) => { if (e.GetNewValue<DateOnly?>() is { } d) c.ShowMonthOf(d); });
    }

    public CalendarView()
    {
        var today = EffectiveToday;
        _year = today.Year;
        _month = today.Month;
        FocusedDate = today;
        for (var i = 0; i < _days.Length; i++)
        {
            var b = new DayButton();
            b.Click += (s, _) => Pick(((DayButton)s!).Date);
            b.PointerEntered += (s, _) => { _hover = ((DayButton)s!).Date; if (Selection == DateSelection.Range && Range is { End: null }) Render(); };
            _days[i] = b;
        }
        for (var i = 0; i < 7; i++)
            _weekdays[i] = new TextBlock { Classes = { "weekday" }, HorizontalAlignment = HorizontalAlignment.Center };
    }

    public DateOnly? Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public DateRange? Range { get => GetValue(RangeProperty); set => SetValue(RangeProperty, value); }
    public DateSelection Selection { get => GetValue(SelectionProperty); set => SetValue(SelectionProperty, value); }
    public DateOnly? Min { get => GetValue(MinProperty); set => SetValue(MinProperty, value); }
    public DateOnly? Max { get => GetValue(MaxProperty); set => SetValue(MaxProperty, value); }
    public Func<DateOnly, bool>? DisabledDates { get => GetValue(DisabledDatesProperty); set => SetValue(DisabledDatesProperty, value); }
    public DayOfWeek? FirstDayOfWeek { get => GetValue(FirstDayOfWeekProperty); set => SetValue(FirstDayOfWeekProperty, value); }
    public IEnumerable<DatePreset>? Presets { get => GetValue(PresetsProperty); set => SetValue(PresetsProperty, value); }
    public DateOnly? Today { get => GetValue(TodayProperty); set => SetValue(TodayProperty, value); }
    public DateOnly FocusedDate { get => GetValue(FocusedDateProperty); set => SetValue(FocusedDateProperty, value); }

    public event EventHandler<RoutedEventArgs>? DatePicked { add => AddHandler(DatePickedEvent, value); remove => RemoveHandler(DatePickedEvent, value); }

    public int DisplayYear => _year;
    public int DisplayMonth => _month;
    public IReadOnlyList<DayButton> Days => _days;

    private DateOnly EffectiveToday => Today ?? DateOnly.FromDateTime(DateTime.Today);
    private static CultureInfo Culture => CultureInfo.CurrentCulture;

    public CalendarOptions Options => new()
    {
        FirstDayOfWeek = FirstDayOfWeek ?? CalendarModel.FirstDayOfWeek(Culture),
        Today = EffectiveToday,
        Min = Min,
        Max = Max,
        IsDateDisabled = DisabledDates,
        Selected = Selection == DateSelection.Single ? Value : null,
        Range = Selection == DateSelection.Range ? Range : null,
        Hover = _hover,
    };

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _grid?.Children.Clear();
        _grid = e.NameScope.Find<Grid>("PART_Grid");
        _title = e.NameScope.Find<TextBlock>("PART_Title");
        _presets = e.NameScope.Find<StackPanel>("PART_Presets");
        if (e.NameScope.Find<Button>("PART_Previous") is { } prev) prev.Click += (_, _) => ShowMonth(_month == 1 ? _year - 1 : _year, _month == 1 ? 12 : _month - 1);
        if (e.NameScope.Find<Button>("PART_Next") is { } next) next.Click += (_, _) => ShowMonth(_month == 12 ? _year + 1 : _year, _month == 12 ? 1 : _month + 1);

        if (_grid is not null)
        {
            _grid.ColumnDefinitions = new ColumnDefinitions(string.Join(",", Enumerable.Repeat("*", 7)));
            _grid.RowDefinitions = new RowDefinitions(string.Join(",", Enumerable.Repeat("Auto", CalendarModel.Rows + 1)));
            for (var i = 0; i < 7; i++)
            {
                Grid.SetColumn(_weekdays[i], i);
                _grid.Children.Add(_weekdays[i]);
            }
            for (var i = 0; i < _days.Length; i++)
            {
                Grid.SetRow(_days[i], i / 7 + 1);
                Grid.SetColumn(_days[i], i % 7);
                _grid.Children.Add(_days[i]);
            }
        }
        BuildPresets();
        Render();
    }

    public void ShowMonthOf(DateOnly d) => ShowMonth(d.Year, d.Month);

    public void ShowMonth(int year, int month)
    {
        _year = year;
        _month = month;
        Render();
    }

    /// <summary>Focuses the focused (or selected) day button — used when a popup opens.</summary>
    public void FocusDay()
    {
        var start = Selection == DateSelection.Single ? Value : Range?.Start;
        if (start is { } s) FocusedDate = s;
        ShowMonthOf(FocusedDate);
        _days.FirstOrDefault(b => b.Date == FocusedDate)?.Focus(NavigationMethod.Tab);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled) return;
        CalendarKey? key = e.Key switch
        {
            Key.Left => CalendarKey.Left,
            Key.Right => CalendarKey.Right,
            Key.Up => CalendarKey.Up,
            Key.Down => CalendarKey.Down,
            Key.Home => CalendarKey.Home,
            Key.End => CalendarKey.End,
            Key.PageUp => CalendarKey.PageUp,
            Key.PageDown => CalendarKey.PageDown,
            _ => null,
        };
        if (key is not { } k) return;

        var current = (e.Source as DayButton)?.Date ?? FocusedDate;
        var next = CalendarModel.Navigate(current, k, e.KeyModifiers.HasFlag(KeyModifiers.Shift), Options);
        MoveFocus(next);
        e.Handled = true;
    }

    /// <summary>Moves keyboard focus to a day, paging months as needed.</summary>
    public void MoveFocus(DateOnly date)
    {
        FocusedDate = date;
        if (date.Year != _year || date.Month != _month) ShowMonthOf(date);
        else if (Selection == DateSelection.Range && Range is { End: null })
        {
            _hover = date;
            Render();
        }
        _days.FirstOrDefault(b => b.Date == date)?.Focus(NavigationMethod.Directional);
    }

    /// <summary>Selects a day (single) or advances the range (start, then end).</summary>
    public void Pick(DateOnly date)
    {
        if (Options.IsDisabled(date)) return;
        FocusedDate = date;
        if (Selection == DateSelection.Single)
        {
            Value = date;
        }
        else
        {
            Range = CalendarModel.PickRange(Range, date);
            _hover = null;
        }
        RaiseEvent(new RoutedEventArgs(DatePickedEvent));
    }

    private void Render()
    {
        var month = CalendarModel.Build(_year, _month, Options);
        if (_title is not null)
            _title.Text = new DateTime(_year, _month, 1).ToString("MMMM yyyy", Culture);

        for (var i = 0; i < 7; i++)
            _weekdays[i].Text = Culture.DateTimeFormat.GetShortestDayName(month.Weekdays[i]);

        var cells = month.Weeks.SelectMany(w => w.Days).ToList();
        for (var i = 0; i < _days.Length && i < cells.Count; i++)
        {
            _days[i].Apply(cells[i], Culture);
            // Roving tab stop: only the focused day (or the first enabled one in the month) is tabbable.
            KeyboardNavigation.SetIsTabStop(_days[i], cells[i].Date == FocusedDate);
        }
        if (!_days.Any(d => d.Date == FocusedDate))
            KeyboardNavigation.SetIsTabStop(_days.First(d => d.IsEnabled && !d.Classes.Contains(":outside")), true);
    }

    private void BuildPresets()
    {
        if (_presets is null) return;
        _presets.Children.Clear();
        var presets = Presets?.ToList() ?? [];
        PseudoClasses.Set(":has-presets", presets.Count > 0);
        foreach (var preset in presets)
        {
            var b = new Button { Content = preset.Label, Classes = { "ghost" }, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left };
            Slate.Avalonia.Sl.SetSize(b, ControlSize.Small);
            b.Click += (_, _) => ApplyPreset(preset);
            _presets.Children.Add(b);
        }
    }

    public void ApplyPreset(DatePreset preset)
    {
        var range = CalendarModel.Resolve(preset.Kind, EffectiveToday);
        if (Selection == DateSelection.Range) Range = range;
        else Value = range.Start;
        ShowMonthOf(range.Start);
        RaiseEvent(new RoutedEventArgs(DatePickedEvent));
    }
}

/// <summary>
/// Slate date picker (<c>sl:DatePicker</c>; distinct from <see cref="global::Avalonia.Controls.DatePicker"/>): a text
/// field that accepts typed dates (<see cref="Format"/> or the culture's short pattern; ranges as "start – end")
/// plus a calendar popup, or an <see cref="Inline"/> calendar. Single or range <see cref="Selection"/>, min/max,
/// disabled dates and presets.
/// </summary>
[TemplatePart("PART_Field", typeof(TextField))]
[TemplatePart("PART_Toggle", typeof(Button))]
[TemplatePart("PART_Popup", typeof(Popup))]
[TemplatePart("PART_Calendar", typeof(CalendarView))]
[PseudoClasses(":inline", ":open")]
public class DatePicker : TemplatedControl
{
    public static readonly StyledProperty<DateOnly?> ValueProperty =
        AvaloniaProperty.Register<DatePicker, DateOnly?>(nameof(Value), defaultBindingMode: global::Avalonia.Data.BindingMode.TwoWay);
    public static readonly StyledProperty<DateRange?> RangeProperty =
        AvaloniaProperty.Register<DatePicker, DateRange?>(nameof(Range), defaultBindingMode: global::Avalonia.Data.BindingMode.TwoWay);
    public static readonly StyledProperty<DateSelection> SelectionProperty = AvaloniaProperty.Register<DatePicker, DateSelection>(nameof(Selection));
    public static readonly StyledProperty<DateOnly?> MinProperty = AvaloniaProperty.Register<DatePicker, DateOnly?>(nameof(Min));
    public static readonly StyledProperty<DateOnly?> MaxProperty = AvaloniaProperty.Register<DatePicker, DateOnly?>(nameof(Max));
    public static readonly StyledProperty<Func<DateOnly, bool>?> DisabledDatesProperty = AvaloniaProperty.Register<DatePicker, Func<DateOnly, bool>?>(nameof(DisabledDates));
    public static readonly StyledProperty<DayOfWeek?> FirstDayOfWeekProperty = AvaloniaProperty.Register<DatePicker, DayOfWeek?>(nameof(FirstDayOfWeek));
    public static readonly StyledProperty<string?> FormatProperty = AvaloniaProperty.Register<DatePicker, string?>(nameof(Format));
    public static readonly StyledProperty<IEnumerable<DatePreset>?> PresetsProperty = AvaloniaProperty.Register<DatePicker, IEnumerable<DatePreset>?>(nameof(Presets));
    public static readonly StyledProperty<string?> LabelProperty = AvaloniaProperty.Register<DatePicker, string?>(nameof(Label));
    public static readonly StyledProperty<string?> PlaceholderProperty = AvaloniaProperty.Register<DatePicker, string?>(nameof(Placeholder));
    public static readonly StyledProperty<string?> HelperTextProperty = AvaloniaProperty.Register<DatePicker, string?>(nameof(HelperText));
    public static readonly StyledProperty<string?> ErrorProperty = AvaloniaProperty.Register<DatePicker, string?>(nameof(Error));
    public static readonly StyledProperty<FieldVariant> VariantProperty = AvaloniaProperty.Register<DatePicker, FieldVariant>(nameof(Variant));
    public static readonly StyledProperty<ControlSize> SizeProperty = AvaloniaProperty.Register<DatePicker, ControlSize>(nameof(Size), ControlSize.Medium);
    public static readonly StyledProperty<bool> ClearableProperty = AvaloniaProperty.Register<DatePicker, bool>(nameof(Clearable));
    public static readonly StyledProperty<bool> InlineProperty = AvaloniaProperty.Register<DatePicker, bool>(nameof(Inline));
    public static readonly StyledProperty<DateOnly?> TodayProperty = AvaloniaProperty.Register<DatePicker, DateOnly?>(nameof(Today));

    public static readonly StyledProperty<bool> IsDropDownOpenProperty =
        AvaloniaProperty.Register<DatePicker, bool>(nameof(IsDropDownOpen), defaultBindingMode: global::Avalonia.Data.BindingMode.TwoWay);

    /// <summary>The text shown in the field (formatted value, or what the user is typing).</summary>
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<DatePicker, string?>(nameof(Text), defaultBindingMode: global::Avalonia.Data.BindingMode.TwoWay);

    /// <summary>Error shown when typed text can't be parsed (cleared once it parses); <see cref="Error"/> wins.</summary>
    public static readonly DirectProperty<DatePicker, string?> ParseErrorProperty =
        AvaloniaProperty.RegisterDirect<DatePicker, string?>(nameof(ParseError), f => f.ParseError);

    /// <summary>The error shown under the field: <see cref="Error"/>, else <see cref="ParseError"/>.</summary>
    public static readonly DirectProperty<DatePicker, string?> DisplayErrorProperty =
        AvaloniaProperty.RegisterDirect<DatePicker, string?>(nameof(DisplayError), f => f.DisplayError);

    public static readonly RoutedEvent<RoutedEventArgs> ValueChangedEvent = RoutedEvent.Register<DatePicker, RoutedEventArgs>(nameof(ValueChanged), RoutingStrategies.Bubble);
    public static readonly RoutedEvent<RoutedEventArgs> RangeChangedEvent = RoutedEvent.Register<DatePicker, RoutedEventArgs>(nameof(RangeChanged), RoutingStrategies.Bubble);

    private TextField? _field;
    private Popup? _popup;
    private CalendarView? _calendar;
    private string? _parseError;
    private string? _displayError;
    private bool _updatingText;

    static DatePicker()
    {
        ValueProperty.Changed.AddClassHandler<DatePicker>((f, _) => { f.SyncText(); f.RaiseEvent(new RoutedEventArgs(ValueChangedEvent)); });
        RangeProperty.Changed.AddClassHandler<DatePicker>((f, _) => { f.SyncText(); f.RaiseEvent(new RoutedEventArgs(RangeChangedEvent)); });
        FormatProperty.Changed.AddClassHandler<DatePicker>((f, _) => f.SyncText());
        SelectionProperty.Changed.AddClassHandler<DatePicker>((f, _) => f.SyncText());
        InlineProperty.Changed.AddClassHandler<DatePicker>((f, e) => f.PseudoClasses.Set(":inline", e.GetNewValue<bool>()));
        IsDropDownOpenProperty.Changed.AddClassHandler<DatePicker>((f, e) => f.OnOpenChanged(e.GetNewValue<bool>()));
        ErrorProperty.Changed.AddClassHandler<DatePicker>((f, _) => f.DisplayError = f.Error ?? f.ParseError);
        ParseErrorProperty.Changed.AddClassHandler<DatePicker>((f, _) => f.DisplayError = f.Error ?? f.ParseError);
    }

    public DateOnly? Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public DateRange? Range { get => GetValue(RangeProperty); set => SetValue(RangeProperty, value); }
    public DateSelection Selection { get => GetValue(SelectionProperty); set => SetValue(SelectionProperty, value); }
    public DateOnly? Min { get => GetValue(MinProperty); set => SetValue(MinProperty, value); }
    public DateOnly? Max { get => GetValue(MaxProperty); set => SetValue(MaxProperty, value); }
    public Func<DateOnly, bool>? DisabledDates { get => GetValue(DisabledDatesProperty); set => SetValue(DisabledDatesProperty, value); }
    public DayOfWeek? FirstDayOfWeek { get => GetValue(FirstDayOfWeekProperty); set => SetValue(FirstDayOfWeekProperty, value); }
    public string? Format { get => GetValue(FormatProperty); set => SetValue(FormatProperty, value); }
    public IEnumerable<DatePreset>? Presets { get => GetValue(PresetsProperty); set => SetValue(PresetsProperty, value); }
    public string? Label { get => GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public string? Placeholder { get => GetValue(PlaceholderProperty); set => SetValue(PlaceholderProperty, value); }
    public string? HelperText { get => GetValue(HelperTextProperty); set => SetValue(HelperTextProperty, value); }
    public string? Error { get => GetValue(ErrorProperty); set => SetValue(ErrorProperty, value); }
    public FieldVariant Variant { get => GetValue(VariantProperty); set => SetValue(VariantProperty, value); }
    public ControlSize Size { get => GetValue(SizeProperty); set => SetValue(SizeProperty, value); }
    public bool Clearable { get => GetValue(ClearableProperty); set => SetValue(ClearableProperty, value); }
    public bool Inline { get => GetValue(InlineProperty); set => SetValue(InlineProperty, value); }
    public DateOnly? Today { get => GetValue(TodayProperty); set => SetValue(TodayProperty, value); }
    public bool IsDropDownOpen { get => GetValue(IsDropDownOpenProperty); set => SetValue(IsDropDownOpenProperty, value); }
    public string? Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public string? ParseError { get => _parseError; private set => SetAndRaise(ParseErrorProperty, ref _parseError, value); }
    public string? DisplayError { get => _displayError; private set => SetAndRaise(DisplayErrorProperty, ref _displayError, value); }

    public event EventHandler<RoutedEventArgs>? ValueChanged { add => AddHandler(ValueChangedEvent, value); remove => RemoveHandler(ValueChangedEvent, value); }
    public event EventHandler<RoutedEventArgs>? RangeChanged { add => AddHandler(RangeChangedEvent, value); remove => RemoveHandler(RangeChangedEvent, value); }

    public CalendarView? Calendar => _calendar;

    /// <summary>The date pattern in use: <see cref="Format"/> or the current culture's short date.</summary>
    public string EffectiveFormat => Format ?? DateText.ShortPattern(CultureInfo.CurrentCulture);

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        if (_calendar is not null) _calendar.DatePicked -= OnDatePicked;
        _field = e.NameScope.Find<TextField>("PART_Field");
        _popup = e.NameScope.Find<Popup>("PART_Popup");
        _calendar = e.NameScope.Find<CalendarView>("PART_Calendar");
        if (e.NameScope.Find<Button>("PART_Toggle") is { } toggle)
            toggle.Click += (_, ev) => { IsDropDownOpen = !IsDropDownOpen; ev.Handled = true; };

        if (_calendar is not null) _calendar.DatePicked += OnDatePicked;
        if (_popup is not null && _field is not null)
        {
            SlatePlacement.Apply(_popup, _field, () => PopoverPlacement.BottomStart, () => 4);
            _popup.Closed += (_, _) => { if (IsDropDownOpen) IsDropDownOpen = false; };
        }
        if (_field is not null)
        {
            _field.LostFocus += (_, _) => Commit();
            _field.AddHandler(KeyDownEvent, OnFieldKeyDown, RoutingStrategies.Tunnel);
        }
        SyncText();
    }

    private void OnFieldKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                Commit();
                break;
            case Key.Down when e.KeyModifiers.HasFlag(KeyModifiers.Alt):
            case Key.F4:
                IsDropDownOpen = true;
                e.Handled = true;
                break;
        }
    }

    private void OnOpenChanged(bool open)
    {
        PseudoClasses.Set(":open", open);
        if (_popup is null) return;
        _popup.IsOpen = open;
        if (open) global::Avalonia.Threading.Dispatcher.UIThread.Post(() => _calendar?.FocusDay());
        else _field?.Focus();
    }

    private void OnDatePicked(object? sender, RoutedEventArgs e)
    {
        if (_calendar is null) return;
        if (Selection == DateSelection.Single)
        {
            Value = _calendar.Value;
            IsDropDownOpen = false;
        }
        else
        {
            Range = _calendar.Range;
            if (Range is { End: not null }) IsDropDownOpen = false;
        }
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!e.Handled && e.Key == Key.Escape && IsDropDownOpen)
        {
            IsDropDownOpen = false;
            e.Handled = true;
        }
    }

    /// <summary>Parses the typed text into <see cref="Value"/>/<see cref="Range"/> (empty text clears).</summary>
    public void Commit()
    {
        if (_updatingText) return;
        var text = Text?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            ParseError = null;
            if (Selection == DateSelection.Single) Value = null;
            else Range = null;
            return;
        }

        if (Selection == DateSelection.Single)
        {
            if (DateText.Parse(text, EffectiveFormat) is { } d && InBounds(d))
            {
                ParseError = null;
                Value = d;
            }
            else
            {
                ParseError = $"Enter a date as {EffectiveFormat}.";
            }
            return;
        }

        var parts = text.Split(['–', '—'], 2, StringSplitOptions.TrimEntries);
        if (parts.Length == 1) parts = text.Split(" - ", 2, StringSplitOptions.TrimEntries);
        var start = DateText.Parse(parts[0], EffectiveFormat);
        var end = parts.Length > 1 ? DateText.Parse(parts[1], EffectiveFormat) : null;
        if (start is { } s && InBounds(s) && (parts.Length == 1 || end is { } en && en >= s && InBounds(en)))
        {
            ParseError = null;
            Range = new DateRange(s, end);
        }
        else
        {
            ParseError = $"Enter a range as {EffectiveFormat} – {EffectiveFormat}.";
        }
    }

    private bool InBounds(DateOnly d) =>
        !((Min is { } min && d < min) || (Max is { } max && d > max) || (DisabledDates?.Invoke(d) ?? false));

    private void SyncText()
    {
        _updatingText = true;
        try
        {
            var f = EffectiveFormat;
            Text = Selection == DateSelection.Single
                ? Value is { } v ? DateText.Format(v, f) : null
                : Range is { } r ? r.End is { } end ? $"{DateText.Format(r.Start, f)} – {DateText.Format(end, f)}" : $"{DateText.Format(r.Start, f)} –" : null;
            ParseError = null;
        }
        finally { _updatingText = false; }
    }
}
