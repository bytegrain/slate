using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Slate.Data;

/// <summary>A sort key: one column and its direction. Earlier entries in <see cref="GridState.Sorts"/> take priority.</summary>
public sealed record GridSort(string Field, SortDirection Direction);

/// <summary>
/// A column filter. Values are JSON primitives (string, double, bool or null) and are coerced to the column type when
/// evaluated: dates as ISO strings, numbers as numbers or numeric strings.
/// </summary>
public sealed record GridFilter(string Field, FilterOperator Operator, object? Value = null, object? Value2 = null, IReadOnlyList<object?>? Values = null)
{
    public bool Equals(GridFilter? other) =>
        other is not null && Field == other.Field && Operator == other.Operator && Equals(Value, other.Value) && Equals(Value2, other.Value2)
        && (Values ?? []).SequenceEqual(other.Values ?? []);

    public override int GetHashCode() => HashCode.Combine(Field, Operator, Value, Value2, Values?.Count ?? 0);
}

/// <summary>Per-column layout overrides (null = use the column definition).</summary>
public sealed record GridColumnState(string Field, double? Width = null, bool? Hidden = null, GridPin? Pinned = null);

/// <summary>
/// Everything a user can change about a grid's view, as an immutable, serialisable value. Persist it with
/// <see cref="ToJson"/> / <see cref="FromJson"/>. Transition methods return a new state.
/// </summary>
public sealed record GridState
{
    /// <summary>
    /// Explicit display order, set only by <see cref="MoveColumn"/>. Empty = definition order; fields not listed
    /// follow, in definition order. Kept separate from <see cref="Columns"/> so that resizing, pinning or hiding a
    /// column never moves it.
    /// </summary>
    public IReadOnlyList<string> Order { get; init; } = [];

    /// <summary>Per-column overrides (width, visibility, pinning). The order of this list carries no meaning.</summary>
    public IReadOnlyList<GridColumnState> Columns { get; init; } = [];
    public IReadOnlyList<GridSort> Sorts { get; init; } = [];
    public IReadOnlyList<GridFilter> Filters { get; init; } = [];
    public string QuickFilter { get; init; } = "";

    /// <summary>Group-by fields, outermost first. Ignored for tree data.</summary>
    public IReadOnlyList<string> GroupBy { get; init; } = [];

    /// <summary>Default for group rows and tree nodes; <see cref="ToggledGroups"/> lists the exceptions.</summary>
    public bool GroupsCollapsedByDefault { get; init; }

    /// <summary>Group ids (and "row:{key}" tree node ids) whose expansion differs from the default.</summary>
    public IReadOnlyList<string> ToggledGroups { get; init; } = [];

    /// <summary>Row keys whose detail row is open.</summary>
    public IReadOnlyList<string> ExpandedDetails { get; init; } = [];

    public int PageIndex { get; init; }
    public int PageSize { get; init; } = 50;

    public static GridState Empty { get; } = new();

    public bool IsCollapsed(string groupId) => GroupsCollapsedByDefault ^ ToggledGroups.Contains(groupId);

    public bool HasFilters => Filters.Count > 0 || !string.IsNullOrWhiteSpace(QuickFilter);

    public GridColumnState? Column(string field) => Columns.FirstOrDefault(c => c.Field == field);

    public SortDirection? SortOf(string field) => Sorts.FirstOrDefault(s => s.Field == field)?.Direction;

    // ---- transitions -------------------------------------------------------------------------------------

    /// <summary>
    /// Header click. Cycles ascending → descending → none. Without <paramref name="additive"/> the column becomes the
    /// only sort; with it (Shift+click) the column is appended or cycled in place, keeping the others.
    /// </summary>
    public GridState ToggleSort(string field, bool additive = false)
    {
        var current = SortOf(field);
        SortDirection? next = current switch
        {
            null => SortDirection.Ascending,
            SortDirection.Ascending => SortDirection.Descending,
            _ => null,
        };

        if (!additive)
            return this with { Sorts = next is { } d ? [new GridSort(field, d)] : [], PageIndex = 0 };

        var list = Sorts.ToList();
        var i = list.FindIndex(s => s.Field == field);
        if (next is null) list.RemoveAt(i);
        else if (i >= 0) list[i] = new GridSort(field, next.Value);
        else list.Add(new GridSort(field, next.Value));
        return this with { Sorts = list, PageIndex = 0 };
    }

    public GridState SetSorts(params GridSort[] sorts) => this with { Sorts = sorts, PageIndex = 0 };

    /// <summary>Adds or replaces the filter for <see cref="GridFilter.Field"/>.</summary>
    public GridState SetFilter(GridFilter filter) =>
        this with { Filters = [.. Filters.Where(f => f.Field != filter.Field), filter], PageIndex = 0 };

    public GridState ClearFilter(string field) => this with { Filters = [.. Filters.Where(f => f.Field != field)], PageIndex = 0 };

    public GridState ClearFilters() => this with { Filters = [], QuickFilter = "", PageIndex = 0 };

    public GridState SetQuickFilter(string text) => this with { QuickFilter = text ?? "", PageIndex = 0 };

    /// <summary>Moves <paramref name="field"/> to <paramref name="toIndex"/> within <paramref name="currentOrder"/> (all column fields, as displayed).</summary>
    public GridState MoveColumn(IReadOnlyList<string> currentOrder, string field, int toIndex)
    {
        var order = currentOrder.ToList();
        if (!order.Remove(field)) return this;
        order.Insert(Math.Clamp(toIndex, 0, order.Count), field);
        return this with { Order = order };
    }

    /// <summary>Sets a column width, clamped to [<paramref name="min"/>, <paramref name="max"/>].</summary>
    public GridState ResizeColumn(string field, double width, double min = 48, double max = 2000) =>
        Update(field, c => c with { Width = Math.Round(Math.Clamp(width, min, max), 1) });

    public GridState PinColumn(string field, GridPin pin) => Update(field, c => c with { Pinned = pin });

    public GridState SetColumnHidden(string field, bool hidden) => Update(field, c => c with { Hidden = hidden });

    public GridState SetGroupBy(params string[] fields) => this with { GroupBy = fields, ToggledGroups = [], PageIndex = 0 };

    public GridState AddGroupBy(string field) =>
        GroupBy.Contains(field) ? this : this with { GroupBy = [.. GroupBy, field], ToggledGroups = [], PageIndex = 0 };

    public GridState RemoveGroupBy(string field) => this with { GroupBy = [.. GroupBy.Where(g => g != field)], ToggledGroups = [], PageIndex = 0 };

    /// <summary>Flips one group (or "row:{key}" tree node).</summary>
    public GridState ToggleGroup(string groupId) =>
        this with { ToggledGroups = ToggledGroups.Contains(groupId) ? [.. ToggledGroups.Where(g => g != groupId)] : [.. ToggledGroups, groupId] };

    public GridState SetGroupExpanded(string groupId, bool expanded) => IsCollapsed(groupId) == !expanded ? this : ToggleGroup(groupId);

    public GridState ExpandAll() => this with { GroupsCollapsedByDefault = false, ToggledGroups = [] };

    public GridState CollapseAll() => this with { GroupsCollapsedByDefault = true, ToggledGroups = [] };

    public GridState ToggleDetail(string rowKey) =>
        this with { ExpandedDetails = ExpandedDetails.Contains(rowKey) ? [.. ExpandedDetails.Where(k => k != rowKey)] : [.. ExpandedDetails, rowKey] };

    public GridState SetPage(int pageIndex) => this with { PageIndex = Math.Max(0, pageIndex) };

    public GridState SetPageSize(int pageSize) => this with { PageSize = Math.Max(1, pageSize), PageIndex = 0 };

    private GridState Update(string field, Func<GridColumnState, GridColumnState> change)
    {
        var list = Columns.ToList();
        var i = list.FindIndex(c => c.Field == field);
        if (i >= 0) list[i] = change(list[i]);
        else list.Add(change(new GridColumnState(field)));
        return this with { Columns = list };
    }

    // ---- equality (lists compare by content) -------------------------------------------------------------

    public bool Equals(GridState? other) => other is not null && ToJson() == other.ToJson();

    public override int GetHashCode() => ToJson().GetHashCode(StringComparison.Ordinal);

    // ---- JSON ----------------------------------------------------------------------------------------------

    /// <summary>Stable JSON (camelCase keys and enum values); the TypeScript port reads and writes the same shape.</summary>
    public string ToJson(bool indented = false) =>
        ToJsonNode().ToJsonString(new JsonSerializerOptions { WriteIndented = indented });

    public JsonObject ToJsonNode() => new()
    {
        ["order"] = new JsonArray([.. Order.Select(f => (JsonNode)JsonValue.Create(f))]),
        ["columns"] = new JsonArray([.. Columns.Select(c =>
        {
            var o = new JsonObject { ["field"] = c.Field };
            if (c.Width is { } w) o["width"] = w;
            if (c.Hidden is { } h) o["hidden"] = h;
            if (c.Pinned is { } p) o["pinned"] = Camel(p);
            return (JsonNode)o;
        })]),
        ["sorts"] = new JsonArray([.. Sorts.Select(s => (JsonNode)new JsonObject { ["field"] = s.Field, ["direction"] = Camel(s.Direction) })]),
        ["filters"] = new JsonArray([.. Filters.Select(f =>
        {
            var o = new JsonObject { ["field"] = f.Field, ["operator"] = Camel(f.Operator) };
            if (f.Value is not null) o["value"] = ToNode(f.Value);
            if (f.Value2 is not null) o["value2"] = ToNode(f.Value2);
            if (f.Values is not null) o["values"] = new JsonArray([.. f.Values.Select(ToNode)]);
            return (JsonNode)o;
        })]),
        ["quickFilter"] = QuickFilter,
        ["groupBy"] = new JsonArray([.. GroupBy.Select(g => (JsonNode)JsonValue.Create(g))]),
        ["groupsCollapsedByDefault"] = GroupsCollapsedByDefault,
        ["toggledGroups"] = new JsonArray([.. ToggledGroups.Select(g => (JsonNode)JsonValue.Create(g))]),
        ["expandedDetails"] = new JsonArray([.. ExpandedDetails.Select(g => (JsonNode)JsonValue.Create(g))]),
        ["pageIndex"] = PageIndex,
        ["pageSize"] = PageSize,
    };

    public static GridState FromJson(string json) =>
        FromJsonNode(JsonNode.Parse(json) as JsonObject ?? throw new FormatException("Grid state JSON must be an object."));

    public static GridState FromJsonNode(JsonObject o) => new()
    {
        Order = Array(o, "order").Select(n => n.GetValue<string>()).ToList(),
        Columns = Array(o, "columns").Select(n => new GridColumnState(
            Str(n, "field"),
            n["width"] is { } w ? (double?)FromNode(w) : null,
            n["hidden"]?.GetValue<bool>(),
            n["pinned"] is { } p ? Enum<GridPin>(p.GetValue<string>()) : null)).ToList(),
        Sorts = Array(o, "sorts").Select(n => new GridSort(Str(n, "field"), Enum<SortDirection>(Str(n, "direction")))).ToList(),
        Filters = Array(o, "filters").Select(n => new GridFilter(
            Str(n, "field"),
            Enum<FilterOperator>(Str(n, "operator")),
            FromNode(n["value"]),
            FromNode(n["value2"]),
            n["values"] is JsonArray vs ? vs.Select(FromNode).ToList() : null)).ToList(),
        QuickFilter = o["quickFilter"]?.GetValue<string>() ?? "",
        GroupBy = Array(o, "groupBy").Select(n => n.GetValue<string>()).ToList(),
        GroupsCollapsedByDefault = o["groupsCollapsedByDefault"]?.GetValue<bool>() ?? false,
        ToggledGroups = Array(o, "toggledGroups").Select(n => n.GetValue<string>()).ToList(),
        ExpandedDetails = Array(o, "expandedDetails").Select(n => n.GetValue<string>()).ToList(),
        PageIndex = o["pageIndex"] is { } pi ? (int)(double)FromNode(pi)! : 0,
        PageSize = o["pageSize"] is { } ps ? (int)(double)FromNode(ps)! : 50,
    };

    internal static string Camel<TEnum>(TEnum value) where TEnum : struct, Enum
    {
        var s = value.ToString();
        return char.ToLowerInvariant(s[0]) + s[1..];
    }

    internal static TEnum Enum<TEnum>(string text) where TEnum : struct, Enum =>
        System.Enum.TryParse<TEnum>(text, ignoreCase: true, out var v) ? v : throw new FormatException($"Unknown {typeof(TEnum).Name} '{text}'.");

    private static IEnumerable<JsonNode> Array(JsonObject o, string key) => (o[key] as JsonArray)?.Where(n => n is not null).Select(n => n!) ?? [];

    private static string Str(JsonNode n, string key) => n[key]?.GetValue<string>() ?? throw new FormatException($"Missing '{key}'.");

    internal static JsonNode? ToNode(object? v) => v switch
    {
        null => null,
        string s => JsonValue.Create(s),
        bool b => JsonValue.Create(b),
        double d => JsonValue.Create(d),
        DateTimeOffset o => JsonValue.Create(o.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture)),
        DateTime t => JsonValue.Create(t.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture)),
        IConvertible c when GridValues.ToDouble(c) is { } d && v is not string => JsonValue.Create(d),
        _ => JsonValue.Create(Convert.ToString(v, CultureInfo.InvariantCulture)),
    };

    internal static object? FromNode(JsonNode? n)
    {
        if (n is not JsonValue v) return null;
        return v.GetValueKind() switch
        {
            JsonValueKind.String => v.GetValue<string>(),
            JsonValueKind.Number => v.TryGetValue<double>(out var d) ? d : v.TryGetValue<int>(out var i) ? i : v.TryGetValue<long>(out var l) ? l
                : v.TryGetValue<decimal>(out var m) ? (double)m : v.TryGetValue<float>(out var f) ? f : double.Parse(v.ToJsonString(), CultureInfo.InvariantCulture),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
        };
    }
}
