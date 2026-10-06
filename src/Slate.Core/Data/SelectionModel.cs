namespace Slate.Data;

/// <summary>Tri-state of a select-all checkbox.</summary>
public enum SelectAllState { None, Some, All }

/// <summary>
/// Row selection keyed by row key, so it survives sorting, filtering and paging. Ranges (Shift) are taken over the
/// view order the caller passes in (data rows only, as displayed). Supports "select all matching" for server data,
/// where only exclusions are tracked.
/// </summary>
public sealed class SelectionModel<TKey> where TKey : notnull
{
    private readonly HashSet<TKey> _selected;
    private readonly HashSet<TKey> _excluded;

    public SelectionModel(GridSelectionMode mode = GridSelectionMode.Multi, IEqualityComparer<TKey>? comparer = null)
    {
        Mode = mode;
        _selected = new HashSet<TKey>(comparer);
        _excluded = new HashSet<TKey>(comparer);
    }

    public GridSelectionMode Mode { get; }

    /// <summary>The range anchor (last plainly clicked/toggled row); see <see cref="HasAnchor"/>.</summary>
    public TKey? Anchor => _hasAnchor ? _anchor : default;

    public bool HasAnchor => _hasAnchor;

    private TKey _anchor = default!;
    private bool _hasAnchor;

    private void SetAnchor(TKey key)
    {
        _anchor = key;
        _hasAnchor = true;
    }

    /// <summary>True after <see cref="SelectAllMatching"/>: every matching row is selected except <see cref="Excluded"/>.</summary>
    public bool AllMatching { get; private set; }

    /// <summary>Total matching rows used for <see cref="Count"/> in all-matching mode.</summary>
    public int MatchingCount { get; private set; }

    public IReadOnlyCollection<TKey> Selected => _selected;
    public IReadOnlyCollection<TKey> Excluded => _excluded;

    public event EventHandler? Changed;

    public int Count => AllMatching ? Math.Max(0, MatchingCount - _excluded.Count) : _selected.Count;

    public bool IsSelected(TKey key) => AllMatching ? !_excluded.Contains(key) : _selected.Contains(key);

    /// <summary>Selected keys in the given order (e.g. the view order).</summary>
    public IReadOnlyList<TKey> SelectedIn(IEnumerable<TKey> order) => order.Where(IsSelected).ToList();

    /// <summary>
    /// A pointer click on a row. Plain: select only it. Ctrl/Cmd: toggle it. Shift: select the range from the anchor
    /// (replacing the selection; with Ctrl, adding to it). Single mode ignores modifiers.
    /// </summary>
    public void Click(TKey key, IReadOnlyList<TKey> viewOrder, bool ctrl = false, bool shift = false)
    {
        switch (Mode)
        {
            case GridSelectionMode.None:
                return;
            case GridSelectionMode.Single:
                ResetAll();
                _selected.Add(key);
                SetAnchor(key);
                break;
            default:
                if (shift && _hasAnchor && IndexOf(viewOrder, _anchor) >= 0 && IndexOf(viewOrder, key) >= 0)
                {
                    var range = Range(viewOrder, _anchor, key);
                    if (!ctrl) ResetAll(keepAnchor: true);
                    foreach (var k in range) Include(k);
                }
                else if (ctrl)
                {
                    Flip(key);
                    SetAnchor(key);
                }
                else
                {
                    ResetAll();
                    _selected.Add(key);
                    SetAnchor(key);
                }
                break;
        }
        Raise();
    }

    /// <summary>Checkbox toggle: flips one row without touching the others.</summary>
    public void Toggle(TKey key)
    {
        if (Mode == GridSelectionMode.None) return;
        if (Mode == GridSelectionMode.Single)
        {
            var was = IsSelected(key);
            ResetAll();
            if (!was) _selected.Add(key);
        }
        else
        {
            Flip(key);
        }
        SetAnchor(key);
        Raise();
    }

    /// <summary>Keyboard range extension (Shift+arrows): selects anchor → key, replacing the selection.</summary>
    public void ExtendTo(TKey key, IReadOnlyList<TKey> viewOrder)
    {
        if (Mode != GridSelectionMode.Multi) { Click(key, viewOrder); return; }
        if (!_hasAnchor) SetAnchor(key);
        Click(key, viewOrder, ctrl: false, shift: true);
    }

    /// <summary>Selects the given keys (e.g. all filtered rows).</summary>
    public void SelectAll(IEnumerable<TKey> keys)
    {
        if (Mode != GridSelectionMode.Multi) return;
        ResetAll(keepAnchor: true);
        foreach (var k in keys) _selected.Add(k);
        Raise();
    }

    /// <summary>Server mode: everything matching the current query is selected; later toggles become exclusions.</summary>
    public void SelectAllMatching(int matchingCount)
    {
        if (Mode != GridSelectionMode.Multi) return;
        ResetAll(keepAnchor: true);
        AllMatching = true;
        MatchingCount = Math.Max(0, matchingCount);
        Raise();
    }

    public void Clear()
    {
        if (_selected.Count == 0 && _excluded.Count == 0 && !AllMatching && !_hasAnchor) return;
        ResetAll();
        Raise();
    }

    /// <summary>Replaces the selection (e.g. from a two-way binding).</summary>
    public void Set(IEnumerable<TKey> keys)
    {
        ResetAll(keepAnchor: true);
        foreach (var k in keys)
        {
            _selected.Add(k);
            if (Mode == GridSelectionMode.Single) break;
        }
        Raise();
    }

    /// <summary>State of a select-all checkbox for the given (filtered) keys.</summary>
    public SelectAllState HeaderState(IReadOnlyCollection<TKey> keys)
    {
        if (keys.Count == 0) return SelectAllState.None;
        var n = keys.Count(IsSelected);
        return n == 0 ? SelectAllState.None : n == keys.Count ? SelectAllState.All : SelectAllState.Some;
    }

    private void Include(TKey k)
    {
        if (AllMatching) _excluded.Remove(k);
        else _selected.Add(k);
    }

    private void Flip(TKey key)
    {
        if (AllMatching)
        {
            if (!_excluded.Remove(key)) _excluded.Add(key);
        }
        else if (!_selected.Remove(key))
        {
            _selected.Add(key);
        }
    }

    private void ResetAll(bool keepAnchor = false)
    {
        _selected.Clear();
        _excluded.Clear();
        AllMatching = false;
        MatchingCount = 0;
        if (!keepAnchor) _hasAnchor = false;
    }

    private static int IndexOf(IReadOnlyList<TKey> order, TKey key)
    {
        var cmp = EqualityComparer<TKey>.Default;
        for (var i = 0; i < order.Count; i++)
            if (cmp.Equals(order[i], key)) return i;
        return -1;
    }

    private static IEnumerable<TKey> Range(IReadOnlyList<TKey> order, TKey from, TKey to)
    {
        int a = IndexOf(order, from), b = IndexOf(order, to);
        if (a > b) (a, b) = (b, a);
        for (var i = a; i <= b; i++) yield return order[i];
    }

    private void Raise() => Changed?.Invoke(this, EventArgs.Empty);
}

/// <summary>Keys the grid navigator understands.</summary>
public enum GridKey { Up, Down, Left, Right, Home, End, CtrlHome, CtrlEnd, PageUp, PageDown, Plus, Minus }

/// <summary>What the renderer should do besides moving the active cell.</summary>
public enum GridNavAction { None, Expand, Collapse }

/// <summary>Active cell position: row index in the view and column index among visible columns.</summary>
public readonly record struct GridCell(int Row, int Column);

public readonly record struct GridNavResult(GridCell Cell, GridNavAction Action);

/// <summary>What the navigator needs to know about the current view.</summary>
public sealed class GridNavContext
{
    public required int RowCount { get; init; }
    public required int ColumnCount { get; init; }

    /// <summary>Rows per PageUp/PageDown (<see cref="GridViewport.PageRows"/>).</summary>
    public int PageRows { get; init; } = 10;

    /// <summary>Row kind by view index (group and detail rows are single full-width cells).</summary>
    public Func<int, GridRowKind> Kind { get; init; } = _ => GridRowKind.Data;

    /// <summary>Whether a row can expand/collapse (group rows; tree rows with children).</summary>
    public Func<int, bool> CanExpand { get; init; } = _ => false;

    public Func<int, bool> IsExpanded { get; init; } = _ => false;
}

/// <summary>
/// WAI-ARIA grid keyboard model. Group and detail rows are one cell wide (the column is remembered for when the
/// user moves back onto data rows). On group rows and on tree rows' first column, Left/Right (and -/+) collapse
/// and expand.
/// </summary>
public static class GridNavigator
{
    public static GridNavResult Move(GridCell current, GridKey key, GridNavContext ctx)
    {
        if (ctx.RowCount == 0 || ctx.ColumnCount == 0) return new(new GridCell(0, 0), GridNavAction.None);
        var row = Math.Clamp(current.Row, 0, ctx.RowCount - 1);
        var col = Math.Clamp(current.Column, 0, ctx.ColumnCount - 1);
        var lastRow = ctx.RowCount - 1;
        var lastCol = ctx.ColumnCount - 1;
        var single = ctx.Kind(row) != GridRowKind.Data;
        var expandable = ctx.CanExpand(row) && (single || col == 0);

        switch (key)
        {
            case GridKey.Up: return Stay(Math.Max(0, row - 1), col);
            case GridKey.Down: return Stay(Math.Min(lastRow, row + 1), col);
            case GridKey.PageUp: return Stay(Math.Max(0, row - ctx.PageRows), col);
            case GridKey.PageDown: return Stay(Math.Min(lastRow, row + ctx.PageRows), col);
            case GridKey.CtrlHome: return Stay(0, 0);
            case GridKey.CtrlEnd: return Stay(lastRow, lastCol);
            case GridKey.Home: return Stay(row, 0);
            case GridKey.End: return Stay(row, single ? col : lastCol);
            case GridKey.Left:
                if (expandable && ctx.IsExpanded(row)) return new(new GridCell(row, col), GridNavAction.Collapse);
                return Stay(row, single ? col : Math.Max(0, col - 1));
            case GridKey.Right:
                if (expandable && !ctx.IsExpanded(row)) return new(new GridCell(row, col), GridNavAction.Expand);
                return Stay(row, single ? col : Math.Min(lastCol, col + 1));
            case GridKey.Plus:
                return new(new GridCell(row, col), ctx.CanExpand(row) && !ctx.IsExpanded(row) ? GridNavAction.Expand : GridNavAction.None);
            case GridKey.Minus:
                return new(new GridCell(row, col), ctx.CanExpand(row) && ctx.IsExpanded(row) ? GridNavAction.Collapse : GridNavAction.None);
            default:
                return Stay(row, col);
        }

        static GridNavResult Stay(int r, int c) => new(new GridCell(r, c), GridNavAction.None);
    }
}
