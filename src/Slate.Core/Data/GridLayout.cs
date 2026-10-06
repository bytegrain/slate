namespace Slate.Data;

/// <summary>A visible column after applying <see cref="GridState"/>: final order, width, pinning and offsets.</summary>
/// <param name="Column">The column definition.</param>
/// <param name="Width">Final width in px.</param>
/// <param name="Pin">Effective pinning.</param>
/// <param name="Index">Position among visible columns (the navigator's column index).</param>
/// <param name="Left">X offset from the start of the row.</param>
/// <param name="StickyOffset">For pinned columns: distance from the row's start (Start pins) or end (End pins) edge while scrolled.</param>
public sealed record ResolvedColumn<T>(GridColumn<T> Column, double Width, GridPin Pin, int Index, double Left, double StickyOffset)
{
    public string Field => Column.Field;
}

/// <summary>The resolved column layout of a grid.</summary>
public sealed record GridColumnLayout<T>(IReadOnlyList<ResolvedColumn<T>> Columns, IReadOnlyList<string> Order, double TotalWidth)
{
    /// <summary>Visible columns, start pins first, then scrolling columns, then end pins.</summary>
    public IReadOnlyList<ResolvedColumn<T>> Visible => Columns;

    public ResolvedColumn<T>? Find(string field) => Columns.FirstOrDefault(c => c.Field == field);

    /// <summary>
    /// Column virtualisation: indices (into <see cref="Columns"/>) of unpinned columns intersecting the horizontal
    /// viewport, plus <paramref name="overscanPx"/> each side. Pinned columns are always rendered.
    /// </summary>
    public (int First, int Last) ScrollingRange(double scrollLeft, double viewportWidth, double overscanPx = 200)
    {
        int first = -1, last = -1;
        for (var i = 0; i < Columns.Count; i++)
        {
            var c = Columns[i];
            if (c.Pin != GridPin.None) continue;
            var right = c.Left + c.Width;
            if (right >= scrollLeft - overscanPx && c.Left <= scrollLeft + viewportWidth + overscanPx)
            {
                if (first < 0) first = i;
                last = i;
            }
        }
        return (first, last);
    }
}

public static class GridLayout
{
    /// <summary>Width used when a column has neither <c>Width</c> nor <c>Flex</c>.</summary>
    public const double DefaultWidth = 150;

    /// <summary>
    /// Applies order, visibility, pinning and widths. Flexible columns share the space left in
    /// <paramref name="availableWidth"/> by weight, within their min/max (redistributing what a clamp frees).
    /// </summary>
    public static GridColumnLayout<T> Resolve<T>(IReadOnlyList<GridColumn<T>> columns, GridState state, double availableWidth = 0)
    {
        var byField = columns.ToDictionary(c => c.Field);
        var order = state.Columns.Select(c => c.Field).Where(byField.ContainsKey).Distinct().ToList();
        order.AddRange(columns.Select(c => c.Field).Where(f => !order.Contains(f)));

        var entries = new List<(GridColumn<T> Col, GridPin Pin, double? Width, double Flex)>();
        foreach (var field in order)
        {
            var col = byField[field];
            var cs = state.Column(field);
            if (cs?.Hidden ?? col.Hidden) continue;
            var width = cs?.Width ?? col.Width;
            var flex = width is null && col.Flex > 0 ? col.Flex : 0;
            if (width is null && flex == 0) width = DefaultWidth;
            if (width is { } w) width = Math.Clamp(w, col.MinWidth, col.MaxWidth);
            entries.Add((col, cs?.Pinned ?? col.Pinned, width, flex));
        }

        // Flex distribution.
        var widths = entries.Select(e => e.Width ?? e.Col.MinWidth).ToArray();
        var flexing = Enumerable.Range(0, entries.Count).Where(i => entries[i].Flex > 0).ToList();
        for (var pass = 0; pass < 8 && flexing.Count > 0; pass++)
        {
            var free = availableWidth - widths.Sum();
            if (free <= 0.0001) break;
            var weight = flexing.Sum(i => entries[i].Flex);
            var clamped = new List<int>();
            foreach (var i in flexing)
            {
                var target = widths[i] + free * entries[i].Flex / weight;
                if (target >= entries[i].Col.MaxWidth) { widths[i] = entries[i].Col.MaxWidth; clamped.Add(i); }
                else widths[i] = target;
            }
            if (clamped.Count == 0) break;
            flexing = flexing.Except(clamped).ToList();
        }

        // Section order: start pins, scrolling, end pins (stable within each).
        var ordered = Enumerable.Range(0, entries.Count)
            .OrderBy(i => entries[i].Pin switch { GridPin.Start => 0, GridPin.None => 1, _ => 2 })
            .ThenBy(i => i)
            .ToList();

        var result = new List<ResolvedColumn<T>>(ordered.Count);
        double left = 0, startSticky = 0;
        foreach (var i in ordered)
        {
            var w = Math.Round(widths[i], 2);
            var pin = entries[i].Pin;
            var sticky = pin == GridPin.Start ? startSticky : 0;
            if (pin == GridPin.Start) startSticky += w;
            result.Add(new ResolvedColumn<T>(entries[i].Col, w, pin, result.Count, left, sticky));
            left += w;
        }

        double endSticky = 0;
        for (var k = result.Count - 1; k >= 0 && result[k].Pin == GridPin.End; k--)
        {
            result[k] = result[k] with { StickyOffset = endSticky };
            endSticky += result[k].Width;
        }

        return new GridColumnLayout<T>(result, order, Math.Round(left, 2));
    }

    /// <summary>
    /// Estimates a column width (px) that fits its header and a sample of cell texts. Heuristic for
    /// Instrument Sans 13px ≈ 7.0px/char, IBM Plex Mono 13px ≈ 7.8px/char, header label 7.4px/char plus 24px for
    /// sort/filter affordances, plus 24px cell padding. Clamped to the column's min/max (and 600px).
    /// </summary>
    public static double EstimateWidth<T>(GridColumn<T> column, IEnumerable<string> sampleTexts, bool monospace = false)
    {
        var perChar = monospace || column.Type is GridColumnType.Number or GridColumnType.Date ? 7.8 : 7.0;
        var widest = 0;
        foreach (var t in sampleTexts.Take(200))
            widest = Math.Max(widest, t?.Length ?? 0);
        var header = column.DisplayTitle.Length * 7.4 + 24;
        var content = widest * perChar;
        var width = Math.Ceiling(Math.Max(header, content) + 24);
        return Math.Clamp(width, column.MinWidth, Math.Min(column.MaxWidth, 600));
    }
}

/// <summary>The rows to render for a vertical scroll position.</summary>
/// <param name="First">First row index to render.</param>
/// <param name="Count">Number of rows to render.</param>
/// <param name="OffsetTop">Y offset of the first rendered row.</param>
/// <param name="TotalHeight">Height of all rows (scroll extent).</param>
public readonly record struct ViewportRange(int First, int Count, double OffsetTop, double TotalHeight)
{
    public int Last => First + Count; // exclusive
}

/// <summary>Row virtualisation maths (fixed row height).</summary>
public static class GridViewport
{
    public static ViewportRange Compute(double scrollTop, double viewportHeight, double rowHeight, int rowCount, int overscan = 6)
    {
        if (rowHeight <= 0) throw new ArgumentOutOfRangeException(nameof(rowHeight));
        if (rowCount <= 0) return new ViewportRange(0, 0, 0, 0);
        var total = rowCount * rowHeight;
        scrollTop = Math.Clamp(scrollTop, 0, Math.Max(0, total - viewportHeight));
        var first = Math.Max(0, (int)Math.Floor(scrollTop / rowHeight) - overscan);
        var last = Math.Min(rowCount, (int)Math.Ceiling((scrollTop + Math.Max(0, viewportHeight)) / rowHeight) + overscan);
        return new ViewportRange(first, Math.Max(0, last - first), first * rowHeight, total);
    }

    /// <summary>Scroll position that brings <paramref name="rowIndex"/> fully into view (unchanged if already visible).</summary>
    public static double ScrollToReveal(int rowIndex, double scrollTop, double viewportHeight, double rowHeight)
    {
        var top = rowIndex * rowHeight;
        var bottom = top + rowHeight;
        if (top < scrollTop) return top;
        if (bottom > scrollTop + viewportHeight) return Math.Max(0, bottom - viewportHeight);
        return scrollTop;
    }

    /// <summary>Rows per PageUp/PageDown.</summary>
    public static int PageRows(double viewportHeight, double rowHeight) => Math.Max(1, (int)Math.Floor(viewportHeight / rowHeight));
}
