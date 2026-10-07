using System.Globalization;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Slate.Data;

namespace Slate.Avalonia.Controls;

/// <summary>
/// The filter popover for one column: type-aware operators and value editors (text field, number, date picker, enum
/// multi-select, boolean). Apply writes a <see cref="GridFilter"/> into the grid's state; Clear removes it.
/// </summary>
public class DataGridFilterEditor : StackPanel
{
    private readonly DataGrid _grid;
    private readonly GridColumn<object> _column;
    private readonly Action _close;
    private readonly Select _operator = new() { Label = "Condition", Size = ControlSize.Small };
    private readonly TextField _value = new() { Label = "Value", Size = ControlSize.Small };
    private readonly TextField _value2 = new() { Label = "and", Size = ControlSize.Small };
    private readonly DatePicker _date = new() { Label = "Date", Size = ControlSize.Small };
    private readonly DatePicker _date2 = new() { Label = "and", Size = ControlSize.Small };
    private readonly Select _options = new() { Label = "Values", Multiple = true, Searchable = true, Size = ControlSize.Small };
    private readonly SegmentedControl _bool = new();

    public DataGridFilterEditor(DataGrid grid, string field, Action close)
    {
        _grid = grid;
        _column = grid.EngineColumns.First(c => c.Field == field);
        _close = close;
        Spacing = 10;
        Width = 260;
        Classes.Add("sl-grid-filter");

        Children.Add(new TextBlock { Text = $"Filter {_column.DisplayTitle}", Classes = { "sl-grid-popover-title" } });

        var ops = Operators(_column.Type);
        _operator.Items = ops.Select(o => o.Label).ToList();
        var existing = grid.State.Filters.FirstOrDefault(f => f.Field == field);

        switch (_column.Type)
        {
            case GridColumnType.Enum:
                var values = (_column.EnumOrder ?? DistinctTexts()).ToList();
                _options.Items = values;
                _options.Values = existing?.Values?.Select(v => (object?)v?.ToString()).ToList() ?? [];
                Children.Add(_options);
                break;
            case GridColumnType.Boolean:
                _bool.Items = new[] { "Any", "Yes", "No" };
                _bool.Value = existing?.Value is bool b ? (b ? "Yes" : "No") : "Any";
                AutomationProperties.SetName(_bool, "Value");
                Children.Add(_bool);
                break;
            default:
                _operator.Value = ops.FirstOrDefault(o => o.Op == existing?.Operator).Label ?? ops[0].Label;
                Children.Add(_operator);
                if (_column.Type == GridColumnType.Date)
                {
                    _date.Value = AsDate(existing?.Value);
                    _date2.Value = AsDate(existing?.Value2);
                    Children.Add(_date);
                    Children.Add(_date2);
                }
                else
                {
                    _value.Value = existing?.Value?.ToString();
                    _value2.Value = existing?.Value2?.ToString();
                    if (_column.Type is GridColumnType.Number or GridColumnType.Progress) { _value.InputType = "number"; _value2.InputType = "number"; }
                    Children.Add(_value);
                    Children.Add(_value2);
                }
                _operator.PropertyChanged += (_, e) => { if (e.Property == Select.ValueProperty) UpdateVisibility(); };
                UpdateVisibility();
                break;
        }

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Right };
        var clear = new Button { Content = "Clear", Classes = { "ghost" } };
        var apply = new Button { Content = "Apply" };
        Sl.SetSize(clear, ControlSize.Small);
        Sl.SetSize(apply, ControlSize.Small);
        Sl.SetVariant(apply, ButtonVariant.Solid);
        Sl.SetTone(apply, Tone.Accent);
        clear.Click += (_, _) => { _grid.ClearFilter(field); _close(); };
        apply.Click += (_, _) => { ApplyFilter(); _close(); };
        buttons.Children.Add(clear);
        buttons.Children.Add(apply);
        Children.Add(buttons);
    }

    /// <summary>The filter the editor's current inputs describe (null = no filter).</summary>
    public GridFilter? CurrentFilter()
    {
        switch (_column.Type)
        {
            case GridColumnType.Enum:
                var vals = _options.Values?.Cast<object?>().ToList() ?? [];
                return vals.Count == 0 ? null : new GridFilter(_column.Field, FilterOperator.AnyOf, Values: vals);
            case GridColumnType.Boolean:
                return (_bool.Value as string) switch
                {
                    "Yes" => new GridFilter(_column.Field, FilterOperator.Equals, true),
                    "No" => new GridFilter(_column.Field, FilterOperator.Equals, false),
                    _ => null,
                };
        }

        var op = Operators(_column.Type).First(o => o.Label == (_operator.Value as string ?? Operators(_column.Type)[0].Label)).Op;
        if (op is FilterOperator.IsEmpty or FilterOperator.IsNotEmpty)
            return new GridFilter(_column.Field, op);

        object? v1, v2;
        if (_column.Type == GridColumnType.Date)
        {
            v1 = _date.Value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            v2 = _date2.Value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }
        else if (_column.Type is GridColumnType.Number or GridColumnType.Progress)
        {
            v1 = double.TryParse(_value.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var a) ? a : null;
            v2 = double.TryParse(_value2.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var b) ? b : null;
        }
        else
        {
            v1 = string.IsNullOrEmpty(_value.Value) ? null : _value.Value;
            v2 = null;
        }
        return v1 is null ? null : new GridFilter(_column.Field, op, v1, op == FilterOperator.Between ? v2 : null);
    }

    public void ApplyFilter()
    {
        if (CurrentFilter() is { } f) _grid.SetFilter(f);
        else _grid.ClearFilter(_column.Field);
    }

    private void UpdateVisibility()
    {
        var op = Operators(_column.Type).FirstOrDefault(o => o.Label == _operator.Value as string).Op;
        var needsValue = op is not (FilterOperator.IsEmpty or FilterOperator.IsNotEmpty);
        var between = op == FilterOperator.Between;
        _value.IsVisible = needsValue && _column.Type != GridColumnType.Date;
        _value2.IsVisible = between && _column.Type != GridColumnType.Date;
        _date.IsVisible = needsValue && _column.Type == GridColumnType.Date;
        _date2.IsVisible = between && _column.Type == GridColumnType.Date;
    }

    private IEnumerable<string> DistinctTexts() =>
        (_grid.Result?.Items ?? []).Take(5000).Select(i => _column.DisplayText(_column.GetValue(i))).Where(t => t.Length > 0).Distinct().Order();

    private static DateOnly? AsDate(object? v) => v switch
    {
        DateOnly d => d,
        DateTime dt => DateOnly.FromDateTime(dt),
        DateTimeOffset dto => DateOnly.FromDateTime(dto.UtcDateTime),
        string s when DateOnly.TryParse(s, CultureInfo.InvariantCulture, out var d) => d,
        string s when DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, out var dto) => DateOnly.FromDateTime(dto.UtcDateTime),
        _ => null,
    };

    internal static IReadOnlyList<(FilterOperator Op, string Label)> Operators(GridColumnType type) => type switch
    {
        GridColumnType.Number or GridColumnType.Progress or GridColumnType.Date =>
        [
            (FilterOperator.Equals, "="), (FilterOperator.NotEquals, "≠"), (FilterOperator.LessThan, "<"), (FilterOperator.LessThanOrEqual, "≤"),
            (FilterOperator.GreaterThan, ">"), (FilterOperator.GreaterThanOrEqual, "≥"), (FilterOperator.Between, "between"),
            (FilterOperator.IsEmpty, "is empty"), (FilterOperator.IsNotEmpty, "is not empty"),
        ],
        _ =>
        [
            (FilterOperator.Contains, "contains"), (FilterOperator.Equals, "equals"), (FilterOperator.NotEquals, "does not equal"),
            (FilterOperator.StartsWith, "starts with"), (FilterOperator.EndsWith, "ends with"),
            (FilterOperator.IsEmpty, "is empty"), (FilterOperator.IsNotEmpty, "is not empty"),
        ],
    };

    /// <summary>Short text for a filter chip, e.g. "≥ 40", "contains web", "any of Ready, Failed".</summary>
    public static string Describe(GridFilter f)
    {
        static string V(object? v) => v switch
        {
            null => "",
            double d => d.ToString("0.##", CultureInfo.InvariantCulture),
            bool b => b ? "yes" : "no",
            _ => v.ToString() ?? "",
        };
        return f.Operator switch
        {
            FilterOperator.AnyOf => "is " + string.Join(", ", (f.Values ?? []).Select(V)),
            FilterOperator.Between => $"between {V(f.Value)} and {V(f.Value2)}",
            FilterOperator.IsEmpty => "is empty",
            FilterOperator.IsNotEmpty => "is not empty",
            FilterOperator.Contains => $"contains {V(f.Value)}",
            FilterOperator.StartsWith => $"starts with {V(f.Value)}",
            FilterOperator.EndsWith => $"ends with {V(f.Value)}",
            FilterOperator.Equals => $"= {V(f.Value)}",
            FilterOperator.NotEquals => $"≠ {V(f.Value)}",
            FilterOperator.LessThan => $"< {V(f.Value)}",
            FilterOperator.LessThanOrEqual => $"≤ {V(f.Value)}",
            FilterOperator.GreaterThan => $"> {V(f.Value)}",
            FilterOperator.GreaterThanOrEqual => $"≥ {V(f.Value)}",
            _ => V(f.Value),
        };
    }
}
