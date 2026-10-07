using System.Collections;
using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Slate.Collections;

namespace Slate.Wpf;

/// <summary>A row in a <see cref="Select"/> dropdown: an option, a group header, or "Create …".</summary>
public sealed class SelectOption(object? item, string text, bool isHeader = false, bool isCreate = false) : INotifyPropertyChanged
{
    private bool _isSelected;

    public object? Item { get; } = item;
    public string Text { get; } = text;
    public bool IsHeader { get; } = isHeader;
    public bool IsCreate { get; } = isCreate;

    /// <summary>Part of the current value (shown with a check mark).</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public override string ToString() => Text;
}

/// <summary>A selected value shown as a removable chip in a multiple <see cref="Select"/>.</summary>
public sealed record SelectChip(object Item, string Text);

/// <summary>
/// Select / combobox (design/api/components.json "Select"): single or multiple, optionally searchable (type to filter,
/// diacritic-insensitive, Slate.Core <see cref="OptionFilter"/>), creatable, grouped, with chips, clear button and
/// keyboard support (arrows/Home/End/PageUp/PageDown via <see cref="ListNavigator"/>, typeahead, Enter, Escape,
/// Backspace removes the last chip). Shares the TextField chrome (variants, sizes, radius, label, helper, error).
/// </summary>
[TemplatePart(Name = PartPopup, Type = typeof(Popup))]
[TemplatePart(Name = PartList, Type = typeof(ListBox))]
[TemplatePart(Name = PartSearch, Type = typeof(TextBox))]
[TemplatePart(Name = PartField, Type = typeof(FrameworkElement))]
public class Select : Control, IFieldChrome
{
    public const string PartPopup = "PART_Popup";
    public const string PartList = "PART_List";
    public const string PartSearch = "PART_Search";
    public const string PartField = "PART_Field";

    private static readonly PropertyChangedCallback Rebuild = (d, _) => ((Select)d).RebuildAll();

    public static readonly DependencyProperty ItemsProperty = DependencyProperty.Register(nameof(Items), typeof(IEnumerable), typeof(Select), new FrameworkPropertyMetadata(null, Rebuild));

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(object), typeof(Select), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnValueChanged));

    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
        nameof(Values), typeof(IList), typeof(Select), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnValuesChanged));

    public static readonly DependencyProperty MultipleProperty = DependencyProperty.Register(nameof(Multiple), typeof(bool), typeof(Select), new FrameworkPropertyMetadata(false, Rebuild));
    public static readonly DependencyProperty SearchableProperty = DependencyProperty.Register(nameof(Searchable), typeof(bool), typeof(Select), new FrameworkPropertyMetadata(false, Rebuild));
    public static readonly DependencyProperty CreatableProperty = DependencyProperty.Register(nameof(Creatable), typeof(bool), typeof(Select), new FrameworkPropertyMetadata(false, Rebuild));
    public static readonly DependencyProperty ItemTextProperty = DependencyProperty.Register(nameof(ItemText), typeof(Func<object, string>), typeof(Select), new FrameworkPropertyMetadata(null, Rebuild));
    public static readonly DependencyProperty ItemTemplateProperty = DependencyProperty.Register(nameof(ItemTemplate), typeof(DataTemplate), typeof(Select), new FrameworkPropertyMetadata(null));
    public static readonly DependencyProperty GroupByProperty = DependencyProperty.Register(nameof(GroupBy), typeof(Func<object, string>), typeof(Select), new FrameworkPropertyMetadata(null, Rebuild));

    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label), typeof(string), typeof(Select), new FrameworkPropertyMetadata(null, (d, e) => AutomationProperties.SetName(d, (string?)e.NewValue ?? "")));

    public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.Register(nameof(Placeholder), typeof(string), typeof(Select), new FrameworkPropertyMetadata(null));
    public static readonly DependencyProperty HelperTextProperty = DependencyProperty.Register(nameof(HelperText), typeof(string), typeof(Select), new FrameworkPropertyMetadata(null, OnHelpChanged));
    public static readonly DependencyProperty ErrorProperty = DependencyProperty.Register(nameof(Error), typeof(string), typeof(Select), new FrameworkPropertyMetadata(null, OnHelpChanged));

    public static readonly DependencyProperty VariantProperty = DependencyProperty.Register(
        nameof(Variant), typeof(FieldVariant), typeof(Select), new FrameworkPropertyMetadata(FieldVariant.Outlined, (d, _) => Styling.Refresh(d)));

    private static readonly DependencyPropertyKey ActualVariantPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(ActualVariant), typeof(FieldVariant), typeof(Select), new FrameworkPropertyMetadata(FieldVariant.Outlined));

    public static readonly DependencyProperty ActualVariantProperty = ActualVariantPropertyKey.DependencyProperty;

    public static readonly DependencyProperty SizeProperty = Sl.SizeProperty.AddOwner(typeof(Select));
    public static readonly DependencyProperty RadiusProperty = Sl.RadiusProperty.AddOwner(typeof(Select));

    public static readonly DependencyProperty ClearableProperty = DependencyProperty.Register(nameof(Clearable), typeof(bool), typeof(Select), new FrameworkPropertyMetadata(false, (d, _) => ((Select)d).SyncState()));
    public static readonly DependencyProperty RequiredProperty = DependencyProperty.Register(nameof(Required), typeof(bool), typeof(Select), new FrameworkPropertyMetadata(false, (d, e) => AutomationProperties.SetIsRequiredForForm(d, (bool)e.NewValue)));
    public static readonly DependencyProperty LoadingProperty = DependencyProperty.Register(nameof(Loading), typeof(bool), typeof(Select), new FrameworkPropertyMetadata(false));
    public static readonly DependencyProperty MaxVisibleChipsProperty = DependencyProperty.Register(nameof(MaxVisibleChips), typeof(int?), typeof(Select), new FrameworkPropertyMetadata(null, (d, _) => ((Select)d).SyncState()));
    public static readonly DependencyProperty EmptyContentProperty = DependencyProperty.Register(nameof(EmptyContent), typeof(object), typeof(Select), new FrameworkPropertyMetadata("No results"));

    public static readonly DependencyProperty IsDropDownOpenProperty = DependencyProperty.Register(
        nameof(IsDropDownOpen), typeof(bool), typeof(Select), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnOpenChanged));

    public static readonly DependencyProperty SearchTextProperty = DependencyProperty.Register(
        nameof(SearchText), typeof(string), typeof(Select), new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSearchChanged));

    private static readonly DependencyPropertyKey OptionsPropertyKey = DependencyProperty.RegisterReadOnly(nameof(Options), typeof(IReadOnlyList<SelectOption>), typeof(Select), new FrameworkPropertyMetadata(Array.Empty<SelectOption>()));
    public static readonly DependencyProperty OptionsProperty = OptionsPropertyKey.DependencyProperty;

    private static readonly DependencyPropertyKey DisplayTextPropertyKey = DependencyProperty.RegisterReadOnly(nameof(DisplayText), typeof(string), typeof(Select), new FrameworkPropertyMetadata(null));
    public static readonly DependencyProperty DisplayTextProperty = DisplayTextPropertyKey.DependencyProperty;

    private static readonly DependencyPropertyKey ChipsPropertyKey = DependencyProperty.RegisterReadOnly(nameof(Chips), typeof(IReadOnlyList<SelectChip>), typeof(Select), new FrameworkPropertyMetadata(Array.Empty<SelectChip>()));
    public static readonly DependencyProperty ChipsProperty = ChipsPropertyKey.DependencyProperty;

    private static readonly DependencyPropertyKey OverflowTextPropertyKey = DependencyProperty.RegisterReadOnly(nameof(OverflowText), typeof(string), typeof(Select), new FrameworkPropertyMetadata(null));
    public static readonly DependencyProperty OverflowTextProperty = OverflowTextPropertyKey.DependencyProperty;

    private static readonly DependencyPropertyKey HasValuePropertyKey = DependencyProperty.RegisterReadOnly(nameof(HasValue), typeof(bool), typeof(Select), new FrameworkPropertyMetadata(false));
    public static readonly DependencyProperty HasValueProperty = HasValuePropertyKey.DependencyProperty;

    private static readonly DependencyPropertyKey ShowClearPropertyKey = DependencyProperty.RegisterReadOnly(nameof(ShowClear), typeof(bool), typeof(Select), new FrameworkPropertyMetadata(false));
    public static readonly DependencyProperty ShowClearProperty = ShowClearPropertyKey.DependencyProperty;

    private static readonly DependencyPropertyKey HasErrorPropertyKey = DependencyProperty.RegisterReadOnly(nameof(HasError), typeof(bool), typeof(Select), new FrameworkPropertyMetadata(false));
    public static readonly DependencyProperty HasErrorProperty = HasErrorPropertyKey.DependencyProperty;

    public static readonly RoutedEvent ValueChangedEvent = EventManager.RegisterRoutedEvent(nameof(ValueChanged), RoutingStrategy.Bubble, typeof(RoutedPropertyChangedEventHandler<object?>), typeof(Select));
    public static readonly RoutedEvent ValuesChangedEvent = EventManager.RegisterRoutedEvent(nameof(ValuesChanged), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(Select));
    public static readonly RoutedEvent SearchChangedEvent = EventManager.RegisterRoutedEvent(nameof(SearchChanged), RoutingStrategy.Bubble, typeof(RoutedPropertyChangedEventHandler<string>), typeof(Select));

    private readonly Typeahead _typeahead = new();
    private readonly HashSet<string> _createdValues = new(StringComparer.Ordinal);
    private Popup? _popup;
    private ListBox? _list;
    private TextBox? _search;
    private FrameworkElement? _field;
    private bool _choosing;

    static Select()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(Select), new FrameworkPropertyMetadata(typeof(Select)));
        FocusableProperty.OverrideMetadata(typeof(Select), new FrameworkPropertyMetadata(true));
    }

    public Select()
    {
        Sl.SetKind(this, SlKind.Field);
        AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(OnButtonClick));
    }

    public IEnumerable? Items { get => (IEnumerable?)GetValue(ItemsProperty); set => SetValue(ItemsProperty, value); }

    /// <summary>Selected item (single mode, two-way).</summary>
    public object? Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }

    /// <summary>Selected items (multiple mode, two-way). Slate assigns a new list on every change.</summary>
    public IList? Values { get => (IList?)GetValue(ValuesProperty); set => SetValue(ValuesProperty, value); }

    public bool Multiple { get => (bool)GetValue(MultipleProperty); set => SetValue(MultipleProperty, value); }

    /// <summary>Combobox: type in the field to filter options.</summary>
    public bool Searchable { get => (bool)GetValue(SearchableProperty); set => SetValue(SearchableProperty, value); }

    /// <summary>Offer "Create “text”" for search text that matches no option (the value is the text).</summary>
    public bool Creatable { get => (bool)GetValue(CreatableProperty); set => SetValue(CreatableProperty, value); }

    /// <summary>Label for an item (default: ToString). Used for display, search and typeahead.</summary>
    public Func<object, string>? ItemText { get => (Func<object, string>?)GetValue(ItemTextProperty); set => SetValue(ItemTextProperty, value); }

    /// <summary>Custom option rendering (DataContext is the item).</summary>
    public DataTemplate? ItemTemplate { get => (DataTemplate?)GetValue(ItemTemplateProperty); set => SetValue(ItemTemplateProperty, value); }

    /// <summary>Group key for an item; groups render with headers in first-appearance order.</summary>
    public Func<object, string>? GroupBy { get => (Func<object, string>?)GetValue(GroupByProperty); set => SetValue(GroupByProperty, value); }

    public string? Label { get => (string?)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public string? Placeholder { get => (string?)GetValue(PlaceholderProperty); set => SetValue(PlaceholderProperty, value); }
    public string? HelperText { get => (string?)GetValue(HelperTextProperty); set => SetValue(HelperTextProperty, value); }
    public string? Error { get => (string?)GetValue(ErrorProperty); set => SetValue(ErrorProperty, value); }
    public FieldVariant Variant { get => (FieldVariant)GetValue(VariantProperty); set => SetValue(VariantProperty, value); }
    public FieldVariant ActualVariant => (FieldVariant)GetValue(ActualVariantProperty);
    public ControlSize Size { get => (ControlSize)GetValue(SizeProperty); set => SetValue(SizeProperty, value); }
    public Radius Radius { get => (Radius)GetValue(RadiusProperty); set => SetValue(RadiusProperty, value); }
    public bool Clearable { get => (bool)GetValue(ClearableProperty); set => SetValue(ClearableProperty, value); }
    public bool Required { get => (bool)GetValue(RequiredProperty); set => SetValue(RequiredProperty, value); }

    /// <summary>Shows a spinner (e.g. while server-side search runs).</summary>
    public bool Loading { get => (bool)GetValue(LoadingProperty); set => SetValue(LoadingProperty, value); }

    /// <summary>Multiple mode: chips beyond this collapse into "+N".</summary>
    public int? MaxVisibleChips { get => (int?)GetValue(MaxVisibleChipsProperty); set => SetValue(MaxVisibleChipsProperty, value); }

    /// <summary>Shown when no option matches.</summary>
    public object? EmptyContent { get => GetValue(EmptyContentProperty); set => SetValue(EmptyContentProperty, value); }

    public bool IsDropDownOpen { get => (bool)GetValue(IsDropDownOpenProperty); set => SetValue(IsDropDownOpenProperty, value); }

    /// <summary>Text typed in a searchable select (two-way; listen to <see cref="SearchChanged"/> for server search).</summary>
    public string SearchText { get => (string)GetValue(SearchTextProperty); set => SetValue(SearchTextProperty, value); }

    public IReadOnlyList<SelectOption> Options => (IReadOnlyList<SelectOption>)GetValue(OptionsProperty);
    public string? DisplayText => (string?)GetValue(DisplayTextProperty);
    public IReadOnlyList<SelectChip> Chips => (IReadOnlyList<SelectChip>)GetValue(ChipsProperty);
    public string? OverflowText => (string?)GetValue(OverflowTextProperty);
    public bool HasValue => (bool)GetValue(HasValueProperty);
    public bool ShowClear => (bool)GetValue(ShowClearProperty);
    public bool HasError => (bool)GetValue(HasErrorProperty);

    public event RoutedPropertyChangedEventHandler<object?> ValueChanged { add => AddHandler(ValueChangedEvent, value); remove => RemoveHandler(ValueChangedEvent, value); }
    public event RoutedEventHandler ValuesChanged { add => AddHandler(ValuesChangedEvent, value); remove => RemoveHandler(ValuesChangedEvent, value); }
    public event RoutedPropertyChangedEventHandler<string> SearchChanged { add => AddHandler(SearchChangedEvent, value); remove => RemoveHandler(SearchChangedEvent, value); }

    DependencyProperty IFieldChrome.FieldVariantProperty => VariantProperty;
    void IFieldChrome.SetActualVariant(FieldVariant variant) => SetValue(ActualVariantPropertyKey, variant);

    // ---- model ----

    private string TextOf(object? item) => item is null ? ""
        : item is string created && _createdValues.Contains(created) ? created
        : ItemText?.Invoke(item) ?? item.ToString() ?? "";

    private List<object> AllItems() => Items?.Cast<object>().ToList() ?? [];

    private IReadOnlyList<object> SelectedItems() =>
        Multiple ? Values?.Cast<object>().ToList() ?? [] : Value is { } v ? [v] : [];

    private bool IsChosen(object? item) => item is not null && SelectedItems().Any(s => Equals(s, item));

    /// <summary>Rebuilds the option list for the current items, search text and grouping.</summary>
    internal void RebuildOptions()
    {
        var items = AllItems();
        _createdValues.RemoveWhere(value => items.Any(item => Equals(item, value)));
        var texts = items.Select(TextOf).ToList();
        var query = Searchable ? SearchText : "";

        IEnumerable<int> order = string.IsNullOrWhiteSpace(query)
            ? Enumerable.Range(0, items.Count)
            : OptionFilter.Filter(texts, query).Select(m => m.Index);

        var options = new List<SelectOption>();
        var indices = order.ToList();
        if (GroupBy is { } groupBy)
        {
            var keys = indices.Select(i => groupBy(items[i]) ?? "").ToList();
            foreach (var (key, members) in OptionFilter.Group(keys))
            {
                options.Add(new SelectOption(null, key, isHeader: true));
                options.AddRange(members.Select(m => Option(indices[m])));
            }
        }
        else
        {
            options.AddRange(indices.Select(Option));
        }

        if (Creatable && Searchable && !string.IsNullOrWhiteSpace(query)
            && !texts.Any(t => string.Equals(TextFolding.Fold(t), TextFolding.Fold(query), StringComparison.Ordinal)))
            options.Add(new SelectOption(query.Trim(), $"Create “{query.Trim()}”", isCreate: true));

        SetValue(OptionsPropertyKey, options);
        if (_list is not null)
            _list.SelectedIndex = ListNavigator.Move(-1, ListKey.First, Disabled());

        SelectOption Option(int i) => new(items[i], texts[i]) { IsSelected = IsChosen(items[i]) };
    }

    private List<bool> Disabled() => Options.Select(o => o.IsHeader).ToList();

    private void RebuildAll()
    {
        RebuildOptions();
        SyncState();
    }

    private void SyncState()
    {
        var selected = SelectedItems();
        SetValue(HasValuePropertyKey, selected.Count > 0);
        SetValue(DisplayTextPropertyKey, Multiple ? null : selected.Count > 0 ? TextOf(selected[0]) : null);

        var max = MaxVisibleChips is { } m && m >= 0 ? m : int.MaxValue;
        SetValue(ChipsPropertyKey, Multiple ? selected.Take(max).Select(i => new SelectChip(i, TextOf(i))).ToList() : []);
        SetValue(OverflowTextPropertyKey, Multiple && selected.Count > max ? $"+{selected.Count - max}" : null);
        SetValue(ShowClearPropertyKey, Clearable && selected.Count > 0 && IsEnabled);

        foreach (var option in Options)
            option.IsSelected = !option.IsHeader && !option.IsCreate && IsChosen(option.Item);
    }

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var s = (Select)d;
        s.SyncState();
        s.RaiseEvent(new RoutedPropertyChangedEventArgs<object?>(e.OldValue, e.NewValue, ValueChangedEvent));
    }

    private static void OnValuesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var s = (Select)d;
        s.SyncState();
        s.RaiseEvent(new RoutedEventArgs(ValuesChangedEvent, s));
    }

    private static void OnSearchChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var s = (Select)d;
        s.RebuildOptions();
        if (s.Searchable && !string.IsNullOrEmpty((string)e.NewValue) && !s._choosing)
            s.SetCurrentValue(IsDropDownOpenProperty, true);
        s.RaiseEvent(new RoutedPropertyChangedEventArgs<string>((string)e.OldValue, (string)e.NewValue, SearchChangedEvent));
    }

    private static void OnHelpChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var s = (Select)d;
        var error = !string.IsNullOrEmpty(s.Error);
        s.SetValue(HasErrorPropertyKey, error);
        Sl.SetHasError(s, error);
        AutomationProperties.SetHelpText(s, error ? s.Error! : s.HelperText ?? "");
    }

    private static void OnOpenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var s = (Select)d;
        if ((bool)e.NewValue)
        {
            s.RebuildOptions();
            s.SyncState();
            if (s._popup?.Child is FrameworkElement panel && s._field is not null)
                panel.MinWidth = s._field.ActualWidth;
            // Start on the current value when there is one.
            var current = s.Options.Select((o, i) => (o, i)).FirstOrDefault(x => x.o.IsSelected).i;
            if (s._list is not null && s.Options.Count > 0)
                s._list.SelectedIndex = s.Options[current].IsHeader ? ListNavigator.Move(-1, ListKey.First, s.Disabled()) : current;
        }
        else if (s.Searchable && !s.Multiple)
        {
            s._choosing = true;
            s.SetCurrentValue(SearchTextProperty, "");
            s._choosing = false;
        }
        if (s._popup is not null)
            s._popup.IsOpen = (bool)e.NewValue;
        if (UIElementAutomationPeer.FromElement(s) is SelectAutomationPeer peer)
            peer.RaiseExpandCollapse((bool)e.OldValue, (bool)e.NewValue);
    }

    // ---- choosing ----

    /// <summary>Chooses an option (as a click or Enter would).</summary>
    public void Choose(SelectOption option)
    {
        if (option.IsHeader)
            return;
        _choosing = true;
        try
        {
            var item = option.Item;
            if (option.IsCreate && item is string created)
                _createdValues.Add(created);
            if (Multiple)
            {
                var values = SelectedItems().ToList();
                if (values.Any(v => Equals(v, item)))
                    values.RemoveAll(v => Equals(v, item));
                else if (item is not null)
                    values.Add(item);
                SetCurrentValue(ValuesProperty, values);
                if (Searchable)
                    SetCurrentValue(SearchTextProperty, "");
            }
            else
            {
                SetCurrentValue(ValueProperty, item);
                SetCurrentValue(IsDropDownOpenProperty, false);
                if (Searchable)
                    SetCurrentValue(SearchTextProperty, "");
            }
        }
        finally
        {
            _choosing = false;
        }
        SyncState();
    }

    public void Clear()
    {
        if (Multiple)
            SetCurrentValue(ValuesProperty, new List<object>());
        else
            SetCurrentValue(ValueProperty, null);
        SetCurrentValue(SearchTextProperty, "");
        SyncState();
    }

    private void RemoveLast()
    {
        var values = SelectedItems().ToList();
        if (values.Count == 0) return;
        values.RemoveAt(values.Count - 1);
        SetCurrentValue(ValuesProperty, values);
    }

    // ---- template & input ----

    public override void OnApplyTemplate()
    {
        if (_list is not null) _list.PreviewMouseLeftButtonUp -= OnListClick;
        if (_popup is not null) _popup.Closed -= OnPopupClosed;
        base.OnApplyTemplate();

        _popup = GetTemplateChild(PartPopup) as Popup;
        _list = GetTemplateChild(PartList) as ListBox;
        _search = GetTemplateChild(PartSearch) as TextBox;
        _field = GetTemplateChild(PartField) as FrameworkElement;

        if (_popup is not null)
        {
            _popup.Placement = PlacementMode.Custom;
            _popup.PlacementTarget = _field;
            _popup.CustomPopupPlacementCallback = PopupPositions.Callback(() => Slate.PopoverPlacement.BottomStart, () => 4);
            _popup.Closed += OnPopupClosed;
        }
        if (_list is not null) _list.PreviewMouseLeftButtonUp += OnListClick;
        RebuildAll();
    }

    private void OnPopupClosed(object? sender, EventArgs e) => SetCurrentValue(IsDropDownOpenProperty, false);

    private void OnListClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is FrameworkElement { DataContext: SelectOption option } && !option.IsHeader)
        {
            Choose(option);
            (Searchable ? _search as UIElement : this)?.Focus();
            e.Handled = true;
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (e.Handled || _field is null || e.OriginalSource is not DependencyObject source || !Popover.IsWithin(source, _field))
            return;
        if (Searchable)
        {
            _search?.Focus();
            SetCurrentValue(IsDropDownOpenProperty, true);
        }
        else
        {
            Focus();
            SetCurrentValue(IsDropDownOpenProperty, !IsDropDownOpen);
        }
        e.Handled = true;
    }

    private void OnButtonClick(object sender, RoutedEventArgs e)
    {
        switch (e.OriginalSource)
        {
            case FrameworkElement { Name: "PART_Clear" }:
                Clear();
                e.Handled = true;
                break;
            case FrameworkElement { Name: "PART_RemoveChip", DataContext: SelectChip chip }:
                var values = SelectedItems().Where(v => !Equals(v, chip.Item)).ToList();
                SetCurrentValue(ValuesProperty, values);
                e.Handled = true;
                break;
        }
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Handled || !IsEnabled)
            return;

        var key = e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key;
        ListKey? move = key switch
        {
            System.Windows.Input.Key.Down => ListKey.Next,
            System.Windows.Input.Key.Up => ListKey.Previous,
            System.Windows.Input.Key.PageDown => ListKey.PageDown,
            System.Windows.Input.Key.PageUp => ListKey.PageUp,
            System.Windows.Input.Key.Home when IsDropDownOpen && !Searchable => ListKey.First,
            System.Windows.Input.Key.End when IsDropDownOpen && !Searchable => ListKey.Last,
            _ => null,
        };

        if (move is { } m)
        {
            if (!IsDropDownOpen || Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
            {
                SetCurrentValue(IsDropDownOpenProperty, !IsDropDownOpen || m != ListKey.Previous);
            }
            else if (_list is not null)
            {
                _list.SelectedIndex = ListNavigator.Move(_list.SelectedIndex, m, Disabled());
                if (_list.SelectedItem is { } active)
                    _list.ScrollIntoView(active);
            }
            e.Handled = true;
            return;
        }

        switch (key)
        {
            case System.Windows.Input.Key.Enter:
            case System.Windows.Input.Key.Space when !Searchable:
                if (IsDropDownOpen && _list?.SelectedItem is SelectOption active)
                    Choose(active);
                else
                    SetCurrentValue(IsDropDownOpenProperty, !IsDropDownOpen);
                e.Handled = true;
                return;
            case System.Windows.Input.Key.Escape when IsDropDownOpen:
                SetCurrentValue(IsDropDownOpenProperty, false);
                e.Handled = true;
                return;
            case System.Windows.Input.Key.Back when Multiple && string.IsNullOrEmpty(SearchText):
                RemoveLast();
                e.Handled = true;
                return;
        }
    }

    protected override void OnPreviewTextInput(TextCompositionEventArgs e)
    {
        base.OnPreviewTextInput(e);
        if (Searchable || string.IsNullOrEmpty(e.Text) || char.IsControl(e.Text[0]))
            return;

        // Typeahead (native-select behaviour): closed single selects change value directly.
        var labels = Options.Select(o => o.IsHeader ? "" : o.Text).ToList();
        var current = _list?.SelectedIndex ?? -1;
        var match = _typeahead.Search(e.Text, Environment.TickCount64, labels, current, Disabled());
        if (match < 0)
            return;
        if (IsDropDownOpen || Multiple)
        {
            if (_list is not null)
            {
                _list.SelectedIndex = match;
                _list.ScrollIntoView(_list.SelectedItem);
            }
        }
        else
        {
            Choose(Options[match]);
        }
        e.Handled = true;
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        if (!IsKeyboardFocusWithin && IsDropDownOpen && _popup?.Child is UIElement child && !child.IsKeyboardFocusWithin)
            SetCurrentValue(IsDropDownOpenProperty, false);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new SelectAutomationPeer(this);
}

/// <summary>Exposes a Select as a combo box with expand/collapse and value patterns.</summary>
public sealed class SelectAutomationPeer(Select owner) : FrameworkElementAutomationPeer(owner), IExpandCollapseProvider, IValueProvider
{
    private Select Select => (Select)Owner;

    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.ComboBox;
    protected override string GetClassNameCore() => "Select";

    public override object? GetPattern(PatternInterface patternInterface) =>
        patternInterface is PatternInterface.ExpandCollapse or PatternInterface.Value ? this : base.GetPattern(patternInterface);

    public ExpandCollapseState ExpandCollapseState => Select.IsDropDownOpen ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed;
    public void Expand() => Select.IsDropDownOpen = true;
    public void Collapse() => Select.IsDropDownOpen = false;

    public bool IsReadOnly => true;
    public string Value => Select.Multiple ? string.Join(", ", Select.Chips.Select(c => c.Text)) : Select.DisplayText ?? "";
    public void SetValue(string value) => throw new InvalidOperationException("Choose an option instead.");

    internal void RaiseExpandCollapse(bool oldValue, bool newValue) =>
        RaisePropertyChangedEvent(ExpandCollapsePatternIdentifiers.ExpandCollapseStateProperty,
            oldValue ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed,
            newValue ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed);
}
