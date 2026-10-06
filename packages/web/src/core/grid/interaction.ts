/** Selection, keyboard navigation and inline editing — ports of Slate.Core `SelectionModel`, `GridNavigator`, `EditSession`. */
import type { GridRowKind } from './pipeline';
import { cellValue, GridFormatError, parseCell, type GridColumn, type GridEditMode, type GridSelectionMode } from './values';

// ---- selection ---------------------------------------------------------------------------------------------

export type SelectAllState = 'none' | 'some' | 'all';

/** Row selection keyed by row key (survives sort/filter/paging). Ranges use the view order passed in. */
export class SelectionModel<K = string> {
  readonly mode: GridSelectionMode;
  private readonly selected = new Set<K>();
  private readonly excluded = new Set<K>();
  private anchorKey: K | undefined;
  private anchorSet = false;
  private listeners = new Set<() => void>();
  allMatching = false;
  matchingCount = 0;

  constructor(mode: GridSelectionMode = 'multi') {
    this.mode = mode;
  }

  get anchor(): K | undefined { return this.anchorSet ? this.anchorKey : undefined; }
  get hasAnchor(): boolean { return this.anchorSet; }
  get selectedKeys(): ReadonlySet<K> { return this.selected; }
  get excludedKeys(): ReadonlySet<K> { return this.excluded; }
  get count(): number { return this.allMatching ? Math.max(0, this.matchingCount - this.excluded.size) : this.selected.size; }

  onChange(listener: () => void): () => void {
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  }

  isSelected(key: K): boolean {
    return this.allMatching ? !this.excluded.has(key) : this.selected.has(key);
  }

  selectedIn(order: Iterable<K>): K[] {
    return [...order].filter((k) => this.isSelected(k));
  }

  /** Pointer click. Plain: only it. Ctrl/Cmd: toggle. Shift: range from the anchor (with Ctrl: added). */
  click(key: K, viewOrder: readonly K[], ctrl = false, shift = false): void {
    if (this.mode === 'none') return;
    if (this.mode === 'single') {
      this.reset();
      this.selected.add(key);
      this.setAnchor(key);
    } else if (shift && this.anchorSet && viewOrder.includes(this.anchorKey as K) && viewOrder.includes(key)) {
      const range = rangeOf(viewOrder, this.anchorKey as K, key);
      if (!ctrl) this.reset(true);
      for (const k of range) this.include(k);
    } else if (ctrl) {
      this.flip(key);
      this.setAnchor(key);
    } else {
      this.reset();
      this.selected.add(key);
      this.setAnchor(key);
    }
    this.raise();
  }

  /** Checkbox toggle. */
  toggle(key: K): void {
    if (this.mode === 'none') return;
    if (this.mode === 'single') {
      const was = this.isSelected(key);
      this.reset();
      if (!was) this.selected.add(key);
    } else {
      this.flip(key);
    }
    this.setAnchor(key);
    this.raise();
  }

  /** Shift+arrow extension. */
  extendTo(key: K, viewOrder: readonly K[]): void {
    if (this.mode !== 'multi') { this.click(key, viewOrder); return; }
    if (!this.anchorSet) this.setAnchor(key);
    this.click(key, viewOrder, false, true);
  }

  selectAll(keys: Iterable<K>): void {
    if (this.mode !== 'multi') return;
    this.reset(true);
    for (const k of keys) this.selected.add(k);
    this.raise();
  }

  /** Server mode: everything matching is selected; later toggles become exclusions. */
  selectAllMatching(matchingCount: number): void {
    if (this.mode !== 'multi') return;
    this.reset(true);
    this.allMatching = true;
    this.matchingCount = Math.max(0, matchingCount);
    this.raise();
  }

  clear(): void {
    if (this.selected.size === 0 && this.excluded.size === 0 && !this.allMatching && !this.anchorSet) return;
    this.reset();
    this.raise();
  }

  set(keys: Iterable<K>): void {
    this.reset(true);
    for (const k of keys) {
      this.selected.add(k);
      if (this.mode === 'single') break;
    }
    this.raise();
  }

  headerState(keys: readonly K[]): SelectAllState {
    if (keys.length === 0) return 'none';
    const n = keys.filter((k) => this.isSelected(k)).length;
    return n === 0 ? 'none' : n === keys.length ? 'all' : 'some';
  }

  private include(k: K) {
    if (this.allMatching) this.excluded.delete(k);
    else this.selected.add(k);
  }

  private flip(k: K) {
    const set = this.allMatching ? this.excluded : this.selected;
    if (set.has(k)) set.delete(k);
    else set.add(k);
  }

  private setAnchor(k: K) {
    this.anchorKey = k;
    this.anchorSet = true;
  }

  private reset(keepAnchor = false) {
    this.selected.clear();
    this.excluded.clear();
    this.allMatching = false;
    this.matchingCount = 0;
    if (!keepAnchor) {
      this.anchorSet = false;
      this.anchorKey = undefined;
    }
  }

  private raise() {
    for (const l of this.listeners) l();
  }
}

function rangeOf<K>(order: readonly K[], from: K, to: K): K[] {
  let a = order.indexOf(from), b = order.indexOf(to);
  if (a > b) [a, b] = [b, a];
  return order.slice(a, b + 1);
}

// ---- navigation --------------------------------------------------------------------------------------------

export type GridKey = 'up' | 'down' | 'left' | 'right' | 'home' | 'end' | 'ctrlHome' | 'ctrlEnd' | 'pageUp' | 'pageDown' | 'plus' | 'minus';
export type GridNavAction = 'none' | 'expand' | 'collapse';

export interface GridCell {
  readonly row: number;
  readonly column: number;
}

export interface GridNavResult {
  readonly cell: GridCell;
  readonly action: GridNavAction;
}

export interface GridNavContext {
  rowCount: number;
  columnCount: number;
  pageRows?: number;
  kind?: (row: number) => GridRowKind;
  canExpand?: (row: number) => boolean;
  isExpanded?: (row: number) => boolean;
}

/** Maps a KeyboardEvent to a navigator key (null when the key isn't a navigation key). */
export function gridKeyFromEvent(e: Pick<KeyboardEvent, 'key' | 'ctrlKey' | 'metaKey'>): GridKey | null {
  const mod = e.ctrlKey || e.metaKey;
  switch (e.key) {
    case 'ArrowUp': return 'up';
    case 'ArrowDown': return 'down';
    case 'ArrowLeft': return 'left';
    case 'ArrowRight': return 'right';
    case 'Home': return mod ? 'ctrlHome' : 'home';
    case 'End': return mod ? 'ctrlEnd' : 'end';
    case 'PageUp': return 'pageUp';
    case 'PageDown': return 'pageDown';
    case '+': return 'plus';
    case '-': return 'minus';
    default: return null;
  }
}

/** WAI-ARIA grid keyboard model (group/detail rows are single cells; groups and tree nodes expand/collapse). */
export function moveCell(current: GridCell, key: GridKey, ctx: GridNavContext): GridNavResult {
  if (ctx.rowCount === 0 || ctx.columnCount === 0) return { cell: { row: 0, column: 0 }, action: 'none' };
  const kind = ctx.kind ?? (() => 'data' as GridRowKind);
  const canExpand = ctx.canExpand ?? (() => false);
  const isExpanded = ctx.isExpanded ?? (() => false);
  const page = ctx.pageRows ?? 10;
  const row = Math.min(Math.max(current.row, 0), ctx.rowCount - 1);
  const column = Math.min(Math.max(current.column, 0), ctx.columnCount - 1);
  const lastRow = ctx.rowCount - 1;
  const lastCol = ctx.columnCount - 1;
  const single = kind(row) !== 'data';
  const expandable = canExpand(row) && (single || column === 0);
  const stay = (r: number, c: number): GridNavResult => ({ cell: { row: r, column: c }, action: 'none' });

  switch (key) {
    case 'up': return stay(Math.max(0, row - 1), column);
    case 'down': return stay(Math.min(lastRow, row + 1), column);
    case 'pageUp': return stay(Math.max(0, row - page), column);
    case 'pageDown': return stay(Math.min(lastRow, row + page), column);
    case 'ctrlHome': return stay(0, 0);
    case 'ctrlEnd': return stay(lastRow, lastCol);
    case 'home': return stay(row, 0);
    case 'end': return stay(row, single ? column : lastCol);
    case 'left':
      if (expandable && isExpanded(row)) return { cell: { row, column }, action: 'collapse' };
      return stay(row, single ? column : Math.max(0, column - 1));
    case 'right':
      if (expandable && !isExpanded(row)) return { cell: { row, column }, action: 'expand' };
      return stay(row, single ? column : Math.min(lastCol, column + 1));
    case 'plus':
      return { cell: { row, column }, action: canExpand(row) && !isExpanded(row) ? 'expand' : 'none' };
    case 'minus':
      return { cell: { row, column }, action: canExpand(row) && isExpanded(row) ? 'collapse' : 'none' };
    default:
      return stay(row, column);
  }
}

// ---- editing -----------------------------------------------------------------------------------------------

export interface GridCellEdit<T> {
  readonly item: T;
  readonly rowKey: string;
  readonly column: GridColumn<T>;
  readonly original: unknown;
  draft: unknown;
  draftText: string | null;
  error: string | null;
}

export interface GridCellChange<T> {
  readonly item: T;
  readonly rowKey: string;
  readonly field: string;
  readonly oldValue: unknown;
  readonly newValue: unknown;
}

export type GridEditCommitKey = 'enter' | 'tab';

const same = (a: unknown, b: unknown) =>
  a === b || (a instanceof Date && b instanceof Date && a.getTime() === b.getTime()) || (Number.isNaN(a) && Number.isNaN(b));

/** Inline editing state machine (cell mode writes through; batch mode collects pending changes). */
export class EditSession<T> {
  readonly mode: GridEditMode;
  current: GridCellEdit<T> | null = null;
  private readonly pendingList: GridCellChange<T>[] = [];
  private committedListeners = new Set<(c: GridCellChange<T>) => void>();
  private changeListeners = new Set<() => void>();

  constructor(mode: GridEditMode = 'cell') {
    this.mode = mode;
  }

  get isEditing(): boolean { return this.current !== null; }
  get pending(): readonly GridCellChange<T>[] { return this.pendingList; }

  onCommitted(l: (c: GridCellChange<T>) => void): () => void {
    this.committedListeners.add(l);
    return () => this.committedListeners.delete(l);
  }

  onChange(l: () => void): () => void {
    this.changeListeners.add(l);
    return () => this.changeListeners.delete(l);
  }

  begin(item: T, rowKey: string, column: GridColumn<T>, initialText?: string): boolean {
    if (this.mode === 'none' || !column.editable || !column.setter) return false;
    if (this.isEditing) this.cancel();
    const original = this.getValue(item, rowKey, column);
    this.current = { item, rowKey, column, original, draft: original, draftText: null, error: null };
    if (initialText !== undefined) this.setDraftText(initialText);
    this.raise();
    return true;
  }

  setDraftText(text: string): void {
    const e = this.require();
    e.draftText = text;
    try {
      e.draft = parseCell(e.column, text);
      e.error = e.column.validate?.(e.draft) ?? null;
    } catch (err) {
      if (!(err instanceof GridFormatError) && !(err instanceof Error)) throw err;
      e.error = (err as Error).message;
    }
    this.raise();
  }

  setDraft(value: unknown): void {
    const e = this.require();
    e.draftText = null;
    e.draft = value;
    e.error = e.column.validate?.(value) ?? null;
    this.raise();
  }

  commit(): boolean {
    const e = this.current;
    if (!e) return true;
    if (e.error !== null) return false;
    if (!same(e.draft, e.original)) {
      const original = cellValue(e.column, e.item);
      const change: GridCellChange<T> = { item: e.item, rowKey: e.rowKey, field: e.column.field, oldValue: original, newValue: e.draft };
      if (this.mode === 'batch') {
        this.removePending(e.rowKey, e.column.field);
        if (!same(e.draft, original)) this.pendingList.push(change);
      } else {
        e.column.setter!(e.item, e.draft);
        for (const l of this.committedListeners) l(change);
      }
    }
    this.current = null;
    this.raise();
    return true;
  }

  cancel(): void {
    if (!this.current) return;
    this.current = null;
    this.raise();
  }

  getValue(item: T, rowKey: string, column: GridColumn<T>): unknown {
    for (let i = this.pendingList.length - 1; i >= 0; i--) {
      const p = this.pendingList[i]!;
      if (p.rowKey === rowKey && p.field === column.field) return p.newValue;
    }
    return cellValue(column, item);
  }

  hasPending(rowKey: string, field: string): boolean {
    return this.pendingList.some((p) => p.rowKey === rowKey && p.field === field);
  }

  commitAll(columns: readonly GridColumn<T>[]): GridCellChange<T>[] {
    if (this.isEditing && !this.commit()) return [];
    const by = new Map(columns.map((c) => [c.field, c] as const));
    const applied = [...this.pendingList];
    for (const change of applied) {
      const c = by.get(change.field);
      if (c?.setter) {
        c.setter(change.item, change.newValue);
        for (const l of this.committedListeners) l(change);
      }
    }
    this.pendingList.length = 0;
    this.raise();
    return applied;
  }

  discardAll(): void {
    this.pendingList.length = 0;
    this.current = null;
    this.raise();
  }

  static moveAfterCommit(key: GridEditCommitKey, shift: boolean): GridKey {
    return key === 'enter' ? (shift ? 'up' : 'down') : shift ? 'left' : 'right';
  }

  private removePending(rowKey: string, field: string) {
    for (let i = this.pendingList.length - 1; i >= 0; i--) {
      const p = this.pendingList[i]!;
      if (p.rowKey === rowKey && p.field === field) this.pendingList.splice(i, 1);
    }
  }

  private require(): GridCellEdit<T> {
    if (!this.current) throw new Error('No cell is being edited.');
    return this.current;
  }

  private raise() {
    for (const l of this.changeListeners) l();
  }
}
