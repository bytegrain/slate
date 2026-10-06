namespace Slate.Data;

/// <summary>A cell being edited.</summary>
public sealed class GridCellEdit<T>
{
    internal GridCellEdit(T item, string rowKey, GridColumn<T> column, object? original)
    {
        Item = item;
        RowKey = rowKey;
        Column = column;
        Original = original;
        Draft = original;
    }

    public T Item { get; }
    public string RowKey { get; }
    public GridColumn<T> Column { get; }
    public object? Original { get; }
    public object? Draft { get; internal set; }

    /// <summary>Text currently in the editor (null when the draft was set as a value).</summary>
    public string? DraftText { get; internal set; }

    /// <summary>Validation or parse error for the draft; commit is blocked while set.</summary>
    public string? Error { get; internal set; }

    public bool IsValid => Error is null;
}

/// <summary>A committed (cell mode) or pending (batch mode) change.</summary>
public sealed record GridCellChange<T>(T Item, string RowKey, string Field, object? OldValue, object? NewValue);

/// <summary>Key that ended an edit, for moving the active cell afterwards.</summary>
public enum GridEditCommitKey { Enter, Tab }

/// <summary>
/// Inline editing state machine. Cell mode writes through <see cref="GridColumn{T}.Setter"/> on commit; batch mode
/// collects <see cref="Pending"/> changes (shown via <see cref="GetValue"/>) until <see cref="CommitAll"/> or
/// <see cref="DiscardAll"/>.
/// </summary>
public sealed class EditSession<T>
{
    private readonly List<GridCellChange<T>> _pending = [];

    public EditSession(GridEditMode mode = GridEditMode.Cell) => Mode = mode;

    public GridEditMode Mode { get; }

    public GridCellEdit<T>? Current { get; private set; }

    public bool IsEditing => Current is not null;

    /// <summary>Uncommitted changes (batch mode), one per cell.</summary>
    public IReadOnlyList<GridCellChange<T>> Pending => _pending;

    /// <summary>Raised for every value written through a setter.</summary>
    public event EventHandler<GridCellChange<T>>? Committed;

    public event EventHandler? Changed;

    /// <summary>Starts editing a cell. Returns false when editing is off, the column isn't editable or has no setter.</summary>
    public bool Begin(T item, string rowKey, GridColumn<T> column, string? initialText = null)
    {
        if (Mode == GridEditMode.None || !column.Editable || column.Setter is null) return false;
        if (IsEditing) Cancel();
        Current = new GridCellEdit<T>(item, rowKey, column, GetValue(item, rowKey, column));
        if (initialText is not null) SetDraftText(initialText);
        Raise();
        return true;
    }

    /// <summary>Updates the draft from editor text: parses with the column parser, then validates.</summary>
    public void SetDraftText(string text)
    {
        var edit = Current ?? throw new InvalidOperationException("No cell is being edited.");
        edit.DraftText = text;
        try
        {
            edit.Draft = edit.Column.Parse(text);
            edit.Error = edit.Column.Validate?.Invoke(edit.Draft);
        }
        catch (FormatException ex)
        {
            edit.Error = ex.Message;
        }
        Raise();
    }

    /// <summary>Updates the draft with a typed value (checkbox, select, date picker) and validates it.</summary>
    public void SetDraft(object? value)
    {
        var edit = Current ?? throw new InvalidOperationException("No cell is being edited.");
        edit.DraftText = null;
        edit.Draft = value;
        edit.Error = edit.Column.Validate?.Invoke(value);
        Raise();
    }

    /// <summary>Commits the draft. Returns false (and keeps editing) when it's invalid.</summary>
    public bool Commit()
    {
        var edit = Current;
        if (edit is null) return true;
        if (!edit.IsValid) return false;

        if (!Equals(edit.Draft, edit.Original))
        {
            var original = edit.Column.GetValue(edit.Item);
            var change = new GridCellChange<T>(edit.Item, edit.RowKey, edit.Column.Field, original, edit.Draft);
            if (Mode == GridEditMode.Batch)
            {
                _pending.RemoveAll(p => p.RowKey == edit.RowKey && p.Field == edit.Column.Field);
                if (!Equals(edit.Draft, original)) _pending.Add(change);
            }
            else
            {
                edit.Column.Setter!(edit.Item, edit.Draft);
                Committed?.Invoke(this, change);
            }
        }

        Current = null;
        Raise();
        return true;
    }

    public void Cancel()
    {
        if (Current is null) return;
        Current = null;
        Raise();
    }

    /// <summary>The value to display: the pending (batch) value if any, otherwise the item's value.</summary>
    public object? GetValue(T item, string rowKey, GridColumn<T> column)
    {
        for (var i = _pending.Count - 1; i >= 0; i--)
            if (_pending[i].RowKey == rowKey && _pending[i].Field == column.Field) return _pending[i].NewValue;
        return column.GetValue(item);
    }

    public bool HasPending(string rowKey, string field) => _pending.Any(p => p.RowKey == rowKey && p.Field == field);

    /// <summary>Batch mode: writes every pending change through its setter.</summary>
    public IReadOnlyList<GridCellChange<T>> CommitAll(IReadOnlyList<GridColumn<T>> columns)
    {
        if (IsEditing && !Commit()) return [];
        var byField = columns.ToDictionary(c => c.Field);
        var applied = _pending.ToList();
        foreach (var change in applied)
        {
            if (byField.TryGetValue(change.Field, out var col) && col.Setter is not null)
            {
                col.Setter(change.Item, change.NewValue);
                Committed?.Invoke(this, change);
            }
        }
        _pending.Clear();
        Raise();
        return applied;
    }

    public void DiscardAll()
    {
        _pending.Clear();
        Current = null;
        Raise();
    }

    /// <summary>Where the active cell goes after committing with Enter (down / Shift: up) or Tab (right / Shift: left).</summary>
    public static GridKey MoveAfterCommit(GridEditCommitKey key, bool shift) => key switch
    {
        GridEditCommitKey.Enter => shift ? GridKey.Up : GridKey.Down,
        _ => shift ? GridKey.Left : GridKey.Right,
    };

    private void Raise() => Changed?.Invoke(this, EventArgs.Empty);
}
