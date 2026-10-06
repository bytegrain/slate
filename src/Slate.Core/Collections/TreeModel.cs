namespace Slate.Collections;

public enum TreeKey
{
    Up,
    Down,
    Home,
    End,
    Right,
    Left,
    /// <summary>"*" — expand every sibling of the focused item.</summary>
    ExpandSiblings,
}

public enum CheckState
{
    Unchecked,
    Checked,
    Indeterminate,
}

public enum TreeLoadState
{
    /// <summary>Children are known (or the node is a leaf).</summary>
    Loaded,
    /// <summary>Has children that haven't been requested yet.</summary>
    NotLoaded,
    Loading,
    Failed,
}

/// <summary>A visible row of a flattened tree.</summary>
public sealed record TreeRow(string Id, string? ParentId, int Depth, bool HasChildren, bool Expanded, int PositionInSet, int SetSize);

public sealed record TreeNavigation(string? FocusId, IReadOnlySet<string> Expanded);

/// <summary>
/// The tree behind Slate's TreeView (and the data grid's tree rows): flattening by expansion, filtering that keeps
/// ancestors, WAI-ARIA tree keyboard behaviour, tri-state checkbox propagation and lazy-load bookkeeping.
/// Nodes are addressed by string id.
/// </summary>
public sealed class TreeModel<T>
{
    private readonly Func<T, string> _id;
    private readonly Func<T, IEnumerable<T>?> _children;
    private readonly Func<T, bool>? _hasChildren;
    private readonly Dictionary<string, T> _nodes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string?> _parent = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<string>> _childIds = new(StringComparer.Ordinal);
    private readonly List<string> _roots = [];
    private readonly Dictionary<string, TreeLoadState> _load = new(StringComparer.Ordinal);

    /// <summary>Builds the model. For lazy trees, <c>hasChildren</c> says whether a node has children that may not be loaded yet.</summary>
    public TreeModel(IEnumerable<T> roots, Func<T, string> id, Func<T, IEnumerable<T>?> children, Func<T, bool>? hasChildren = null)
    {
        _id = id ?? throw new ArgumentNullException(nameof(id));
        _children = children ?? throw new ArgumentNullException(nameof(children));
        _hasChildren = hasChildren;
        foreach (var root in roots ?? throw new ArgumentNullException(nameof(roots)))
            _roots.Add(Index(root, null));
    }

    public IReadOnlyList<string> Roots => _roots;

    public T Node(string id) => _nodes[id];

    public string? ParentOf(string id) => _parent[id];

    public IReadOnlyList<string> ChildrenOf(string id) => _childIds.TryGetValue(id, out var c) ? c : [];

    public bool HasChildren(string id) => ChildrenOf(id).Count > 0 || LoadState(id) is TreeLoadState.NotLoaded or TreeLoadState.Loading or TreeLoadState.Failed;

    public TreeLoadState LoadState(string id) => _load.TryGetValue(id, out var s) ? s : TreeLoadState.Loaded;

    /// <summary>Ancestors of a node, nearest first.</summary>
    public IEnumerable<string> Ancestors(string id)
    {
        for (var p = _parent[id]; p is not null; p = _parent[p])
            yield return p;
    }

    /// <summary>Visible rows in display order. <paramref name="visible"/> limits rows (e.g. to a filter result).</summary>
    public IReadOnlyList<TreeRow> Flatten(IReadOnlySet<string> expanded, IReadOnlySet<string>? visible = null)
    {
        var rows = new List<TreeRow>();
        void Walk(IReadOnlyList<string> ids, string? parent, int depth)
        {
            var shown = visible is null ? ids : ids.Where(visible.Contains).ToList();
            for (var i = 0; i < shown.Count; i++)
            {
                var id = shown[i];
                var has = HasChildren(id);
                var open = has && expanded.Contains(id);
                rows.Add(new TreeRow(id, parent, depth, has, open, i + 1, shown.Count));
                if (open) Walk(ChildrenOf(id), id, depth + 1);
            }
        }
        Walk(_roots, null, 0);
        return rows;
    }

    /// <summary>
    /// Ids matching <paramref name="isMatch"/> plus all their ancestors, and the ancestors that must be expanded
    /// to reveal the matches.
    /// </summary>
    public (IReadOnlySet<string> Visible, IReadOnlySet<string> Expand) Filter(Func<T, bool> isMatch)
    {
        ArgumentNullException.ThrowIfNull(isMatch);
        var visible = new HashSet<string>(StringComparer.Ordinal);
        var expand = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (id, node) in _nodes)
        {
            if (!isMatch(node)) continue;
            visible.Add(id);
            foreach (var a in Ancestors(id))
            {
                visible.Add(a);
                expand.Add(a);
            }
        }
        return (visible, expand);
    }

    /// <summary>Applies a key to the focused item, returning the new focus and expansion set.</summary>
    public TreeNavigation Navigate(string? focus, TreeKey key, IReadOnlySet<string> expanded, IReadOnlySet<string>? visible = null)
    {
        var rows = Flatten(expanded, visible);
        var set = new HashSet<string>(expanded, StringComparer.Ordinal);
        if (rows.Count == 0) return new(null, set);
        var index = focus is null ? -1 : IndexOf(rows, focus);
        if (index < 0) return new(rows[0].Id, set);
        var row = rows[index];

        switch (key)
        {
            case TreeKey.Up:
                return new(rows[Math.Max(0, index - 1)].Id, set);
            case TreeKey.Down:
                return new(rows[Math.Min(rows.Count - 1, index + 1)].Id, set);
            case TreeKey.Home:
                return new(rows[0].Id, set);
            case TreeKey.End:
                return new(rows[^1].Id, set);
            case TreeKey.Right:
                if (!row.HasChildren) return new(row.Id, set);
                if (!row.Expanded)
                {
                    set.Add(row.Id);
                    return new(row.Id, set);
                }
                return new(index + 1 < rows.Count && rows[index + 1].ParentId == row.Id ? rows[index + 1].Id : row.Id, set);
            case TreeKey.Left:
                if (row.Expanded)
                {
                    set.Remove(row.Id);
                    return new(row.Id, set);
                }
                return new(row.ParentId ?? row.Id, set);
            case TreeKey.ExpandSiblings:
                var siblings = row.ParentId is null ? _roots : ChildrenOf(row.ParentId);
                foreach (var s in siblings)
                    if (HasChildren(s) && (visible is null || visible.Contains(s)))
                        set.Add(s);
                return new(row.Id, set);
            default:
                throw new ArgumentOutOfRangeException(nameof(key));
        }
    }

    /// <summary>Check state of a node given the set of fully checked ids (leaves and complete branches).</summary>
    public CheckState GetCheckState(string id, IReadOnlySet<string> checkedIds)
    {
        var children = ChildrenOf(id);
        if (children.Count == 0) return checkedIds.Contains(id) ? CheckState.Checked : CheckState.Unchecked;
        var states = children.Select(c => GetCheckState(c, checkedIds)).ToList();
        if (states.All(s => s == CheckState.Checked)) return CheckState.Checked;
        if (states.All(s => s == CheckState.Unchecked)) return CheckState.Unchecked;
        return CheckState.Indeterminate;
    }

    /// <summary>
    /// Toggles a node: a checked node unchecks its whole subtree, otherwise the whole subtree is checked. Ancestors
    /// become checked exactly when all their children are. Returns the new checked set.
    /// </summary>
    public IReadOnlySet<string> ToggleCheck(string id, IReadOnlySet<string> checkedIds)
    {
        var result = new HashSet<string>(checkedIds, StringComparer.Ordinal);
        var check = GetCheckState(id, checkedIds) != CheckState.Checked;
        foreach (var n in Subtree(id))
        {
            if (check) result.Add(n);
            else result.Remove(n);
        }
        foreach (var a in Ancestors(id))
        {
            if (ChildrenOf(a).All(c => GetCheckState(c, result) == CheckState.Checked)) result.Add(a);
            else result.Remove(a);
        }
        return result;
    }

    /// <summary>Lazy loading: call when a node is expanded. True means the caller should start loading its children.</summary>
    public bool BeginLoad(string id)
    {
        if (LoadState(id) is not (TreeLoadState.NotLoaded or TreeLoadState.Failed)) return false;
        _load[id] = TreeLoadState.Loading;
        return true;
    }

    public void CompleteLoad(string id, IEnumerable<T> children)
    {
        var list = _childIds[id] = [];
        foreach (var c in children) list.Add(Index(c, id));
        _load[id] = TreeLoadState.Loaded;
    }

    public void FailLoad(string id) => _load[id] = TreeLoadState.Failed;

    private IEnumerable<string> Subtree(string id)
    {
        yield return id;
        foreach (var c in ChildrenOf(id))
            foreach (var d in Subtree(c))
                yield return d;
    }

    private static int IndexOf(IReadOnlyList<TreeRow> rows, string id)
    {
        for (var i = 0; i < rows.Count; i++)
            if (rows[i].Id == id) return i;
        return -1;
    }

    private string Index(T node, string? parent)
    {
        var id = _id(node);
        if (!_nodes.TryAdd(id, node))
            throw new ArgumentException($"Duplicate tree node id '{id}'.");
        _parent[id] = parent;
        var children = _children(node)?.ToList();
        if (children is { Count: > 0 })
        {
            _childIds[id] = children.Select(c => Index(c, id)).ToList();
        }
        else if (_hasChildren?.Invoke(node) == true)
        {
            _load[id] = TreeLoadState.NotLoaded;
        }
        return id;
    }
}
