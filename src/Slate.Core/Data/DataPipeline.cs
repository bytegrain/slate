using System.Globalization;

namespace Slate.Data;

public enum GridRowKind
{
    Data,
    Group,
    Detail,
}

/// <summary>One rendered row of the grid's flattened view.</summary>
public sealed class GridViewRow<T>
{
    public GridRowKind Kind { get; init; }

    /// <summary>Index within <see cref="GridPipelineResult{T}.Rows"/>.</summary>
    public int Index { get; init; }

    /// <summary>Nesting level: group level for group rows, groups above (or tree depth) for data rows.</summary>
    public int Depth { get; init; }

    /// <summary>The data item (data and detail rows).</summary>
    public T? Item { get; init; }

    /// <summary>Row key text (data), group id (group) or "detail:{key}" (detail).</summary>
    public required string Key { get; init; }

    public string? GroupId { get; init; }
    public string? GroupField { get; init; }

    /// <summary>Raw value of the group (first row's value).</summary>
    public object? GroupKey { get; init; }

    /// <summary>Display text of the group value ("" for empty).</summary>
    public string? GroupKeyText { get; init; }

    /// <summary>Data rows in the group (all descendants).</summary>
    public int RowCount { get; init; }

    /// <summary>Aggregates by field for group rows.</summary>
    public IReadOnlyDictionary<string, object?>? Aggregates { get; init; }

    /// <summary>Group rows and tree nodes with children: whether children are shown.</summary>
    public bool Expanded { get; init; }

    /// <summary>Tree data: the node has (visible) children.</summary>
    public bool HasChildren { get; init; }

    public override string ToString() => $"{Kind} {Key} depth={Depth}";
}

/// <summary>Output of a pipeline run.</summary>
public sealed class GridPipelineResult<T>
{
    /// <summary>Rows to render (after collapse and paging).</summary>
    public required IReadOnlyList<GridViewRow<T>> Rows { get; init; }

    /// <summary>Filtered, sorted data items (ignores collapse and paging) — for export and "select all matching".</summary>
    public required IReadOnlyList<T> Items { get; init; }

    /// <summary>Row keys parallel to <see cref="Items"/>.</summary>
    public required IReadOnlyList<string> ItemKeys { get; init; }

    /// <summary>Keys of data rows in <see cref="Rows"/>, in view order (the selection model's range order).</summary>
    public required IReadOnlyList<string> VisibleKeys { get; init; }

    public int TotalCount { get; init; }
    public int FilteredCount { get; init; }

    /// <summary>Aggregates over all filtered rows, by field (footer row).</summary>
    public required IReadOnlyDictionary<string, object?> Totals { get; init; }

    /// <summary>Row count before paging.</summary>
    public int ViewRowCount { get; init; }

    public int PageIndex { get; init; }
    public int PageCount { get; init; } = 1;
}

public sealed class DataPipelineOptions<T>
{
    /// <summary>Stable row identity (selection, details, tree expansion). Defaults to the row's position.</summary>
    public Func<T, object?>? RowKey { get; init; }

    /// <summary>Tree data: children of a row. When set, grouping is ignored.</summary>
    public Func<T, IEnumerable<T>?>? ChildrenSelector { get; init; }

    /// <summary>Slice <see cref="GridPipelineResult{T}.Rows"/> to <see cref="GridState.PageIndex"/>/<see cref="GridState.PageSize"/>.</summary>
    public bool Paginate { get; init; }
}

/// <summary>
/// Client-side data pipeline: filter → stable multi-sort (nulls last) → group with aggregates (or tree flattening)
/// → detail rows → paging. Pure and deterministic; the TypeScript port produces identical results.
/// Results are cached until the items reference, <see cref="Invalidate"/> or the state changes, so scrolling
/// (which only needs <see cref="GridViewport"/>) never re-runs it.
/// </summary>
public sealed class DataPipeline<T>
{
    private readonly Dictionary<string, GridColumn<T>> _byField;
    private (IReadOnlyList<T> Items, int Version, string State, GridPipelineResult<T> Result)? _cache;
    private int _version;

    public DataPipeline(IReadOnlyList<GridColumn<T>> columns, DataPipelineOptions<T>? options = null)
    {
        Columns = columns;
        Options = options ?? new DataPipelineOptions<T>();
        _byField = columns.ToDictionary(c => c.Field);
    }

    public IReadOnlyList<GridColumn<T>> Columns { get; }
    public DataPipelineOptions<T> Options { get; }

    /// <summary>Call after mutating items in place so the next <see cref="Run"/> recomputes.</summary>
    public void Invalidate() => _version++;

    public GridColumn<T>? Column(string field) => _byField.GetValueOrDefault(field);

    public GridPipelineResult<T> Run(IReadOnlyList<T> items, GridState state)
    {
        var stateKey = state.ToJson();
        if (_cache is { } c && ReferenceEquals(c.Items, items) && c.Version == _version && c.State == stateKey)
            return c.Result;

        var result = Options.ChildrenSelector is null ? RunFlat(items, state) : RunTree(items, state);
        _cache = (items, _version, stateKey, result);
        return result;
    }

    // ---- filtering -----------------------------------------------------------------------------------------

    /// <summary>Builds the row predicate for the state's column filters and quick filter (null = everything passes).</summary>
    public Func<T, bool>? BuildPredicate(GridState state)
    {
        var tests = new List<Func<T, bool>>();
        foreach (var f in state.Filters)
        {
            if (Column(f.Field) is not { } col) continue;
            tests.Add(item => FilterMatches(col, col.GetValue(item), f));
        }

        var terms = (state.QuickFilter ?? "").ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (terms.Length > 0)
        {
            var searchable = Columns.Where(c => c.Searchable && !(state.Column(c.Field)?.Hidden ?? c.Hidden)).ToArray();
            tests.Add(item =>
            {
                var texts = new string[searchable.Length];
                for (var i = 0; i < searchable.Length; i++)
                    texts[i] = searchable[i].DisplayText(searchable[i].GetValue(item)).ToLowerInvariant();
                foreach (var term in terms)
                {
                    var hit = false;
                    foreach (var t in texts)
                        if (t.Contains(term, StringComparison.Ordinal)) { hit = true; break; }
                    if (!hit) return false;
                }
                return true;
            });
        }

        if (tests.Count == 0) return null;
        return item =>
        {
            foreach (var t in tests)
                if (!t(item)) return false;
            return true;
        };
    }

    /// <summary>Evaluates one filter against a cell value (see <see cref="FilterOperator"/>).</summary>
    public static bool FilterMatches(GridColumn<T> col, object? raw, GridFilter f)
    {
        switch (f.Operator)
        {
            case FilterOperator.IsEmpty:
                return IsEmpty(raw);
            case FilterOperator.IsNotEmpty:
                return !IsEmpty(raw);
            case FilterOperator.Contains or FilterOperator.StartsWith or FilterOperator.EndsWith:
            {
                if (raw is null) return false;
                var t = col.DisplayText(raw).ToLowerInvariant();
                var v = FilterText(f.Value).ToLowerInvariant();
                return f.Operator switch
                {
                    FilterOperator.Contains => t.Contains(v, StringComparison.Ordinal),
                    FilterOperator.StartsWith => t.StartsWith(v, StringComparison.Ordinal),
                    _ => t.EndsWith(v, StringComparison.Ordinal),
                };
            }
            case FilterOperator.AnyOf:
            {
                if (raw is null) return false;
                var t = col.DisplayText(raw).ToLowerInvariant();
                return (f.Values ?? []).Any(v => FilterText(v).ToLowerInvariant() == t);
            }
        }

        var typed = col.Type is GridColumnType.Number or GridColumnType.Progress or GridColumnType.Date or GridColumnType.Boolean;
        object? a, b, b2;
        if (typed)
        {
            a = GridValues.Normalize(raw, col.Type);
            b = GridValues.Normalize(f.Value, col.Type);
            b2 = GridValues.Normalize(f.Value2, col.Type);
        }
        else
        {
            a = raw is null ? null : col.DisplayText(raw);
            b = f.Value is null ? null : FilterText(f.Value);
            b2 = f.Value2 is null ? null : FilterText(f.Value2);
        }

        if (b is null) return true; // incomplete filter: ignore
        if (a is null) return f.Operator == FilterOperator.NotEquals;

        var cmp = typed ? GridValues.Compare(a, b) : CompareText((string)a, (string)b);
        switch (f.Operator)
        {
            case FilterOperator.Equals: return cmp == 0;
            case FilterOperator.NotEquals: return cmp != 0;
            case FilterOperator.LessThan: return cmp < 0;
            case FilterOperator.LessThanOrEqual: return cmp <= 0;
            case FilterOperator.GreaterThan: return cmp > 0;
            case FilterOperator.GreaterThanOrEqual: return cmp >= 0;
            case FilterOperator.Between:
            {
                if (b2 is null) return cmp >= 0;
                var lo = b; var hi = b2;
                if ((typed ? GridValues.Compare(lo, hi) : CompareText((string)lo, (string)hi)) > 0) (lo, hi) = (hi, lo);
                var c1 = typed ? GridValues.Compare(a, lo) : CompareText((string)a, (string)lo);
                var c2 = typed ? GridValues.Compare(a, hi) : CompareText((string)a, (string)hi);
                return c1 >= 0 && c2 <= 0;
            }
            default: return true;
        }
    }

    private static int CompareText(string a, string b) => Math.Sign(string.CompareOrdinal(a.ToLowerInvariant(), b.ToLowerInvariant()));

    private static bool IsEmpty(object? raw) => raw is null || (raw is string s && s.Trim().Length == 0);

    /// <summary>Filter values as text: strings as-is, numbers shortest round-trip, booleans "true"/"false".</summary>
    public static string FilterText(object? v) => v switch
    {
        null => "",
        string s => s,
        bool b => b ? "true" : "false",
        double d => d.ToString("R", CultureInfo.InvariantCulture),
        _ => Convert.ToString(v, CultureInfo.InvariantCulture) ?? "",
    };

    // ---- sorting -------------------------------------------------------------------------------------------

    private readonly struct SortKey
    {
        public readonly bool IsNull;
        public readonly double Num;
        public readonly string? Lower;
        public readonly string? Orig;
        public readonly object? Raw;

        public SortKey(bool isNull, double num, string? lower, string? orig, object? raw)
        {
            IsNull = isNull; Num = num; Lower = lower; Orig = orig; Raw = raw;
        }
    }

    private SortKey KeyOf(GridColumn<T> col, object? raw)
    {
        if (raw is null) return new SortKey(true, 0, null, null, null);
        if (col.Comparer is not null) return new SortKey(false, 0, null, null, raw);

        var n = GridValues.Normalize(raw, col.Type);
        switch (n)
        {
            case null: return new SortKey(true, 0, null, null, null);
            case double d: return new SortKey(false, d, null, null, raw);
            case bool b: return new SortKey(false, b ? 1 : 0, null, null, raw);
            default:
            {
                var s = (string)n;
                double idx = 0;
                if (col.Type == GridColumnType.Enum && col.EnumOrder is { Count: > 0 } order)
                {
                    idx = order.Count;
                    for (var i = 0; i < order.Count; i++)
                        if (string.Equals(order[i], s, StringComparison.OrdinalIgnoreCase)) { idx = i; break; }
                }
                return new SortKey(false, idx, s.ToLowerInvariant(), s, raw);
            }
        }
    }

    private static int CompareKeys(GridColumn<T> col, in SortKey a, in SortKey b)
    {
        if (a.IsNull || b.IsNull) return a.IsNull == b.IsNull ? 0 : a.IsNull ? 1 : -1; // nulls last (caller doesn't flip)
        if (col.Comparer is not null) return Math.Sign(col.Comparer.Compare(a.Raw, b.Raw));
        var c = a.Num.CompareTo(b.Num);
        if (c != 0) return c;
        if (a.Lower is null || b.Lower is null) return 0;
        c = string.CompareOrdinal(a.Lower, b.Lower);
        if (c != 0) return Math.Sign(c);
        return Math.Sign(string.CompareOrdinal(a.Orig, b.Orig));
    }

    /// <summary>Stable multi-column sort of <paramref name="indices"/> (into <paramref name="items"/>). Nulls sort last in both directions.</summary>
    public void Sort(IReadOnlyList<T> items, List<int> indices, IReadOnlyList<GridSort> sorts)
    {
        var active = sorts.Select(s => (Col: Column(s.Field), s.Direction)).Where(s => s.Col is not null && s.Col.Sortable).ToArray();
        if (active.Length == 0 || indices.Count < 2) return;

        var keys = new SortKey[active.Length][];
        for (var k = 0; k < active.Length; k++)
        {
            var col = active[k].Col!;
            var arr = new SortKey[items.Count];
            foreach (var i in indices) arr[i] = KeyOf(col, col.GetValue(items[i]));
            keys[k] = arr;
        }

        var span = indices.ToArray();
        Array.Sort(span, (x, y) =>
        {
            for (var k = 0; k < active.Length; k++)
            {
                var (col, dir) = active[k];
                ref readonly var a = ref keys[k][x];
                ref readonly var b = ref keys[k][y];
                if (a.IsNull || b.IsNull)
                {
                    if (a.IsNull && b.IsNull) continue;
                    return a.IsNull ? 1 : -1;
                }
                var c = CompareKeys(col!, a, b);
                if (c != 0) return dir == SortDirection.Descending ? -c : c;
            }
            return x.CompareTo(y);
        });
        indices.Clear();
        indices.AddRange(span);
    }

    // ---- aggregates ----------------------------------------------------------------------------------------

    /// <summary>Aggregates for every column with an <see cref="GridColumn{T}.Aggregate"/>, over the given rows (in order).</summary>
    public IReadOnlyDictionary<string, object?> Aggregate(IReadOnlyList<T> items, IEnumerable<int> indices)
    {
        var cols = Columns.Where(c => c.Aggregate != GridAggregate.None).ToArray();
        var result = new Dictionary<string, object?>();
        if (cols.Length == 0) return result;
        var list = indices as IReadOnlyList<int> ?? indices.ToList();
        foreach (var col in cols)
            result[col.Field] = AggregateColumn(col, list.Select(i => col.GetValue(items[i])));
        return result;
    }

    internal object? AggregateColumn(GridColumn<T> col, IEnumerable<object?> values)
    {
        switch (col.Aggregate)
        {
            case GridAggregate.Count:
                return values.Count(v => v is not null);
            case GridAggregate.Sum or GridAggregate.Avg:
            {
                double sum = 0; var n = 0;
                foreach (var v in values)
                {
                    if (GridValues.ToDouble(v) is not { } d) continue;
                    sum += d; n++;
                }
                return col.Aggregate == GridAggregate.Sum ? sum : n == 0 ? null : sum / n;
            }
            case GridAggregate.Min or GridAggregate.Max:
            {
                object? best = null; SortKey bestKey = default; var has = false;
                foreach (var v in values)
                {
                    if (v is null) continue;
                    var k = KeyOf(col, v);
                    if (k.IsNull) continue;
                    if (!has) { best = v; bestKey = k; has = true; continue; }
                    var c = CompareKeys(col, k, bestKey);
                    if (col.Aggregate == GridAggregate.Min ? c < 0 : c > 0) { best = v; bestKey = k; }
                }
                return best;
            }
            default: return null;
        }
    }

    // ---- flat (optionally grouped) -------------------------------------------------------------------------

    private string RowKeyText(T item, int index) =>
        Options.RowKey?.Invoke(item) is { } k ? GridValues.KeyText(k) : "#" + index.ToString(CultureInfo.InvariantCulture);

    private GridPipelineResult<T> RunFlat(IReadOnlyList<T> items, GridState state)
    {
        var predicate = BuildPredicate(state);
        var indices = new List<int>(items.Count);
        for (var i = 0; i < items.Count; i++)
            if (predicate is null || predicate(items[i])) indices.Add(i);

        Sort(items, indices, state.Sorts);

        var keys = new string[items.Count];
        foreach (var i in indices) keys[i] = RowKeyText(items[i], i);

        var rows = new List<GridViewRow<T>>(indices.Count);
        var groupFields = state.GroupBy.Where(f => Column(f) is not null).ToList();
        if (groupFields.Count == 0)
        {
            foreach (var i in indices) AddDataRow(rows, items[i], keys[i], 0, state);
        }
        else
        {
            AddGroups(rows, items, indices, keys, groupFields, 0, null, state);
        }

        return Finish(rows, [.. indices.Select(i => items[i])], [.. indices.Select(i => keys[i])], items.Count, indices.Count,
            Aggregate(items, indices), state);
    }

    private void AddDataRow(List<GridViewRow<T>> rows, T item, string key, int depth, GridState state, bool hasChildren = false, bool expanded = false)
    {
        rows.Add(new GridViewRow<T> { Kind = GridRowKind.Data, Index = rows.Count, Depth = depth, Item = item, Key = key, HasChildren = hasChildren, Expanded = expanded });
        if (state.ExpandedDetails.Contains(key))
            rows.Add(new GridViewRow<T> { Kind = GridRowKind.Detail, Index = rows.Count, Depth = depth, Item = item, Key = "detail:" + key });
    }

    /// <summary>Group id segment: field=keyText with '|' and '\' escaped; nested ids are joined with '|'.</summary>
    public static string GroupId(string? parentId, string field, string keyText)
    {
        var seg = field + "=" + keyText.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("|", "\\|", StringComparison.Ordinal);
        return parentId is null ? seg : parentId + "|" + seg;
    }

    private void AddGroups(List<GridViewRow<T>> rows, IReadOnlyList<T> items, List<int> indices, string[] keys, List<string> fields, int level, string? parentId, GridState state)
    {
        var col = Column(fields[level])!;
        var buckets = new List<(string Text, object? Raw, SortKey Key, List<int> Rows)>();
        var lookup = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var i in indices)
        {
            var raw = col.GetValue(items[i]);
            var text = raw is null ? "" : col.DisplayText(raw);
            var bucketKey = raw is null ? "\u0000null" : text;
            if (!lookup.TryGetValue(bucketKey, out var b))
            {
                b = buckets.Count;
                lookup[bucketKey] = b;
                buckets.Add((text, raw, KeyOf(col, raw), []));
            }
            buckets[b].Rows.Add(i);
        }

        var desc = state.SortOf(col.Field) == SortDirection.Descending;
        var order = Enumerable.Range(0, buckets.Count).ToArray();
        Array.Sort(order, (x, y) =>
        {
            var a = buckets[x].Key; var b = buckets[y].Key;
            if (a.IsNull || b.IsNull) return a.IsNull == b.IsNull ? x.CompareTo(y) : a.IsNull ? 1 : -1;
            var c = CompareKeys(col, a, b);
            if (c != 0) return desc ? -c : c;
            return x.CompareTo(y);
        });

        foreach (var bi in order)
        {
            var bucket = buckets[bi];
            var id = GroupId(parentId, col.Field, bucket.Text);
            var expanded = !state.IsCollapsed(id);
            rows.Add(new GridViewRow<T>
            {
                Kind = GridRowKind.Group, Index = rows.Count, Depth = level, Key = id, GroupId = id, GroupField = col.Field,
                GroupKey = bucket.Raw, GroupKeyText = bucket.Text, RowCount = bucket.Rows.Count,
                Aggregates = Aggregate(items, bucket.Rows), Expanded = expanded,
            });
            if (!expanded) continue;
            if (level + 1 < fields.Count)
                AddGroups(rows, items, bucket.Rows, keys, fields, level + 1, id, state);
            else
                foreach (var i in bucket.Rows) AddDataRow(rows, items[i], keys[i], level + 1, state);
        }
    }

    // ---- tree --------------------------------------------------------------------------------------------

    private sealed class Node
    {
        public required T Item;
        public required string Key;
        public required int Order;
        public bool Matches;
        public List<Node> Children = [];
    }

    private GridPipelineResult<T> RunTree(IReadOnlyList<T> items, GridState state)
    {
        var predicate = BuildPredicate(state);
        var filtering = predicate is not null;
        var total = 0;

        List<Node> Build(IEnumerable<T> source, string prefix)
        {
            var kept = new List<Node>();
            var order = 0;
            foreach (var item in source)
            {
                total++;
                var key = Options.RowKey?.Invoke(item) is { } k ? GridValues.KeyText(k) : prefix + order.ToString(CultureInfo.InvariantCulture);
                var node = new Node { Item = item, Key = key, Order = order++, Matches = predicate is null || predicate(item) };
                node.Children = Build(Options.ChildrenSelector!(item) ?? [], key.StartsWith('#') ? key + "/" : "#" + key + "/");
                if (node.Matches || node.Children.Count > 0) kept.Add(node);
            }
            SortNodes(kept, state.Sorts);
            return kept;
        }

        var roots = Build(items, "#");
        var rows = new List<GridViewRow<T>>();
        var flat = new List<Node>();

        void Walk(List<Node> nodes, int depth, bool visible)
        {
            foreach (var n in nodes)
            {
                flat.Add(n);
                var hasChildren = n.Children.Count > 0;
                var expanded = hasChildren && (filtering ? true : !state.IsCollapsed("row:" + n.Key));
                if (visible) AddDataRow(rows, n.Item, n.Key, depth, state, hasChildren, expanded);
                Walk(n.Children, depth + 1, visible && expanded);
            }
        }

        Walk(roots, 0, true);
        var flatItems = flat.Select(n => n.Item).ToList();
        return Finish(rows, flatItems, [.. flat.Select(n => n.Key)], total, flat.Count,
            Aggregate(flatItems, Enumerable.Range(0, flatItems.Count)), state);
    }

    private void SortNodes(List<Node> nodes, IReadOnlyList<GridSort> sorts)
    {
        if (nodes.Count < 2 || sorts.Count == 0) return;
        var items = nodes.Select(n => n.Item).ToList();
        var idx = Enumerable.Range(0, nodes.Count).ToList();
        Sort(items, idx, sorts);
        var sorted = idx.Select(i => nodes[i]).ToList();
        nodes.Clear();
        nodes.AddRange(sorted);
    }

    // ---- paging & result ----------------------------------------------------------------------------------

    private GridPipelineResult<T> Finish(List<GridViewRow<T>> rows, IReadOnlyList<T> items, IReadOnlyList<string> keys, int total, int filtered,
        IReadOnlyDictionary<string, object?> totals, GridState state)
    {
        var viewCount = rows.Count;
        int pageCount = 1, pageIndex = 0;
        IReadOnlyList<GridViewRow<T>> page = rows;
        if (Options.Paginate)
        {
            var size = Math.Max(1, state.PageSize);
            pageCount = Math.Max(1, (int)Math.Ceiling(viewCount / (double)size));
            pageIndex = Math.Clamp(state.PageIndex, 0, pageCount - 1);
            page = rows.Skip(pageIndex * size).Take(size).Select((r, i) => Reindex(r, i)).ToList();
        }

        return new GridPipelineResult<T>
        {
            Rows = page,
            Items = items,
            ItemKeys = keys,
            VisibleKeys = page.Where(r => r.Kind == GridRowKind.Data).Select(r => r.Key).ToList(),
            TotalCount = total,
            FilteredCount = filtered,
            Totals = totals,
            ViewRowCount = viewCount,
            PageIndex = pageIndex,
            PageCount = pageCount,
        };
    }

    private static GridViewRow<T> Reindex(GridViewRow<T> r, int index) => r.Index == index ? r : new GridViewRow<T>
    {
        Kind = r.Kind, Index = index, Depth = r.Depth, Item = r.Item, Key = r.Key, GroupId = r.GroupId, GroupField = r.GroupField,
        GroupKey = r.GroupKey, GroupKeyText = r.GroupKeyText, RowCount = r.RowCount, Aggregates = r.Aggregates, Expanded = r.Expanded,
        HasChildren = r.HasChildren,
    };
}
