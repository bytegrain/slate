using System.Collections;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Slate.Collections;

namespace Slate.Avalonia.Controls;

/// <summary>
/// Select and combobox (docs: Select): single or <see cref="Multiple"/> values (shown as chips), optional
/// <see cref="Searchable"/> filtering with highlighted matches, <see cref="Creatable"/> values, grouping, and full
/// keyboard support (arrows, Home/End, PageUp/Down, typeahead, Enter, Escape, Backspace removes the last chip).
/// Behaviour comes from Slate.Core (ListNavigator, Typeahead, OptionFilter); styled from the field tokens.
/// </summary>
[TemplatePart("PART_Control", typeof(Border))]
[TemplatePart("PART_Popup", typeof(Popup))]
[TemplatePart("PART_Panel", typeof(Border))]
[TemplatePart("PART_Options", typeof(StackPanel))]
[TemplatePart("PART_Search", typeof(TextBox))]
[TemplatePart("PART_Chips", typeof(WrapPanel))]
[TemplatePart("PART_Clear", typeof(Button))]
[PseudoClasses(":open", ":invalid", ":required", ":multiple", ":searchable", ":has-value", ":loading")]
public class Select : TemplatedControl
{
    public static readonly StyledProperty<IEnumerable?> ItemsProperty = AvaloniaProperty.Register<Select, IEnumerable?>(nameof(Items));
    public static readonly StyledProperty<object?> ValueProperty =
        AvaloniaProperty.Register<Select, object?>(nameof(Value), defaultBindingMode: global::Avalonia.Data.BindingMode.TwoWay);
    public static readonly StyledProperty<IList?> ValuesProperty =
        AvaloniaProperty.Register<Select, IList?>(nameof(Values), defaultBindingMode: global::Avalonia.Data.BindingMode.TwoWay);
    public static readonly StyledProperty<bool> MultipleProperty = AvaloniaProperty.Register<Select, bool>(nameof(Multiple));
    public static readonly StyledProperty<bool> SearchableProperty = AvaloniaProperty.Register<Select, bool>(nameof(Searchable));
    public static readonly StyledProperty<bool> CreatableProperty = AvaloniaProperty.Register<Select, bool>(nameof(Creatable));
    public static readonly StyledProperty<Func<object?, string>?> ItemTextProperty = AvaloniaProperty.Register<Select, Func<object?, string>?>(nameof(ItemText));
    public static readonly StyledProperty<IDataTemplate?> ItemTemplateProperty = AvaloniaProperty.Register<Select, IDataTemplate?>(nameof(ItemTemplate));
    public static readonly StyledProperty<Func<object?, string>?> GroupByProperty = AvaloniaProperty.Register<Select, Func<object?, string>?>(nameof(GroupBy));
    public static readonly StyledProperty<string?> LabelProperty = AvaloniaProperty.Register<Select, string?>(nameof(Label));
    public static readonly StyledProperty<string?> PlaceholderProperty = AvaloniaProperty.Register<Select, string?>(nameof(Placeholder));
    public static readonly StyledProperty<string?> HelperTextProperty = AvaloniaProperty.Register<Select, string?>(nameof(HelperText));
    public static readonly StyledProperty<string?> ErrorProperty = AvaloniaProperty.Register<Select, string?>(nameof(Error));
    public static readonly StyledProperty<FieldVariant> VariantProperty = AvaloniaProperty.Register<Select, FieldVariant>(nameof(Variant));
    public static readonly StyledProperty<ControlSize> SizeProperty = AvaloniaProperty.Register<Select, ControlSize>(nameof(Size), ControlSize.Medium);
    public static readonly StyledProperty<Radius> RadiusProperty = AvaloniaProperty.Register<Select, Radius>(nameof(Radius));
    public static readonly StyledProperty<bool> ClearableProperty = AvaloniaProperty.Register<Select, bool>(nameof(Clearable));
    public static readonly StyledProperty<bool> RequiredProperty = AvaloniaProperty.Register<Select, bool>(nameof(Required));
    public static readonly StyledProperty<bool> LoadingProperty = AvaloniaProperty.Register<Select, bool>(nameof(Loading));
    public static readonly StyledProperty<int?> MaxVisibleChipsProperty = AvaloniaProperty.Register<Select, int?>(nameof(MaxVisibleChips));
    public static readonly StyledProperty<object?> EmptyContentProperty = AvaloniaProperty.Register<Select, object?>(nameof(EmptyContent), "No results");

    public static readonly StyledProperty<bool> IsDropDownOpenProperty =
        AvaloniaProperty.Register<Select, bool>(nameof(IsDropDownOpen), defaultBindingMode: global::Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<string?> SearchTextProperty =
        AvaloniaProperty.Register<Select, string?>(nameof(SearchText), defaultBindingMode: global::Avalonia.Data.BindingMode.TwoWay);

    public static readonly DirectProperty<Select, string?> DisplayTextProperty =
        AvaloniaProperty.RegisterDirect<Select, string?>(nameof(DisplayText), s => s.DisplayText);

    public static readonly RoutedEvent<RoutedEventArgs> ValueChangedEvent = RoutedEvent.Register<Select, RoutedEventArgs>(nameof(ValueChanged), RoutingStrategies.Bubble);
    public static readonly RoutedEvent<RoutedEventArgs> ValuesChangedEvent = RoutedEvent.Register<Select, RoutedEventArgs>(nameof(ValuesChanged), RoutingStrategies.Bubble);
    public static readonly RoutedEvent<RoutedEventArgs> SearchChangedEvent = RoutedEvent.Register<Select, RoutedEventArgs>(nameof(SearchChanged), RoutingStrategies.Bubble);

    private readonly Typeahead _typeahead = new();
    private readonly List<SelectOption> _options = [];
    private Border? _control;
    private Popup? _popup;
    private Border? _panel;
    private StackPanel? _optionsHost;
    private TextBox? _search;
    private WrapPanel? _chips;
    private Button? _clear;
    private int _highlight = -1;
    private string? _displayText;

    static Select()
    {
        FocusableProperty.OverrideDefaultValue<Select>(true);
        ItemsProperty.Changed.AddClassHandler<Select>((s, _) => s.Rebuild());
        ValueProperty.Changed.AddClassHandler<Select>((s, _) => { s.UpdateDisplay(); s.Rebuild(); s.RaiseEvent(new RoutedEventArgs(ValueChangedEvent)); });
        ValuesProperty.Changed.AddClassHandler<Select>((s, _) => { s.UpdateDisplay(); s.Rebuild(); });
        MultipleProperty.Changed.AddClassHandler<Select>((s, e) => { s.PseudoClasses.Set(":multiple", e.GetNewValue<bool>()); s.UpdateDisplay(); });
        SearchableProperty.Changed.AddClassHandler<Select>((s, e) => s.PseudoClasses.Set(":searchable", e.GetNewValue<bool>()));
        ItemTextProperty.Changed.AddClassHandler<Select>((s, _) => { s.UpdateDisplay(); s.Rebuild(); });
        GroupByProperty.Changed.AddClassHandler<Select>((s, _) => s.Rebuild());
        SearchTextProperty.Changed.AddClassHandler<Select>((s, _) =>
        {
            s._highlight = -1;
            s.Rebuild();
            s.RaiseEvent(new RoutedEventArgs(SearchChangedEvent));
        });
        IsDropDownOpenProperty.Changed.AddClassHandler<Select>((s, e) => s.OnOpenChanged(e.GetNewValue<bool>()));
        ErrorProperty.Changed.AddClassHandler<Select>((s, e) => s.PseudoClasses.Set(":invalid", !string.IsNullOrEmpty(e.GetNewValue<string?>())));
        RequiredProperty.Changed.AddClassHandler<Select>((s, e) => s.PseudoClasses.Set(":required", e.GetNewValue<bool>()));
        LoadingProperty.Changed.AddClassHandler<Select>((s, e) => s.PseudoClasses.Set(":loading", e.GetNewValue<bool>()));
        ClearableProperty.Changed.AddClassHandler<Select>((s, _) => s.UpdateDisplay());
        MaxVisibleChipsProperty.Changed.AddClassHandler<Select>((s, _) => s.UpdateDisplay());
        VariantProperty.Changed.AddClassHandler<Select>((s, _) => s.UpdateLook());
        SizeProperty.Changed.AddClassHandler<Select>((s, _) => s.UpdateLook());
        RadiusProperty.Changed.AddClassHandler<Select>((s, _) => s.UpdateLook());
        LabelProperty.Changed.AddClassHandler<Select>((s, e) => AutomationProperties.SetName(s, e.GetNewValue<string?>()));
        LoadedEvent.AddClassHandler<Select>((s, _) => s.UpdateLook());
    }

    public IEnumerable? Items { get => GetValue(ItemsProperty); set => SetValue(ItemsProperty, value); }
    public object? Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public IList? Values { get => GetValue(ValuesProperty); set => SetValue(ValuesProperty, value); }
    public bool Multiple { get => GetValue(MultipleProperty); set => SetValue(MultipleProperty, value); }
    public bool Searchable { get => GetValue(SearchableProperty); set => SetValue(SearchableProperty, value); }
    public bool Creatable { get => GetValue(CreatableProperty); set => SetValue(CreatableProperty, value); }
    public Func<object?, string>? ItemText { get => GetValue(ItemTextProperty); set => SetValue(ItemTextProperty, value); }
    public IDataTemplate? ItemTemplate { get => GetValue(ItemTemplateProperty); set => SetValue(ItemTemplateProperty, value); }
    public Func<object?, string>? GroupBy { get => GetValue(GroupByProperty); set => SetValue(GroupByProperty, value); }
    public string? Label { get => GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public string? Placeholder { get => GetValue(PlaceholderProperty); set => SetValue(PlaceholderProperty, value); }
    public string? HelperText { get => GetValue(HelperTextProperty); set => SetValue(HelperTextProperty, value); }
    public string? Error { get => GetValue(ErrorProperty); set => SetValue(ErrorProperty, value); }
    public FieldVariant Variant { get => GetValue(VariantProperty); set => SetValue(VariantProperty, value); }
    public ControlSize Size { get => GetValue(SizeProperty); set => SetValue(SizeProperty, value); }
    public Radius Radius { get => GetValue(RadiusProperty); set => SetValue(RadiusProperty, value); }
    public bool Clearable { get => GetValue(ClearableProperty); set => SetValue(ClearableProperty, value); }
    public bool Required { get => GetValue(RequiredProperty); set => SetValue(RequiredProperty, value); }
    public bool Loading { get => GetValue(LoadingProperty); set => SetValue(LoadingProperty, value); }
    public int? MaxVisibleChips { get => GetValue(MaxVisibleChipsProperty); set => SetValue(MaxVisibleChipsProperty, value); }
    public object? EmptyContent { get => GetValue(EmptyContentProperty); set => SetValue(EmptyContentProperty, value); }
    public bool IsDropDownOpen { get => GetValue(IsDropDownOpenProperty); set => SetValue(IsDropDownOpenProperty, value); }
    public string? SearchText { get => GetValue(SearchTextProperty); set => SetValue(SearchTextProperty, value); }
    public string? DisplayText { get => _displayText; private set => SetAndRaise(DisplayTextProperty, ref _displayText, value); }

    public event EventHandler<RoutedEventArgs>? ValueChanged { add => AddHandler(ValueChangedEvent, value); remove => RemoveHandler(ValueChangedEvent, value); }
    public event EventHandler<RoutedEventArgs>? ValuesChanged { add => AddHandler(ValuesChangedEvent, value); remove => RemoveHandler(ValuesChangedEvent, value); }
    public event EventHandler<RoutedEventArgs>? SearchChanged { add => AddHandler(SearchChangedEvent, value); remove => RemoveHandler(SearchChangedEvent, value); }

    /// <summary>The option rows currently shown (after filtering), for tests and automation.</summary>
    public IReadOnlyList<SelectOption> VisibleOptions => _options;

    /// <summary>Index of the keyboard-highlighted option in <see cref="VisibleOptions"/> (-1 = none).</summary>
    public int HighlightedIndex => _highlight;

    public FieldVariant EffectiveVariant => IsSet(VariantProperty) ? Variant : SlateTheme.Defaults.Field.Variant;
    public ControlSize EffectiveSize => IsSet(SizeProperty) ? Size : SlateTheme.Defaults.Field.Size;
    public Radius EffectiveRadius => IsSet(RadiusProperty) ? Radius : SlateTheme.Defaults.Field.Radius;

    public string TextOf(object? item) => item is null ? "" : ItemText?.Invoke(item) ?? item.ToString() ?? "";

    private IReadOnlyList<object?> AllItems => Items?.Cast<object?>().ToList() ?? [];

    private bool IsSelected(object? item) =>
        Multiple ? Values?.Cast<object?>().Any(v => Equals(v, item)) == true : Equals(Value, item);

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        if (_control is not null) _control.PointerPressed -= OnControlPressed;
        if (_clear is not null) _clear.Click -= OnClearClick;
        if (_popup is not null) _popup.Closed -= OnPopupClosed;

        _control = e.NameScope.Find<Border>("PART_Control");
        _popup = e.NameScope.Find<Popup>("PART_Popup");
        _panel = e.NameScope.Find<Border>("PART_Panel");
        _optionsHost = e.NameScope.Find<StackPanel>("PART_Options");
        _search = e.NameScope.Find<TextBox>("PART_Search");
        _chips = e.NameScope.Find<WrapPanel>("PART_Chips");
        _clear = e.NameScope.Find<Button>("PART_Clear");

        if (_control is not null) _control.PointerPressed += OnControlPressed;
        if (_clear is not null) _clear.Click += OnClearClick;
        if (_popup is not null && _control is not null)
        {
            SlatePlacement.Apply(_popup, _control, () => PopoverPlacement.BottomStart, () => 4, r =>
            {
                if (_panel is not null) _panel.MaxHeight = Math.Min(320, r.MaxHeight ?? 320);
            });
            _popup.Closed += OnPopupClosed;
        }
        UpdateDisplay();
        Rebuild();
        UpdateLook();
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new SelectAutomationPeer(this);

    // ---- opening ----------------------------------------------------------------------------------------

    private void OnControlPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!IsEffectivelyEnabled || e.Source is Visual v && v.FindAncestorOfType<Button>(true) is { } b && (b == _clear || b.Classes.Contains("chip-remove"))) return;
        IsDropDownOpen = !IsDropDownOpen;
        if (Searchable && IsDropDownOpen) _search?.Focus();
        else Focus();
        e.Handled = true;
    }

    private void OnOpenChanged(bool open)
    {
        PseudoClasses.Set(":open", open);
        if (_popup is null) return;
        if (open)
        {
            if (_panel is not null && _control is not null) _panel.MinWidth = _control.Bounds.Width;
            Rebuild();
            _highlight = _options.FindIndex(o => o.IsSelected && !o.IsCreate);
            if (_highlight < 0) _highlight = ListNavigator.Move(-1, ListKey.First, Disabled());
            ApplyHighlight();
            _popup.IsOpen = true;
        }
        else
        {
            _popup.IsOpen = false;
            if (!string.IsNullOrEmpty(SearchText)) SearchText = null;
        }
    }

    private void OnPopupClosed(object? sender, EventArgs e)
    {
        if (IsDropDownOpen) IsDropDownOpen = false;
    }

    // ---- keyboard -----------------------------------------------------------------------------------------

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || !IsEffectivelyEnabled) return;
        var open = IsDropDownOpen;

        switch (e.Key)
        {
            case Key.Down when !open:
            case Key.Up when !open:
            case Key.F4:
                IsDropDownOpen = true;
                e.Handled = true;
                return;
            case Key.Enter when !open:
            case Key.Space when !open && !Searchable:
                IsDropDownOpen = true;
                e.Handled = true;
                return;
            case Key.Escape when open:
                IsDropDownOpen = false;
                Focus();
                e.Handled = true;
                return;
            case Key.Tab when open:
                IsDropDownOpen = false;
                return;
            case Key.Back when Multiple && string.IsNullOrEmpty(SearchText) && Values is { Count: > 0 } values:
                var copy = values.Cast<object?>().ToList();
                copy.RemoveAt(copy.Count - 1);
                SetValues(copy);
                e.Handled = true;
                return;
        }

        if (open)
        {
            ListKey? key = e.Key switch
            {
                Key.Down => ListKey.Next,
                Key.Up => ListKey.Previous,
                Key.Home when !Searchable => ListKey.First,
                Key.End when !Searchable => ListKey.Last,
                Key.PageDown => ListKey.PageDown,
                Key.PageUp => ListKey.PageUp,
                _ => null,
            };
            if (key is { } k)
            {
                _highlight = ListNavigator.Move(_highlight, k, Disabled(), new ListNavigationOptions { PageSize = 8 });
                ApplyHighlight();
                e.Handled = true;
                return;
            }
            if (e.Key is Key.Enter || (e.Key == Key.Space && !Searchable))
            {
                if (_highlight >= 0 && _highlight < _options.Count) Choose(_options[_highlight]);
                e.Handled = true;
            }
        }
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);
        if (e.Handled || Searchable || string.IsNullOrEmpty(e.Text) || e.Text == " ") return;

        var labels = _options.Select(o => o.Text).ToList();
        var current = IsDropDownOpen ? _highlight : _options.FindIndex(o => o.IsSelected);
        var found = _typeahead.Search(e.Text, Environment.TickCount64, labels, current);
        if (found < 0) return;

        if (IsDropDownOpen)
        {
            _highlight = found;
            ApplyHighlight();
        }
        else if (!Multiple)
        {
            Value = _options[found].Item;
        }
        e.Handled = true;
    }

    private IReadOnlyList<bool> Disabled() => _options.Select(_ => false).ToList();

    // ---- options ------------------------------------------------------------------------------------------

    private void Rebuild()
    {
        if (_optionsHost is null) return;
        _optionsHost.Children.Clear();
        _options.Clear();

        var items = AllItems;
        var labels = items.Select(TextOf).ToList();
        IReadOnlyList<OptionMatch> matches = Searchable
            ? OptionFilter.Filter(labels, SearchText)
            : labels.Select((_, i) => new OptionMatch(i, 0, [])).ToList();

        // Groups keep source order of first appearance; matches stay ranked inside each group.
        var ordered = matches.ToList();
        if (GroupBy is { } groupBy)
        {
            var keys = items.Select(i => groupBy(i)).ToList();
            var groups = OptionFilter.Group(keys).Select(g => g.Key).ToList();
            ordered = ordered.OrderBy(m => groups.IndexOf(keys[m.Index])).ToList();
            string? current = null;
            foreach (var m in ordered)
            {
                var key = keys[m.Index];
                if (key != current)
                {
                    current = key;
                    _optionsHost.Children.Add(new TextBlock { Text = key, Classes = { "select-group" } });
                }
                AddOption(items[m.Index], labels[m.Index], m.Ranges);
            }
        }
        else
        {
            foreach (var m in ordered)
                AddOption(items[m.Index], labels[m.Index], m.Ranges);
        }

        if (Searchable && Creatable && SearchText is { Length: > 0 } query
            && !labels.Any(l => TextFolding.Fold(l) == TextFolding.Fold(query.Trim())))
        {
            var create = new SelectOption(this, query.Trim(), $"Create “{query.Trim()}”", [], isCreate: true);
            _options.Add(create);
            _optionsHost.Children.Add(create);
        }

        if (_options.Count == 0)
            _optionsHost.Children.Add(new ContentControl { Content = EmptyContent, Classes = { "select-empty" } });

        if (_highlight >= _options.Count) _highlight = _options.Count - 1;
        if (IsDropDownOpen && _highlight < 0 && _options.Count > 0) _highlight = 0;
        ApplyHighlight();
    }

    private void AddOption(object? item, string label, IReadOnlyList<TextRange> ranges)
    {
        var option = new SelectOption(this, item, label, ranges) { IsSelected = IsSelected(item) };
        _options.Add(option);
        _optionsHost!.Children.Add(option);
    }

    private void ApplyHighlight()
    {
        for (var i = 0; i < _options.Count; i++)
            _options[i].IsHighlighted = i == _highlight;
        if (_highlight >= 0 && _highlight < _options.Count)
            _options[_highlight].BringIntoView();
    }

    internal void Choose(SelectOption option)
    {
        var item = option.Item;
        if (Multiple)
        {
            var list = Values?.Cast<object?>().ToList() ?? [];
            var existing = list.FindIndex(v => Equals(v, item));
            if (existing >= 0) list.RemoveAt(existing);
            else list.Add(item);
            SetValues(list);
            if (option.IsCreate) SearchText = null;
        }
        else
        {
            Value = item;
            IsDropDownOpen = false;
            Focus();
        }
    }

    private void SetValues(List<object?> list)
    {
        Values = list;
        RaiseEvent(new RoutedEventArgs(ValuesChangedEvent));
    }

    internal void RemoveValue(object? item)
    {
        var list = Values?.Cast<object?>().Where(v => !Equals(v, item)).ToList() ?? [];
        SetValues(list);
    }

    private void OnClearClick(object? sender, RoutedEventArgs e)
    {
        if (Multiple) SetValues([]);
        else Value = null;
        e.Handled = true;
        Focus();
    }

    // ---- display --------------------------------------------------------------------------------------------

    private void UpdateDisplay()
    {
        var hasValue = Multiple ? Values is { Count: > 0 } : Value is not null;
        PseudoClasses.Set(":has-value", hasValue);
        DisplayText = Multiple ? null : Value is null ? null : TextOf(Value);
        if (_clear is not null) _clear.IsVisible = Clearable && hasValue;

        if (_chips is null) return;
        _chips.Children.Clear();
        if (!Multiple || Values is null) return;

        var values = Values.Cast<object?>().ToList();
        var max = MaxVisibleChips ?? int.MaxValue;
        foreach (var v in values.Take(max))
            _chips.Children.Add(Chip(v));
        if (values.Count > max)
            _chips.Children.Add(new Badge { Content = $"+{values.Count - max}", Margin = new Thickness(0, 2, 4, 2), VerticalAlignment = VerticalAlignment.Center });
    }

    private Control Chip(object? value)
    {
        var remove = new Button { Classes = { "ghost", "icon-only", "chip-remove" }, Focusable = false, Padding = default, Width = 16, Height = 16, MinWidth = 16, MinHeight = 16 };
        Slate.Avalonia.Sl.SetSize(remove, ControlSize.Small);
        Slate.Avalonia.Sl.SetStartIcon(remove, "x");
        Slate.Avalonia.Sl.SetLabel(remove, $"Remove {TextOf(value)}");
        remove.Click += (_, e) => { RemoveValue(value); e.Handled = true; };
        return new Border
        {
            Classes = { "select-chip" },
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 2,
                Children = { new TextBlock { Text = TextOf(value), VerticalAlignment = VerticalAlignment.Center }, remove },
            },
        };
    }

    private void UpdateLook()
    {
        var size = EffectiveSize;
        Classes.Set("small", size == ControlSize.Small);
        Classes.Set("large", size == ControlSize.Large);
        var variant = EffectiveVariant;
        Classes.Set("filled", variant == FieldVariant.Filled);
        Classes.Set("underlined", variant == FieldVariant.Underlined);
        Slate.Avalonia.Sl.SetOne(this, Slate.Avalonia.Sl.RadiusClasses, Slate.Avalonia.Sl.ClassFor(EffectiveRadius));
    }

    private sealed class SelectAutomationPeer(Select owner) : ControlAutomationPeer(owner), IExpandCollapseProvider, IValueProvider
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.ComboBox;
        protected override string? GetNameCore() => owner.Label ?? base.GetNameCore();

        public ExpandCollapseState ExpandCollapseState => owner.IsDropDownOpen ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed;
        public bool ShowsMenu => false;
        public void Expand() => owner.IsDropDownOpen = true;
        public void Collapse() => owner.IsDropDownOpen = false;

        public bool IsReadOnly => true;
        public string? Value => owner.Multiple
            ? string.Join(", ", owner.Values?.Cast<object?>().Select(owner.TextOf) ?? [])
            : owner.DisplayText;
        public void SetValue(string? value) => throw new InvalidOperationException("Select values are chosen from its options.");
    }
}

/// <summary>An option row inside a <see cref="Select"/> dropdown (matched text is bold).</summary>
[PseudoClasses(":selected", ":highlighted")]
public class SelectOption : TemplatedControl
{
    public static readonly StyledProperty<bool> IsSelectedProperty = AvaloniaProperty.Register<SelectOption, bool>(nameof(IsSelected));
    public static readonly StyledProperty<bool> IsHighlightedProperty = AvaloniaProperty.Register<SelectOption, bool>(nameof(IsHighlighted));
    public static readonly StyledProperty<object?> ContentProperty = AvaloniaProperty.Register<SelectOption, object?>(nameof(Content));

    private readonly Select _owner;

    static SelectOption()
    {
        IsSelectedProperty.Changed.AddClassHandler<SelectOption>((o, e) => o.PseudoClasses.Set(":selected", e.GetNewValue<bool>()));
        IsHighlightedProperty.Changed.AddClassHandler<SelectOption>((o, e) => o.PseudoClasses.Set(":highlighted", e.GetNewValue<bool>()));
    }

    internal SelectOption(Select owner, object? item, string text, IReadOnlyList<TextRange> ranges, bool isCreate = false)
    {
        _owner = owner;
        Item = item;
        Text = text;
        IsCreate = isCreate;
        AutomationProperties.SetName(this, text);
        Content = owner.ItemTemplate is { } template && !isCreate ? template.Build(item) : Highlighted(text, ranges);
        if (owner.ItemTemplate is not null && Content is Control c) c.DataContext = item;
    }

    public object? Item { get; }
    public string Text { get; }
    public bool IsCreate { get; }
    public bool IsSelected { get => GetValue(IsSelectedProperty); set => SetValue(IsSelectedProperty, value); }
    public bool IsHighlighted { get => GetValue(IsHighlightedProperty); set => SetValue(IsHighlightedProperty, value); }
    public object? Content { get => GetValue(ContentProperty); set => SetValue(ContentProperty, value); }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        _owner.Choose(this);
        e.Handled = true;
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new OptionPeer(this);

    private static TextBlock Highlighted(string text, IReadOnlyList<TextRange> ranges)
    {
        var block = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        if (ranges.Count == 0)
        {
            block.Text = text;
            return block;
        }
        var inlines = new InlineCollection();
        var pos = 0;
        foreach (var r in ranges.OrderBy(r => r.Start))
        {
            if (r.Start > pos) inlines.Add(new Run(text[pos..r.Start]));
            inlines.Add(new Run(text.Substring(r.Start, r.Length)) { FontWeight = FontWeight.Bold });
            pos = r.Start + r.Length;
        }
        if (pos < text.Length) inlines.Add(new Run(text[pos..]));
        block.Inlines = inlines;
        return block;
    }

    private sealed class OptionPeer(SelectOption owner) : ControlAutomationPeer(owner), ISelectionItemProvider
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.ListItem;
        public bool IsSelected => owner.IsSelected;
        public ISelectionProvider? SelectionContainer => null;
        public void AddToSelection() => owner._owner.Choose(owner);
        public void RemoveFromSelection() => owner._owner.Choose(owner);
        public void Select() => owner._owner.Choose(owner);
    }
}
