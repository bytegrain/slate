/** Column layout and row virtualisation maths — port of Slate.Core `GridLayout` / `GridViewport`. */
import { columnState, type GridState } from './state';
import { col, DEFAULT_COLUMN_WIDTH, roundTo, type GridColumn, type GridPin } from './values';

export interface ResolvedColumn<T> {
  readonly column: GridColumn<T>;
  readonly field: string;
  readonly width: number;
  readonly pin: GridPin;
  /** Position among visible columns (the navigator's column index). */
  readonly index: number;
  /** X offset from the start of the row. */
  readonly left: number;
  /** Pinned columns: distance from the row's start (start pins) or end (end pins) edge while scrolled. */
  readonly stickyOffset: number;
}

export interface GridColumnLayout<T> {
  /** Visible columns: start pins, scrolling columns, end pins. */
  readonly columns: readonly ResolvedColumn<T>[];
  /** Every column field in display order (hidden included). */
  readonly order: readonly string[];
  readonly totalWidth: number;
}

/** Applies order, visibility, pinning and widths; flexible columns share `availableWidth` by weight within min/max. */
export function resolveColumns<T>(columns: readonly GridColumn<T>[], state: GridState, availableWidth = 0): GridColumnLayout<T> {
  const byField = new Map(columns.map((c) => [c.field, c] as const));
  const order: string[] = [];
  for (const f of state.order) if (byField.has(f) && !order.includes(f)) order.push(f);
  for (const c of columns) if (!order.includes(c.field)) order.push(c.field);

  const entries: { c: GridColumn<T>; pin: GridPin; width: number | null; flex: number }[] = [];
  for (const field of order) {
    const c = byField.get(field)!;
    const cs = columnState(state, field);
    if (cs?.hidden ?? col.hidden(c)) continue;
    let width: number | null = cs?.width ?? c.width ?? null;
    const flex = width === null && col.flex(c) > 0 ? col.flex(c) : 0;
    if (width === null && flex === 0) width = DEFAULT_COLUMN_WIDTH;
    if (width !== null) width = Math.min(Math.max(width, col.minWidth(c)), col.maxWidth(c));
    entries.push({ c, pin: cs?.pinned ?? col.pinned(c), width, flex });
  }

  const widths = entries.map((e) => e.width ?? col.minWidth(e.c));
  let flexing = entries.map((_, i) => i).filter((i) => entries[i]!.flex > 0);
  for (let pass = 0; pass < 8 && flexing.length > 0; pass++) {
    const free = availableWidth - widths.reduce((a, b) => a + b, 0);
    if (free <= 0.0001) break;
    const weight = flexing.reduce((a, i) => a + entries[i]!.flex, 0);
    const clamped: number[] = [];
    for (const i of flexing) {
      const target = widths[i]! + (free * entries[i]!.flex) / weight;
      if (target >= col.maxWidth(entries[i]!.c)) { widths[i] = col.maxWidth(entries[i]!.c); clamped.push(i); }
      else widths[i] = target;
    }
    if (clamped.length === 0) break;
    flexing = flexing.filter((i) => !clamped.includes(i));
  }

  const rank = (p: GridPin) => (p === 'start' ? 0 : p === 'none' ? 1 : 2);
  const ordered = entries.map((_, i) => i).sort((a, b) => rank(entries[a]!.pin) - rank(entries[b]!.pin) || a - b);

  const result: ResolvedColumn<T>[] = [];
  let left = 0;
  let startSticky = 0;
  for (const i of ordered) {
    const w = roundTo(widths[i]!, 2);
    const pin = entries[i]!.pin;
    const sticky = pin === 'start' ? startSticky : 0;
    if (pin === 'start') startSticky += w;
    result.push({ column: entries[i]!.c, field: entries[i]!.c.field, width: w, pin, index: result.length, left, stickyOffset: sticky });
    left += w;
  }
  let endSticky = 0;
  for (let k = result.length - 1; k >= 0 && result[k]!.pin === 'end'; k--) {
    result[k] = { ...result[k]!, stickyOffset: endSticky };
    endSticky += result[k]!.width;
  }
  return { columns: result, order, totalWidth: roundTo(left, 2) };
}

/** Column virtualisation: [first, last] indices of unpinned columns intersecting the viewport (−1 when none). */
export function scrollingColumnRange<T>(layout: GridColumnLayout<T>, scrollLeft: number, viewportWidth: number, overscanPx = 200): [number, number] {
  let first = -1;
  let last = -1;
  layout.columns.forEach((c, i) => {
    if (c.pin !== 'none') return;
    if (c.left + c.width >= scrollLeft - overscanPx && c.left <= scrollLeft + viewportWidth + overscanPx) {
      if (first < 0) first = i;
      last = i;
    }
  });
  return [first, last];
}

/** Estimated fitting width — same heuristic as C# `GridLayout.EstimateWidth`. */
export function estimateColumnWidth<T>(column: GridColumn<T>, sampleTexts: Iterable<string>, monospace = false): number {
  const perChar = monospace || column.type === 'number' || column.type === 'date' ? 7.8 : 7.0;
  let widest = 0;
  let n = 0;
  for (const t of sampleTexts) {
    if (n++ >= 200) break;
    widest = Math.max(widest, t?.length ?? 0);
  }
  const header = col.title(column).length * 7.4 + 24;
  const width = Math.ceil(Math.max(header, widest * perChar) + 24);
  return Math.min(Math.max(width, col.minWidth(column)), Math.min(col.maxWidth(column), 600));
}

export interface ViewportRange {
  readonly first: number;
  readonly count: number;
  readonly offsetTop: number;
  readonly totalHeight: number;
}

export function computeViewport(scrollTop: number, viewportHeight: number, rowHeight: number, rowCount: number, overscan = 6): ViewportRange {
  if (rowHeight <= 0) throw new RangeError('rowHeight must be positive.');
  if (rowCount <= 0) return { first: 0, count: 0, offsetTop: 0, totalHeight: 0 };
  const total = rowCount * rowHeight;
  const top = Math.min(Math.max(scrollTop, 0), Math.max(0, total - viewportHeight));
  const first = Math.max(0, Math.floor(top / rowHeight) - overscan);
  const last = Math.min(rowCount, Math.ceil((top + Math.max(0, viewportHeight)) / rowHeight) + overscan);
  return { first, count: Math.max(0, last - first), offsetTop: first * rowHeight, totalHeight: total };
}

export function scrollToReveal(rowIndex: number, scrollTop: number, viewportHeight: number, rowHeight: number): number {
  const top = rowIndex * rowHeight;
  const bottom = top + rowHeight;
  if (top < scrollTop) return top;
  if (bottom > scrollTop + viewportHeight) return Math.max(0, bottom - viewportHeight);
  return scrollTop;
}

export const pageRows = (viewportHeight: number, rowHeight: number): number => Math.max(1, Math.floor(viewportHeight / rowHeight));
