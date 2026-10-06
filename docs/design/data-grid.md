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

## Engine API

C# lives in `src/Slate.Core/Data` (namespace `Slate.Data`); the TypeScript port in `packages/web/src/core/grid`
(exported as the `grid` namespace from `@slate/web`, plus `DataPipeline`, `GridRowCache`, `createGridState`, … at top
level). Contract enums (`GridColumnType`, `SortDirection`, `GridPin`, `GridAggregate`, `GridSelectionMode`,
`GridPagination`) are in the root `Slate` namespace; engine enums (`GridEditMode`, `FilterOperator`, `GridAlign`,
`GridRowKind`, `GridKey`, `GridNavAction`, `SelectAllState`) in `Slate.Data`. TS uses the camelCase string values.

| Piece | C# | TS | Renderer use |
|---|---|---|---|
| Column definition | `GridColumn<T>` | `GridColumn<T>` + `col.*` defaults, `cellValue`, `displayText`, `parseCell` | header, cell text, editors |
| Values | `GridValues` (Normalize, Compare, Format, Parse, ParseIsoDate) | `normalize`, `compareNormalized`, `formatValue`, `parseValue`, `parseIsoDate` | portable formats: numbers `0`, `0.00`, `#,##0.0`, `0%`; dates `yyyy MMM MM dd HH mm ss` (UTC) |
| View state | `GridState` record + transitions, `ToJson/FromJson` | `GridState` object + `toggleSort`, `setFilter`, … `gridStateToJson/FromJson` | every header/menu/toolbar action is a pure transition; persist the JSON |
| Pipeline | `DataPipeline<T>.Run(items, state)` → `GridPipelineResult<T>` | `new DataPipeline(cols, opts).run(items, state)` | `Rows` (data/group/detail, depth, aggregates, expanded), `Items`/`ItemKeys` (export, select-all), `VisibleKeys` (range order), `Totals` (footer), paging |
| Layout | `GridLayout.Resolve(cols, state, width)` → `GridColumnLayout<T>` | `resolveColumns` | widths (flex), order, pinned sections, `Left`/`StickyOffset`, `ScrollingRange` (column virtualisation), `EstimateWidth` (double-click auto-fit) |
| Virtualisation | `GridViewport.Compute / ScrollToReveal / PageRows` | `computeViewport`, `scrollToReveal`, `pageRows` | render only `[First, First+Count)` at `OffsetTop` |
| Selection | `SelectionModel<TKey>` | `SelectionModel<K>` | `Click(key, VisibleKeys, ctrl, shift)`, `Toggle`, `ExtendTo`, `SelectAll(ItemKeys)`, `SelectAllMatching(total)`, `HeaderState` |
| Keyboard | `GridNavigator.Move(cell, key, ctx)` | `moveCell`, `gridKeyFromEvent` | returns the new active cell + expand/collapse action |
| Editing | `EditSession<T>` | `EditSession<T>` | `Begin` → `SetDraftText` (parse + validate) → `Commit` (false while invalid); batch `Pending`, `CommitAll`, `DiscardAll`; `MoveAfterCommit` |
| Server data | `IGridDataSource<T>`, `GridQuery`, `GridResult<T>`, `InMemoryGridDataSource<T>`, `GridRowCache<T>` | `GridDataSource<T>`, `queryFromState`, `InMemoryGridDataSource`, `GridRowCache` | `SetQuery` on state change, `EnsureRange(first, count)` per viewport, `TryGet`/`get` per row, skeleton when missing |
| Export | `GridExport.ToCsv / ToTsv` | `toCsv`, `toTsv` | CSV download, Ctrl+C TSV (pass `valueOf` to include pending edits) |

Decisions worth knowing:
- **Text ordering** is case-insensitive ordinal with an ordinal tie-break (identical in .NET and JS; no culture
  collation). **Nulls sort last in both directions.** Ties keep source order.
- **Grouping** buckets by display text; group order follows the column's sort direction if that column is sorted,
  else ascending; the empty group is last. Group ids are `field=text` joined by `|` (escaped). Tree nodes use
  `row:{key}`; both share `GroupsCollapsedByDefault` + `ToggledGroups`. Tree data ignores `GroupBy`; filtering a tree
  keeps (and force-expands) ancestors of matches.
- **Quick filter**: whitespace-separated terms, each must appear in some visible searchable column's display text.
- **Filters**: `Contains/StartsWith/EndsWith/AnyOf` use display text; comparisons are typed for number/progress/
  date/boolean columns (filter values are coerced: ISO strings for dates) and text-based otherwise; a filter
  without a value is ignored; `Between` is inclusive in either order.
- **Paging** slices the flattened view rows (groups included). **Aggregates**: `count` = non-null values,
  `sum`/`avg` over numeric values, `min`/`max` by the column ordering (raw value).
- Parity is enforced by `tests/fixtures/data-grid.json` (C# writes it; `SLATE_UPDATE_FIXTURES=1 dotnet test` to
  regenerate) and `packages/web/test/grid-engine.test.ts`. Perf budgets: 100k rows sort+filter+quick filter
  < 150 ms, grouping < 200 ms (`SLATE_PERF_FACTOR` relaxes on slow CI).
