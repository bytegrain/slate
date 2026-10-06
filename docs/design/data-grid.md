# Data grid

Slate's flagship data component: built for 100k+ row datasets in professional tools, keyboard-first, and
identical across Web, Blazor, WPF and Avalonia. Name: **DataGrid** (`sl-data-grid`, `SlDataGrid<T>`, `DataGrid`).

## Architecture

```
Slate.Core.Data (C#)  ⇄  @slate/web src/core/grid (TS port, fixture-identical)
  GridColumn / GridState / DataPipeline / SelectionModel / GridNavigator / EditSession / IGridDataSource / export
        │  (pure, platform-free, unit tested incl. performance budgets)
        ▼
  Renderers: sl-data-grid · SlDataGrid<T> · Slate.Avalonia DataGrid · Slate.Wpf DataGrid
  (draw the view window, forward pointer/keyboard input to the engine — no grid logic of their own)
```

`tests/fixtures/data-grid.json` holds pipeline inputs and expected outputs produced by Slate.Core; the TS port must
match it exactly (ordering, group keys, aggregates, selection ranges, navigation results).

## Features

### Data & performance
- **Row virtualization**: only rows in the viewport (+ overscan) exist. Fixed row height from density
  (`component.grid.rowHeight` 32px compact / 40px comfortable). Scrolls 100k rows at 60fps.
- **Column virtualization** for wide grids (> 40 columns).
- **Client mode**: the engine sorts/filters/groups an in-memory list. Budget: 100k rows, sort + filter
  under 150 ms on a dev machine (enforced by a test with a generous CI margin).
- **Server mode** (`IGridDataSource`): the grid sends a `GridQuery` (sort, filters, quick filter, group,
  range/page) and renders the `GridResult` (rows, total count, optional group summaries). Loading shows
  skeleton rows; scrolling fetches pages on demand (infinite scroll) with request cancellation.
- **Paging** as an alternative to infinite scroll (page size, page index, total).
- **Live updates**: rows replaced by key flash `selection.background` briefly (reduced motion: no flash).

### Columns
- Types: `text`, `number` (tabular, right aligned, format string), `date`, `boolean` (check icon), `enum`
  (badge with tone per value), `progress` (bar), `sparkline` (mini bars), `actions` (row menu), `custom`
  (template / cell renderer).
- **Sort**: click header (asc → desc → none); Shift+click adds secondary sorts (badge shows order).
  Stable, culture-aware for text, null-last.
- **Filter**: per-column menu with type-aware operators (text: contains / equals / starts with / ends with
  / is empty; number & date: = ≠ < ≤ > ≥ between / is empty; boolean; enum: any of), plus a **quick filter**
  across all searchable columns. Active filters show as removable **filter chips** above the grid.
- **Resize** (drag the header edge, double-click to auto-fit content), **reorder** (drag header, keyboard
  Alt+←/→), **pin** left/right (sticky with a shadow edge when scrolled), **hide/show** via the column chooser.
- **Width modes**: fixed px, `flex` weight, min/max; auto-size from content sample.
- **Header groups** (multi-row headers spanning columns).

### Rows
- **Selection**: none / single / multi. Checkbox column with select-all (tri-state, "select all N matching"
  in server mode); click, Ctrl/Cmd+click toggle, Shift+click range, Shift+arrows extend. Selection survives
  sorting/filtering (tracked by row key).
- **Grouping** by one or more columns (drag to group bar or column menu): group rows with expand/collapse,
  counts, and **aggregates** (sum, avg, min, max, count) shown in group rows and an optional footer row.
- **Tree data**: hierarchical rows (`ChildrenSelector`), indented with expanders, filtering keeps ancestors.
- **Row detail**: expandable detail template under a row.
- **Row reorder** by drag (client mode, no active sort).
- **Conditional styling**: `RowTone(row)` / `CellTone(row, column)` → Tone tint.

### Editing
- Inline cell editing: Enter/F2/double-click or typing begins; Enter commits and moves down, Tab moves right,
  Escape cancels. Editors per type (text field, number, date, checkbox, select for enums).
- Validation per column (`Validate(value) → error`): invalid cells show the danger border + message; commit
  is blocked until fixed. `CellEditCommitted` event with old/new value; optional batch mode with
  "N unsaved changes — Commit / Discard" bar (as in the Alloy database-explorer design).

### Keyboard (WAI-ARIA grid pattern)
Arrows move the active cell; Home/End row start/end; Ctrl+Home/End grid start/end; PageUp/PageDown by
viewport; Space toggles row selection; Ctrl+A selects all; Enter edits / activates; Escape cancels;
Ctrl+C copies the selection as TSV (with headers on Ctrl+Shift+C); `+`/`-` expand/collapse groups and tree
nodes. Single tab stop (roving), `aria-rowcount`/`aria-rowindex` reflect the full dataset.

### Export & state
- Export visible/selected/all rows to CSV (RFC 4180) and TSV; respects column formatting and hidden columns.
- **State** (column order/width/visibility/pinning, sort, filters, grouping, page size) serialises to JSON
  for persistence and restores exactly (`GridState.ToJson()` / `FromJson`).

### Toolbar (optional, composable)
Quick filter field, filter chips, density toggle, column chooser, export menu, and a selection bar that
replaces the toolbar when rows are selected ("3 selected · Archive · Delete").

## Visual

Header: `background.subtle`, `typography.label` in `text.secondary`, 1px bottom `border.default`, sort and
filter icons on hover/active. Rows: hairline `border.default` separators, hover `background.hover`,
selected `selection.background` with a 2px `selection.indicator` inset bar on the first cell, active cell
2px `focus.ring` outline (keyboard only). Numbers in `font.family.mono` tabular. Pinned columns cast
`shadow.e1` on their scrolling edge. Group rows `background.subtle`, `bodyStrong`. Footer aggregates in mono.
All of it via `component.grid.*` tokens.
