import { LitElement, css, html, nothing, type PropertyValues, type TemplateResult } from 'lit';
import { classMap } from 'lit/directives/class-map.js';
import { styleMap } from 'lit/directives/style-map.js';
import { hostReset, styles } from '../internal/styles';
import { renderIcon } from '../internal/icon';
import { uid } from '../internal/dom';
import { tokenNumber } from '../core/tokens';
import {
  addGroupBy, clearFilter, clearFilters, columnState, createGridState, gridStateToJson, moveColumn, pinColumn,
  removeGroupBy, resizeColumn, setColumnHidden, setFilter, setGroupBy, setGroupExpanded, setPage, setPageSize,
  setQuickFilter, setSorts, sortOf, toggleDetail, toggleGroup, toggleSort, type GridFilter, type GridState,
} from '../core/grid/state';
import { DataPipeline, type GridPipelineResult, type GridViewRow } from '../core/grid/pipeline';
import { computeViewport, estimateColumnWidth, pageRows, resolveColumns, scrollingColumnRange, scrollToReveal, type GridColumnLayout, type ResolvedColumn } from '../core/grid/layout';
import { EditSession, SelectionModel, gridKeyFromEvent, moveCell, type GridCell } from '../core/grid/interaction';
import { GridRowCache, queryFromState, sameResultSet, type GridDataSource } from '../core/grid/data-source';
import { toCsv, toTsv } from '../core/grid/export';
import { cellValue, col, displayText, keyText, type GridColumn, type GridEditMode, type GridPagination, type GridPin, type GridSelectionMode } from '../core/grid/values';
import type { Tone } from '../core/defaults';
import {
  aggregateLabel, aggregateText, describeFilter, draftFrom, editorKind, editorText, filterFromDraft, filterKind,
  renderCellContent, renderFilterEditor, type DataGridCellContext, type DataGridColumn, type FilterDraft,
} from './data-grid-cells';

export type { DataGridColumn, DataGridAction, DataGridCellContext } from './data-grid-cells';

// Rows are caller-defined objects; callbacks receive them untyped so `(d: MyRow) => …` assigns without casts.
// eslint-disable-next-line @typescript-eslint/no-explicit-any
type Item = any;
type ExportFormat = 'csv' | 'tsv';
type ExportScope = 'visible' | 'selected' | 'all';
type Density = 'compact' | 'comfortable';

/** A rendered row: an engine view row (client mode) or a cache slot (server mode). */
interface RowSlot {
  kind: 'data' | 'group' | 'detail' | 'skeleton';
  index: number;
  key: string;
  depth: number;
  item?: Item;
  view?: GridViewRow<Item>;
}

const SELECT_WIDTH = 44;
const DETAIL_TOGGLE_WIDTH = 36;
const SKELETON_ROWS = 30;
const FLASH_MS = 900;

/**
 * <sl-data-grid> — virtualised, keyboard-first data grid (docs/design/data-grid.md).
 *
 *   grid.columns = [{ field: 'name', title: 'Name', flex: 1 }, { field: 'size', type: 'number', aggregate: 'sum' }];
 *   grid.items = rows;   // client mode — or grid.dataSource = new InMemoryGridDataSource(rows, columns, 200)
 *
 * All behaviour (filtering, sorting, grouping, tree data, selection, navigation, editing, server paging, export)
 * comes from the platform-free engine in core/grid; this element renders the view window and forwards input.
 * Events: sl-state-changed {state, json}, sl-selection-changed {keys, items, count, allMatching},
 * sl-row-activated {item, key}, sl-cell-edit-committed {item, field, oldValue, newValue}, sl-copy {text},
 * sl-export {format, scope, text} (cancelable — prevent it to handle the download yourself).
 */
export class SlDataGrid extends LitElement {
  static override properties = {
    items: { type: Array },
    dataSource: { attribute: false },
    columns: { type: Array },
    rowKey: { attribute: false },
    selectionMode: { attribute: 'selection-mode', reflect: true },
    selectedItems: { attribute: false },
    pagination: { reflect: true },
    pageSize: { type: Number, attribute: 'page-size' },
    density: { reflect: true },
    striped: { type: Boolean, reflect: true },
    bordered: { type: Boolean, reflect: true },
    quickFilter: { attribute: 'quick-filter' },
    showToolbar: { type: Boolean, attribute: 'show-toolbar', reflect: true },
    showFooter: { type: Boolean, attribute: 'show-footer', reflect: true },
    groupable: { type: Boolean, reflect: true },
    groupBy: { type: Array, attribute: 'group-by' },
    childrenSelector: { attribute: false },
    rowDetail: { attribute: false },
    rowTone: { attribute: false },
    editMode: { attribute: 'edit-mode', reflect: true },
    loading: { type: Boolean, reflect: true },
    state: { attribute: false },
    label: {},
    detailHeight: { type: Number, attribute: 'detail-height' },
    flashUpdates: { type: Boolean, attribute: 'flash-updates' },
    // internal
    tick: { state: true },
  };

  static override styles = [
    hostReset,
    styles.base,
    styles.tone,
    styles.button,
    styles.selection,
    styles.feedback,
    styles.datagrid,
    css`
      :host { display: block; height: 480px; min-height: 0; }
      :host([hidden]) { display: none; }
    `,
  ];

  declare items: Item[];
  declare dataSource: GridDataSource<Item> | undefined;
  declare columns: DataGridColumn[];
  declare rowKey: ((item: Item) => unknown) | undefined;
  declare selectionMode: GridSelectionMode;
  declare selectedItems: Item[];
  declare pagination: GridPagination;
  declare pageSize: number;
  declare density: Density | undefined;
  declare striped: boolean;
  declare bordered: boolean;
  declare quickFilter: string;
  declare showToolbar: boolean;
  declare showFooter: boolean;
  declare groupable: boolean;
  declare groupBy: string[];
  declare childrenSelector: ((item: Item) => Iterable<Item> | null | undefined) | undefined;
  /** Detail template for expanded rows: (item) => template. A `<template slot="row-detail">` also works. */
  declare rowDetail: ((item: Item) => unknown) | undefined;
  declare rowTone: ((item: Item) => Tone | null | undefined) | undefined;
  declare editMode: GridEditMode;
  declare loading: boolean;
  declare state: GridState;
  /** Accessible name of the grid. */
  declare label: string | undefined;
  /** Height (px) of expanded detail rows. */
  declare detailHeight: number;
  /** Flash rows whose item object was replaced (live updates). */
  declare flashUpdates: boolean;
  declare tick: number;

  private readonly gridId = uid('sl-grid');
  private pipeline!: DataPipeline<Item>;
  private result: GridPipelineResult<Item> | null = null;
  private slots: RowSlot[] = [];
  private offsets: Float64Array | null = null;
  private layout: GridColumnLayout<Item> = { columns: [], order: [], totalWidth: 0 };
  private selection = new SelectionModel<string>('none');
  private edit = new EditSession<Item>('none');
  private cache: GridRowCache<Item> | null = null;
  private keyToItem = new Map<string, Item>();
  private previousItems = new Map<string, Item>();
  private flashing = new Map<string, number>();
  private active: GridCell = { row: 0, column: 0 };
  private keyboard = false;
  private vTop = 0;
  private vLeft = 0;
  private viewportHeight = 0;
  private viewportWidth = 0;
  private rowHeight = 32;
  private headerHeight = 40;
  private renderedWindow = '';
  private rafPending = false;
  private resizeObserver: ResizeObserver | null = null;
  private openFilter: string | null = null;
  private filterDraft: FilterDraft | null = null;
  private chooserOpen = false;
  private drag: { field: string; startX: number; moved: boolean; over: string | null; after: boolean; overGroupBar: boolean } | null = null;
  private resizing: { field: string; startX: number; startWidth: number; column: DataGridColumn } | null = null;
  private announcement = '';
  private suppressClick = false;
  private slottedColumns: DataGridColumn[] = [];
  private detailTemplate: HTMLTemplateElement | null = null;

  constructor() {
    super();
    this.items = [];
    this.columns = [];
    this.selectionMode = 'none';
    this.selectedItems = [];
    this.pagination = 'none';
    this.pageSize = 50;
    this.striped = false;
    this.bordered = false;
    this.quickFilter = '';
    this.showToolbar = false;
    this.showFooter = false;
    this.groupable = false;
    this.groupBy = [];
    this.editMode = 'none';
    this.loading = false;
    this.state = createGridState();
    this.detailHeight = 160;
    this.flashUpdates = true;
    this.tick = 0;
  }

  // ---- public API --------------------------------------------------------------------------------------------

  /** Effective columns: the `columns` property, else `<* slot="columns" field="…">` children. */
  get effectiveColumns(): DataGridColumn[] {
    const source = this.columns.length ? this.columns : this.slottedColumns;
    if (source !== this.columnsSource) {
      this.columnsSource = source;
      // Editable plain-field columns write back to the same field unless a setter is given.
      this.columnsResolved = source.map((c) => (c.editable && !c.setter && !c.accessor
        ? { ...c, setter: (item: Item, value: unknown) => { (item as Record<string, unknown>)[c.field] = value; } }
        : c));
    }
    return this.columnsResolved;
  }
  private lastPageKey = '';
  private columnsSource: DataGridColumn[] | null = null;
  private columnsResolved: DataGridColumn[] = [];

  /** Engine result of the last render (client mode). */
  get view(): GridPipelineResult<Item> | null {
    return this.result;
  }

  /** Number of selected rows (including "all matching" in server mode). */
  get selectedCount(): number {
    return this.selection.count;
  }

  /** Replaces items by key (live updates); replaced rows flash. */
  upsert(changed: readonly Item[]): void {
    const byKey = new Map(changed.map((it) => [this.keyOf(it, -1), it] as const));
    this.items = this.items.map((it, i) => byKey.get(this.keyOf(it, i)) ?? it);
  }

  /** Re-runs the pipeline after items were mutated in place. */
  refresh(): void {
    this.pipeline?.invalidate();
    this.cache?.setQuery(queryFromState(this.state));
    this.requestUpdate();
  }

  /** Selects rows by key (engine keys: rowKey text). */
  selectKeys(keys: Iterable<string>): void {
    this.selection.set(keys);
  }

  clearSelection(): void {
    this.selection.clear();
  }

  /** Commits pending batch edits. */
  commitEdits(): void {
    const applied = this.edit.commitAll(this.effectiveColumns);
    if (applied.length) {
      this.pipeline.invalidate();
      this.announce(`${applied.length} change${applied.length === 1 ? '' : 's'} committed`);
    }
  }

  discardEdits(): void {
    this.edit.discardAll();
  }

  /** Builds an export (does not download). */
  async exportText(format: ExportFormat, scope: ExportScope = 'visible'): Promise<string> {
    const rows = await this.exportRows(scope);
    const cols = this.exportColumns();
    const valueOf = (item: Item, c: GridColumn<Item>) => this.edit.getValue(item, this.keyOf(item, -1), c);
    return format === 'csv' ? toCsv(cols, rows, true, valueOf) : toTsv(cols, rows, true, valueOf);
  }

  /** Exports and downloads (unless an `sl-export` listener calls preventDefault). */
  async export(format: ExportFormat, scope: ExportScope = 'visible'): Promise<void> {
    const text = await this.exportText(format, scope);
    const go = this.dispatchEvent(new CustomEvent('sl-export', { bubbles: true, composed: true, cancelable: true, detail: { format, scope, text } }));
    if (!go || typeof URL.createObjectURL !== 'function') return;
    const blob = new Blob([text], { type: format === 'csv' ? 'text/csv' : 'text/tab-separated-values' });
    const a = document.createElement('a');
    a.href = URL.createObjectURL(blob);
    a.download = `${this.label ?? 'export'}.${format}`;
    a.click();
    setTimeout(() => URL.revokeObjectURL(a.href), 1000);
  }

  /** Scrolls a view row into view and makes it active. */
  scrollToRow(index: number): void {
    this.active = { row: Math.max(0, Math.min(index, this.slots.length - 1)), column: this.active.column };
    this.reveal();
    this.requestUpdate();
  }

  // ---- lifecycle ---------------------------------------------------------------------------------------------

  override connectedCallback(): void {
    super.connectedCallback();
    if (typeof ResizeObserver !== 'undefined') {
      this.resizeObserver = new ResizeObserver(() => this.measure());
      this.resizeObserver.observe(this);
    }
  }

  override disconnectedCallback(): void {
    super.disconnectedCallback();
    this.resizeObserver?.disconnect();
    this.resizeObserver = null;
  }

  protected override willUpdate(changed: PropertyValues<this>): void {
    if (changed.has('selectionMode') || !this.selectionWired) this.wireSelection();
    if (changed.has('editMode') || !this.editWired) this.wireEdit();

    // Two-way props that live in the state. An assigned `state` wins; otherwise changed props update the state.
    // On the first render only props that differ from their defaults apply, so an initial `state` isn't wiped.
    let s = this.state ?? createGridState();
    const external = changed.has('state') && s !== this.lastState && this.hasUpdated;
    if (!external) {
      const first = !this.hasUpdated;
      if (changed.has('groupBy') && !sameList(this.groupBy, s.groupBy) && (!first || this.groupBy?.length)) s = setGroupBy(s, ...(this.groupBy ?? []));
      if (changed.has('quickFilter') && (this.quickFilter ?? '') !== s.quickFilter && (!first || this.quickFilter)) s = setQuickFilter(s, this.quickFilter ?? '');
      if (changed.has('pageSize') && this.pageSize !== s.pageSize && (!first || this.pageSize !== 50)) s = setPageSize(s, this.pageSize);
    }
    if (s !== this.state) this.state = s;
    this.lastState = this.state;
    if (!sameList(this.groupBy, this.state.groupBy)) this.groupBy = [...this.state.groupBy];
    if (this.quickFilter !== this.state.quickFilter) this.quickFilter = this.state.quickFilter;
    if (this.pageSize !== this.state.pageSize) this.pageSize = this.state.pageSize;

    if (!this.pipeline || changed.has('columns') || changed.has('rowKey') || changed.has('childrenSelector') || changed.has('pagination')) {
      this.pipeline = new DataPipeline(this.effectiveColumns, {
        rowKey: this.rowKey,
        childrenSelector: this.childrenSelector,
        paginate: this.pagination === 'pages',
      });
    }

    if (changed.has('dataSource')) {
      this.cacheUnsub?.();
      this.cache = this.dataSource ? new GridRowCache(this.dataSource, 100) : null;
      this.cacheUnsub = this.cache?.onChange(() => this.requestUpdate()) ?? null;
      if (this.cache) this.cache.setQuery(queryFromState(this.state));
    } else if (this.cache && !sameResultSet(this.cache.query, queryFromState(this.state))) {
      this.cache.setQuery(queryFromState(this.state));
      this.selection.clear();
    }

    if (changed.has('items') && !this.cache) this.trackLiveUpdates();
    this.buildView();
  }

  protected override firstUpdated(): void {
    this.readSlots();
    this.measure();
  }

  protected override updated(changed: PropertyValues<this>): void {
    super.updated(changed);
    // Scroll ticks are the hot path: measuring there would force a synchronous layout every frame.
    if (!(changed.size === 1 && changed.has('tick'))) this.measureRows();
    // A new page (or switching paging on/off) starts at the top of the viewport.
    const pageKey = `${this.pagination}:${this.state.pageIndex}:${this.state.pageSize}`;
    if (pageKey !== this.lastPageKey) {
      const first = this.lastPageKey === '';
      this.lastPageKey = pageKey;
      const vp = this.renderRoot.querySelector<HTMLElement>('.sl-data-grid__viewport');
      if (!first && vp && vp.scrollTop !== 0) {
        vp.scrollTop = 0;
        this.vTop = 0;
        this.requestUpdate();
      }
    }
    if (this.cache) void this.cache.ensureRange(...this.cacheRange());
    const editor = this.renderRoot.querySelector<HTMLElement>('.sl-data-grid__editor');
    if (editor && this.renderRoot.ownerDocument && (this.renderRoot as ShadowRoot).activeElement !== editor) editor.focus();
  }

  // ---- view model --------------------------------------------------------------------------------------------

  private lastState: GridState | null = null;
  private selectionWired = false;
  private editWired = false;
  private selectionUnsub: (() => void) | null = null;
  private editUnsub: (() => void) | null = null;
  private cacheUnsub: (() => void) | null = null;

  private wireSelection(): void {
    this.selectionUnsub?.();
    this.selection = new SelectionModel<string>(this.selectionMode ?? 'none');
    this.selectionUnsub = this.selection.onChange(() => { this.headerStateMemo = null; this.selectionChanged(); });
    this.selectionWired = true;
  }

  private wireEdit(): void {
    this.editUnsub?.();
    this.edit = new EditSession<Item>(this.editMode ?? 'none');
    const a = this.edit.onChange(() => this.requestUpdate());
    const b = this.edit.onCommitted((c) => {
      this.pipeline?.invalidate();
      this.dispatchEvent(new CustomEvent('sl-cell-edit-committed', { bubbles: true, composed: true, detail: { ...c } }));
    });
    this.editUnsub = () => { a(); b(); };
    this.editWired = true;
  }

  private get isComfortable(): boolean {
    if (this.density) return this.density === 'comfortable';
    let node: Element | null = this;
    while (node) {
      const d = node.getAttribute('data-sl-density');
      if (d) return d === 'comfortable';
      node = node.parentElement ?? ((node.getRootNode() as ShadowRoot).host as Element | undefined) ?? null;
    }
    return false;
  }

  private tokenRowHeight(fallback: number): number {
    try {
      return tokenNumber(this.isComfortable ? '--sl-size-control-comfortable-md' : '--sl-size-control-compact-md');
    } catch {
      return fallback;
    }
  }

  private keyOf(item: Item, index: number): string {
    const k = this.rowKey?.(item);
    return k === null || k === undefined ? '#' + index : keyText(k);
  }

  private buildView(): void {
    const columns = this.effectiveColumns;
    if (this.cache) {
      this.result = null;
      const total = this.cache.totalCount;
      const count = total ?? SKELETON_ROWS;
      const paged = this.pagination === 'pages';
      const first = paged ? this.state.pageIndex * this.state.pageSize : 0;
      const n = paged ? Math.max(0, Math.min(this.state.pageSize, count - first)) : count;
      const slots: RowSlot[] = new Array(n);
      for (let i = 0; i < n; i++) {
        const item = this.cache.get(first + i);
        slots[i] = item === undefined
          ? { kind: 'skeleton', index: i, key: 'skeleton:' + (first + i), depth: 0 }
          : { kind: 'data', index: i, key: this.keyOf(item, first + i), depth: 0, item };
        if (item !== undefined) this.keyToItem.set(slots[i]!.key, item);
      }
      this.slots = slots;
    } else {
      const result = this.pipeline.run(this.items ?? [], this.state);
      if (result !== this.result) {
        this.result = result;
        this.keyToItem = new Map();
        for (let i = 0; i < result.items.length; i++) this.keyToItem.set(result.itemKeys[i]!, result.items[i]);
        this.slots = result.rows.map((r) => ({ kind: r.kind, index: r.index, key: r.key, depth: r.depth, item: r.item, view: r }));
      }
    }
    this.layout = resolveColumns(columns, this.state, Math.max(0, (this.viewportWidth || 960) - this.leadingWidth));
    this.buildOffsets();
    if (this.active.row >= this.slots.length) this.active = { row: Math.max(0, this.slots.length - 1), column: this.active.column };
    if (this.active.column >= this.layout.columns.length) this.active = { row: this.active.row, column: Math.max(0, this.layout.columns.length - 1) };
  }

  private buildOffsets(): void {
    if (!this.state.expandedDetails.length || !this.slots.some((s) => s.kind === 'detail')) {
      this.offsets = null;
      return;
    }
    const o = new Float64Array(this.slots.length + 1);
    for (let i = 0; i < this.slots.length; i++) o[i + 1] = o[i]! + (this.slots[i]!.kind === 'detail' ? this.detailHeight : this.rowHeight);
    this.offsets = o;
  }

  private rowTop(i: number): number {
    return this.offsets ? this.offsets[i]! : i * this.rowHeight;
  }

  private rowHeightOf(i: number): number {
    return this.slots[i]?.kind === 'detail' ? this.detailHeight : this.rowHeight;
  }

  private get totalHeight(): number {
    return this.offsets ? this.offsets[this.slots.length]! : this.slots.length * this.rowHeight;
  }

  /** The [first, count) window of slots to render. */
  private window(): { first: number; count: number; offsetTop: number } {
    const body = Math.max(0, (this.viewportHeight || 480) - this.headerHeight);
    if (!this.offsets) {
      const v = computeViewport(this.vTop, body, this.rowHeight, this.slots.length, 8);
      return { first: v.first, count: v.count, offsetTop: v.offsetTop };
    }
    const o = this.offsets;
    let lo = 0, hi = this.slots.length;
    while (lo < hi) {
      const mid = (lo + hi) >> 1;
      if (o[mid + 1]! <= this.vTop) lo = mid + 1;
      else hi = mid;
    }
    const first = Math.max(0, lo - 8);
    let last = lo;
    while (last < this.slots.length && o[last]! < this.vTop + body) last++;
    last = Math.min(this.slots.length, last + 8);
    return { first, count: last - first, offsetTop: o[first]! };
  }

  private cacheRange(): [number, number] {
    const w = this.window();
    const base = this.pagination === 'pages' ? this.state.pageIndex * this.state.pageSize : 0;
    return [base + w.first, Math.max(w.count, 1)];
  }

  private get leadingWidth(): number {
    return (this.selectionMode === 'multi' ? SELECT_WIDTH : 0) + (this.hasDetail ? DETAIL_TOGGLE_WIDTH : 0);
  }

  private get hasDetail(): boolean {
    return !!this.rowDetail || !!this.detailTemplate;
  }

  private trackLiveUpdates(): void {
    if (!this.flashUpdates || !this.rowKey) {
      this.previousItems.clear();
      return;
    }
    const items = this.items ?? [];
    const next = new Map<string, Item>();
    const now = Date.now();
    const had = this.previousItems.size > 0;
    for (let i = 0; i < items.length; i++) {
      const key = this.keyOf(items[i], i);
      next.set(key, items[i]);
      if (had) {
        const prev = this.previousItems.get(key);
        if (prev !== undefined && prev !== items[i]) this.flashing.set(key, now);
      }
    }
    this.previousItems = items.length <= 250_000 ? next : new Map();
    this.pipeline?.invalidate();
    if (this.flashing.size) setTimeout(() => this.expireFlash(), FLASH_MS);
  }

  private expireFlash(): void {
    const now = Date.now();
    for (const [k, t] of this.flashing) if (now - t >= FLASH_MS - 20) this.flashing.delete(k);
    this.requestUpdate();
  }

  private readSlots(): void {
    const colSlot = this.renderRoot.querySelector<HTMLSlotElement>('slot[name="columns"]');
    const defs = (colSlot?.assignedElements({ flatten: true }) ?? []).filter((e) => e.hasAttribute('field'));
    if (defs.length) {
      this.slottedColumns = defs.map(columnFromElement);
      if (!this.columns.length) {
        this.pipeline = new DataPipeline(this.slottedColumns, { rowKey: this.rowKey, childrenSelector: this.childrenSelector, paginate: this.pagination === 'pages' });
        this.requestUpdate();
      }
    }
    const detailSlot = this.renderRoot.querySelector<HTMLSlotElement>('slot[name="row-detail"]');
    const tpl = detailSlot?.assignedElements({ flatten: true }).find((e): e is HTMLTemplateElement => e instanceof HTMLTemplateElement) ?? null;
    if (tpl !== this.detailTemplate) {
      this.detailTemplate = tpl;
      this.requestUpdate();
    }
  }

  /** Row and header heights come from the CSS (density tokens, header-group rows); fallbacks cover layout-less DOMs. */
  private measureRows(): void {
    const probe = this.renderRoot.querySelector<HTMLElement>('.sl-data-grid__probe');
    const head = this.renderRoot.querySelector<HTMLElement>('.sl-data-grid__probe--head');
    const rh = probe?.offsetHeight || this.tokenRowHeight(this.isComfortable ? 40 : 32);
    const header = this.renderRoot.querySelector<HTMLElement>('.sl-data-grid__header');
    const headerRows = header?.querySelectorAll(':scope > .sl-data-grid__header-row, :scope > .sl-data-grid__header-groups').length || 1;
    const hh = header?.offsetHeight || (head?.offsetHeight || (this.isComfortable ? 48 : 40)) * headerRows;
    if (rh !== this.rowHeight || hh !== this.headerHeight) {
      this.rowHeight = rh;
      this.headerHeight = hh;
      this.buildOffsets();
      this.requestUpdate();
    }
  }

  private measure(): void {
    const vp = this.renderRoot.querySelector<HTMLElement>('.sl-data-grid__viewport');
    if (!vp) return;
    const h = vp.clientHeight;
    const w = vp.clientWidth;
    if (h !== this.viewportHeight || w !== this.viewportWidth) {
      this.viewportHeight = h;
      this.viewportWidth = w;
      this.requestUpdate();
    }
  }

  // ---- state transitions -------------------------------------------------------------------------------------

  /** Applies a state transition and notifies (`sl-state-changed`). */
  private commitState(next: GridState, message?: string): void {
    if (next === this.state) return;
    this.state = next;
    if (message) this.announce(message);
    this.dispatchEvent(new CustomEvent('sl-state-changed', { bubbles: true, composed: true, detail: { state: next, json: gridStateToJson(next) } }));
  }

  private announce(message: string): void {
    this.announcement = message;
    this.requestUpdate();
  }

  private selectionChanged(): void {
    const keys = this.selection.allMatching ? [...this.keyToItem.keys()].filter((k) => this.selection.isSelected(k)) : [...this.selection.selectedKeys];
    const items: Item[] = [];
    for (const k of keys) {
      const it = this.keyToItem.get(k);
      if (it !== undefined) items.push(it);
    }
    this.selectedItems = items;
    this.dispatchEvent(new CustomEvent('sl-selection-changed', {
      bubbles: true, composed: true,
      detail: { keys, items, count: this.selection.count, allMatching: this.selection.allMatching },
    }));
    this.announce(this.selection.count ? `${this.selection.count} selected` : 'Selection cleared');
  }

  // ---- input wiring ------------------------------------------------------------------------------------------

  private onScroll = (e: Event): void => {
    const vp = e.currentTarget as HTMLElement;
    this.vTop = vp.scrollTop;
    const xChanged = vp.scrollLeft !== this.vLeft;
    this.vLeft = vp.scrollLeft;
    const root = this.renderRoot.querySelector('.sl-data-grid');
    root?.classList.toggle('is-scrolled-x', this.vLeft > 0);
    root?.classList.toggle('is-scrollable-end', this.vLeft + vp.clientWidth < vp.scrollWidth - 1);
    const w = this.window();
    const key = `${w.first}:${w.count}:${xChanged && this.layout.columns.length > 40 ? Math.round(this.vLeft / 100) : ''}`;
    if (key === this.renderedWindow && !xChanged) return;
    if (this.rafPending) return;
    this.rafPending = true;
    const run = () => {
      this.rafPending = false;
      this.tick++;
    };
    if (typeof requestAnimationFrame === 'function') requestAnimationFrame(run);
    else run();
  };

  private columnAt(index: number): ResolvedColumn<Item> | undefined {
    return this.layout.columns[index];
  }

  private navContext() {
    const body = Math.max(1, (this.viewportHeight || 480) - this.headerHeight);
    return {
      rowCount: this.slots.length,
      columnCount: Math.max(1, this.layout.columns.length),
      pageRows: pageRows(body, this.rowHeight),
      kind: (r: number) => {
        const k = this.slots[r]?.kind;
        return k === 'group' || k === 'detail' ? k : 'data';
      },
      canExpand: (r: number) => {
        const s = this.slots[r];
        return s?.kind === 'group' || !!s?.view?.hasChildren;
      },
      isExpanded: (r: number) => !!this.slots[r]?.view?.expanded,
    } as const;
  }

  private onKeyDown = (e: KeyboardEvent): void => {
    if (this.edit.isEditing || e.defaultPrevented) return;
    const target = e.composedPath()[0] as HTMLElement | undefined;
    if (target && target !== e.currentTarget && target.closest?.('button, input, select, textarea, sl-menu')) return;
    this.keyboard = true;
    const mod = e.ctrlKey || e.metaKey;
    const slot = this.slots[this.active.row];

    if (e.altKey && (e.key === 'ArrowLeft' || e.key === 'ArrowRight')) {
      const c = this.columnAt(this.active.column);
      if (c && c.column.reorderable !== false) {
        e.preventDefault();
        const order = this.layout.order;
        const to = order.indexOf(c.field) + (e.key === 'ArrowLeft' ? -1 : 1);
        this.commitState(moveColumn(this.state, order, c.field, to), `${col.title(c.column)} moved`);
        this.active = { row: this.active.row, column: Math.max(0, Math.min(this.active.column + (e.key === 'ArrowLeft' ? -1 : 1), this.layout.columns.length - 1)) };
      }
      return;
    }

    const gk = gridKeyFromEvent(e);
    if (gk && !((gk === 'plus' || gk === 'minus') && mod)) {
      e.preventDefault();
      const before = this.active;
      const r = moveCell(this.active, gk, this.navContext());
      if (r.action !== 'none') this.toggleSlot(this.active.row, r.action === 'expand');
      this.active = r.cell;
      if (e.shiftKey && this.selectionMode === 'multi' && (gk === 'up' || gk === 'down' || gk === 'pageUp' || gk === 'pageDown') && before.row !== r.cell.row) {
        const k = this.slots[r.cell.row];
        if (k?.kind === 'data') {
          if (!this.selection.hasAnchor) {
            const prev = this.slots[before.row];
            if (prev?.kind === 'data') this.selection.click(prev.key, this.visibleKeys());
          }
          this.selection.extendTo(k.key, this.visibleKeys());
        }
      }
      this.reveal();
      this.requestUpdate();
      return;
    }

    switch (e.key) {
      case ' ':
        e.preventDefault();
        if (slot?.kind === 'group' || (slot?.view?.hasChildren && this.selectionMode === 'none')) this.toggleSlot(this.active.row);
        else if (slot?.kind === 'data' && this.selectionMode !== 'none') {
          if (this.selectionMode === 'single') this.selection.click(slot.key, this.visibleKeys());
          else this.selection.toggle(slot.key);
        }
        return;
      case 'Enter':
        e.preventDefault();
        if (slot?.kind === 'group') this.toggleSlot(this.active.row);
        else if (slot?.kind === 'data' && !this.beginEdit()) this.activate(slot);
        return;
      case 'F2':
        e.preventDefault();
        this.beginEdit();
        return;
      case 'Escape':
        if (this.selection.count && this.selectionMode !== 'none') {
          e.preventDefault();
          this.selection.clear();
        }
        return;
    }

    if (mod && (e.key === 'a' || e.key === 'A') && this.selectionMode === 'multi') {
      e.preventDefault();
      this.selectAll();
      return;
    }
    if (mod && (e.key === 'c' || e.key === 'C')) {
      e.preventDefault();
      void this.copy(e.shiftKey);
      return;
    }
    if (!mod && !e.altKey && e.key.length === 1 && slot?.kind === 'data') {
      if (this.beginEdit(e.key)) e.preventDefault();
    }
  };

  private visibleKeys(): readonly string[] {
    if (this.result) return this.result.visibleKeys;
    return this.slots.filter((s) => s.kind === 'data').map((s) => s.key);
  }

  private selectAll(): void {
    if (this.cache) this.selection.selectAllMatching(this.cache.totalCount ?? this.slots.length);
    else this.selection.selectAll(this.result?.itemKeys ?? []);
  }

  private toggleHeaderSelection(): void {
    if (this.selectAllState() === 'all') this.selection.clear();
    else this.selectAll();
  }

  private toggleSlot(row: number, expand?: boolean): void {
    const s = this.slots[row];
    if (!s?.view) return;
    const id = s.kind === 'group' ? s.view.groupId! : 'row:' + s.key;
    if (s.kind !== 'group' && !s.view.hasChildren) return;
    const next = expand === undefined ? toggleGroup(this.state, id) : setGroupExpanded(this.state, id, expand);
    this.commitState(next, `${s.view.expanded ? 'Collapsed' : 'Expanded'} ${s.view.groupKeyText ?? ''}`.trim());
  }

  private activate(slot: RowSlot): void {
    this.dispatchEvent(new CustomEvent('sl-row-activated', { bubbles: true, composed: true, detail: { item: slot.item, key: slot.key } }));
  }

  private reveal(): void {
    const vp = this.renderRoot.querySelector<HTMLElement>('.sl-data-grid__viewport');
    if (!vp) return;
    const body = Math.max(0, (this.viewportHeight || vp.clientHeight) - this.headerHeight);
    const top = this.rowTop(this.active.row);
    const h = this.rowHeightOf(this.active.row);
    let next = this.vTop;
    if (this.offsets) {
      if (top < next) next = top;
      else if (top + h > next + body) next = top + h - body;
    } else {
      next = scrollToReveal(this.active.row, this.vTop, body, this.rowHeight);
    }
    if (next !== this.vTop) {
      vp.scrollTop = next;
      this.vTop = next;
    }
    this.revealColumn(this.columnAt(this.active.column));
  }

  /** Scrolls horizontally so a scrolling (unpinned) column is fully visible between the pinned sections. */
  private revealColumn(c: ResolvedColumn<Item> | undefined): void {
    const vp = this.renderRoot.querySelector<HTMLElement>('.sl-data-grid__viewport');
    if (!vp || !c || c.pin !== 'none') return;
    const startPinned = this.layout.columns.filter((x) => x.pin === 'start').reduce((a, x) => a + x.width, 0) + this.leadingWidth;
    const endPinned = this.layout.columns.filter((x) => x.pin === 'end').reduce((a, x) => a + x.width, 0);
    const left = c.left + this.leadingWidth;
    const width = this.viewportWidth || vp.clientWidth;
    if (left - startPinned < vp.scrollLeft) vp.scrollLeft = left - startPinned;
    else if (left + c.width > vp.scrollLeft + width - endPinned) vp.scrollLeft = left + c.width - width + endPinned;
    this.vLeft = vp.scrollLeft;
  }

  private focusGrid(): void {
    this.renderRoot.querySelector<HTMLElement>('.sl-data-grid__viewport')?.focus({ preventScroll: true });
  }

  private onRowClick(e: MouseEvent, slot: RowSlot, row: number, column: number): void {
    if (this.suppressClick) return;
    const target = e.composedPath()[0] as HTMLElement;
    if (target.closest?.('button, a, input, select, sl-menu')) return;
    this.keyboard = false;
    if (this.edit.isEditing && !this.edit.commit()) return;
    this.active = { row, column: column < 0 ? this.active.column : column };
    this.focusGrid();
    if (slot.kind === 'group') {
      this.toggleSlot(row);
    } else if (slot.kind === 'data' && this.selectionMode !== 'none') {
      this.selection.click(slot.key, this.visibleKeys(), this.selectionMode === 'multi' && (e.ctrlKey || e.metaKey), this.selectionMode === 'multi' && e.shiftKey);
    }
    this.requestUpdate();
  }

  private onRowDblClick(slot: RowSlot, row: number, column: number): void {
    if (slot.kind !== 'data') return;
    this.active = { row, column };
    if (!this.beginEdit()) this.activate(slot);
  }

  // ---- editing -----------------------------------------------------------------------------------------------

  private beginEdit(initialText?: string): boolean {
    const slot = this.slots[this.active.row];
    const c = this.columnAt(this.active.column);
    if (!slot || slot.kind !== 'data' || !c || slot.item === undefined) return false;
    if (this.edit.mode === 'none' || !c.column.editable || !c.column.setter) return false;
    const ok = this.edit.begin(slot.item, slot.key, c.column, undefined);
    if (ok && this.edit.current) {
      this.edit.current.draftText = initialText ?? editorText(c.column as DataGridColumn, this.edit.current.original);
      if (initialText !== undefined) this.edit.setDraftText(initialText);
    }
    this.requestUpdate();
    return ok;
  }

  private onEditorKey = (e: KeyboardEvent): void => {
    e.stopPropagation();
    if (e.key === 'Escape') {
      e.preventDefault();
      this.edit.cancel();
      this.keyboard = true;
      this.focusGrid();
      return;
    }
    if (e.key === 'Enter' || e.key === 'Tab') {
      e.preventDefault();
      if (!this.edit.commit()) return;
      const move = EditSession.moveAfterCommit(e.key === 'Enter' ? 'enter' : 'tab', e.shiftKey);
      this.active = moveCell(this.active, move, this.navContext()).cell;
      this.keyboard = true;
      this.reveal();
      this.focusGrid();
      this.requestUpdate();
    }
  };

  private onEditorInput = (e: Event): void => {
    const el = e.currentTarget as HTMLInputElement;
    if (el.type === 'checkbox') this.edit.setDraft(el.checked);
    else this.edit.setDraftText(el.value);
  };

  private onEditorBlur = (): void => {
    // Leaving the cell commits when valid (spreadsheet behaviour); invalid edits stay open.
    queueMicrotask(() => {
      if (!this.edit.isEditing) return;
      const focused = (this.renderRoot as ShadowRoot).activeElement;
      if (focused?.classList.contains('sl-data-grid__editor')) return;
      this.edit.commit();
    });
  };

  // ---- clipboard & export ------------------------------------------------------------------------------------

  private exportColumns(): GridColumn<Item>[] {
    return this.layout.columns.map((c) => c.column).filter((c) => col.type(c) !== 'actions');
  }

  private async exportRows(scope: ExportScope): Promise<Item[]> {
    if (this.cache && this.dataSource) {
      if (scope === 'selected' && !this.selection.allMatching) return [...this.selection.selectedKeys].map((k) => this.keyToItem.get(k)).filter((x) => x !== undefined);
      const total = this.cache.totalCount ?? 0;
      const res = await this.dataSource.query({ ...queryFromState(this.state), offset: 0, count: total });
      const items = [...res.items];
      return scope === 'selected' ? items.filter((it, i) => this.selection.isSelected(this.keyOf(it, i))) : items;
    }
    const r = this.result;
    if (!r) return [];
    if (scope === 'all') return [...(this.items ?? [])];
    if (scope === 'selected') return this.selection.selectedIn(r.itemKeys).map((k) => this.keyToItem.get(k)).filter((x) => x !== undefined);
    return [...r.items];
  }

  private async copy(withHeader: boolean): Promise<void> {
    let rows: Item[];
    if (this.selection.count) rows = await this.exportRows('selected');
    else {
      const s = this.slots[this.active.row];
      rows = s?.item !== undefined ? [s.item] : [];
    }
    if (!rows.length) return;
    const valueOf = (item: Item, c: GridColumn<Item>) => this.edit.getValue(item, this.keyOf(item, -1), c);
    const text = toTsv(this.exportColumns(), rows, withHeader, valueOf);
    this.dispatchEvent(new CustomEvent('sl-copy', { bubbles: true, composed: true, detail: { text, rows: rows.length } }));
    try {
      await navigator.clipboard?.writeText(text);
    } catch {
      /* clipboard may be unavailable (permissions); the sl-copy event still carries the text */
    }
    this.announce(`Copied ${rows.length} row${rows.length === 1 ? '' : 's'}`);
  }

  // ---- header interactions -----------------------------------------------------------------------------------

  private onHeaderPointerDown(e: PointerEvent, c: ResolvedColumn<Item>): void {
    if (e.button !== 0 || c.column.reorderable === false) return;
    this.drag = { field: c.field, startX: e.clientX, moved: false, over: null, after: false, overGroupBar: false };
    (e.currentTarget as HTMLElement).setPointerCapture?.(e.pointerId);
  }

  private onHeaderPointerMove(e: PointerEvent): void {
    const d = this.drag;
    if (!d) return;
    if (!d.moved && Math.abs(e.clientX - d.startX) < 5) return;
    d.moved = true;
    const bar = this.renderRoot.querySelector<HTMLElement>('.sl-data-grid__group-bar');
    const br = bar?.getBoundingClientRect();
    d.overGroupBar = !!br && e.clientY >= br.top && e.clientY <= br.bottom && e.clientX >= br.left && e.clientX <= br.right;
    d.over = null;
    if (!d.overGroupBar) {
      for (const cell of this.renderRoot.querySelectorAll<HTMLElement>('.sl-data-grid__header-cell[data-field]')) {
        const r = cell.getBoundingClientRect();
        if (e.clientX >= r.left && e.clientX <= r.right) {
          d.over = cell.dataset.field!;
          d.after = e.clientX > r.left + r.width / 2;
        }
      }
    }
    this.requestUpdate();
  }

  private onHeaderPointerUp(): void {
    const d = this.drag;
    this.drag = null;
    if (!d) return;
    if (d.moved) {
      this.suppressClick = true;
      setTimeout(() => (this.suppressClick = false));
      const c = this.layout.columns.find((x) => x.field === d.field);
      if (d.overGroupBar && this.groupable) {
        this.commitState(addGroupBy(this.state, d.field), `Grouped by ${c ? col.title(c.column) : d.field}`);
      } else if (d.over && d.over !== d.field) {
        const order = this.layout.order.filter((f) => f !== d.field);
        let to = order.indexOf(d.over) + (d.after ? 1 : 0);
        if (to < 0) to = order.length;
        this.commitState(moveColumn(this.state, this.layout.order, d.field, to), `${c ? col.title(c.column) : d.field} moved`);
      }
    }
    this.requestUpdate();
  }

  private onSortClick(e: MouseEvent, c: ResolvedColumn<Item>): void {
    if (this.suppressClick || !col.sortable(c.column)) return;
    const next = toggleSort(this.state, c.field, e.shiftKey);
    const dir = sortOf(next, c.field);
    this.commitState(next, dir ? `Sorted by ${col.title(c.column)}, ${dir}` : `Sort removed from ${col.title(c.column)}`);
  }

  private onResizeDown(e: PointerEvent, c: ResolvedColumn<Item>): void {
    e.stopPropagation();
    e.preventDefault();
    this.resizing = { field: c.field, startX: e.clientX, startWidth: c.width, column: c.column as DataGridColumn };
    (e.currentTarget as HTMLElement).setPointerCapture?.(e.pointerId);
  }

  private onResizeMove(e: PointerEvent): void {
    const r = this.resizing;
    if (!r) return;
    this.state = resizeColumn(this.state, r.field, r.startWidth + (e.clientX - r.startX), col.minWidth(r.column), col.maxWidth(r.column));
  }

  private onResizeUp(): void {
    if (!this.resizing) return;
    this.resizing = null;
    this.commitState({ ...this.state }, 'Column resized');
  }

  private autosize(c: ResolvedColumn<Item>): void {
    const sample: string[] = [];
    for (const s of this.slots) {
      if (s.kind !== 'data' || s.item === undefined) continue;
      sample.push(displayText(c.column, cellValue(c.column, s.item)));
      if (sample.length >= 200) break;
    }
    const width = estimateColumnWidth(c.column, sample);
    this.commitState(resizeColumn(this.state, c.field, width, col.minWidth(c.column), col.maxWidth(c.column)), `${col.title(c.column)} resized to fit`);
  }

  private onMenuSelect(e: CustomEvent<{ item: HTMLElement }>, c: ResolvedColumn<Item>): void {
    const action = e.detail.item.dataset.action;
    const title = col.title(c.column);
    switch (action) {
      case 'asc': this.commitState(setSorts(this.state, { field: c.field, direction: 'ascending' }), `Sorted by ${title}, ascending`); break;
      case 'desc': this.commitState(setSorts(this.state, { field: c.field, direction: 'descending' }), `Sorted by ${title}, descending`); break;
      case 'unsort': this.commitState({ ...this.state, sorts: this.state.sorts.filter((s) => s.field !== c.field) }, `Sort removed from ${title}`); break;
      case 'filter': this.openFilterFor(c.field); break;
      case 'group': this.commitState(addGroupBy(this.state, c.field), `Grouped by ${title}`); break;
      case 'ungroup': this.commitState(removeGroupBy(this.state, c.field), `Ungrouped ${title}`); break;
      case 'pin-start': case 'pin-end': case 'unpin': {
        const pin: GridPin = action === 'pin-start' ? 'start' : action === 'pin-end' ? 'end' : 'none';
        this.commitState(pinColumn(this.state, c.field, pin), pin === 'none' ? `${title} unpinned` : `${title} pinned`);
        break;
      }
      case 'autosize': this.autosize(c); break;
      case 'hide': this.commitState(setColumnHidden(this.state, c.field, true), `${title} hidden`); break;
      case 'columns': this.chooserOpen = true; this.requestUpdate(); break;
    }
  }

  private openFilterFor(field: string): void {
    const c = this.effectiveColumns.find((x) => x.field === field);
    if (!c) return;
    this.revealColumn(this.layout.columns.find((x) => x.field === field));
    this.openFilter = field;
    this.filterDraft = draftFrom(c, this.state.filters.find((f) => f.field === field));
    this.requestUpdate();
  }

  private applyFilter(c: DataGridColumn): void {
    const d = this.filterDraft;
    if (!d) return;
    const f = filterFromDraft(c, d);
    const next = f ? setFilter(this.state, f) : clearFilter(this.state, c.field);
    this.openFilter = null;
    this.filterDraft = null;
    this.commitState(next, f ? `Filtered ${col.title(c)}` : `Filter cleared from ${col.title(c)}`);
    this.requestUpdate();
  }

  private filterOptions(c: DataGridColumn): string[] {
    if (c.filterOptions) return [...c.filterOptions];
    if (c.enumOrder) return [...c.enumOrder];
    const seen = new Set<string>();
    for (const it of this.items ?? []) {
      const v = cellValue(c, it);
      if (v !== null && v !== undefined) seen.add(displayText(c, v));
      if (seen.size >= 200) break;
    }
    return [...seen].sort((a, b) => a.localeCompare(b));
  }

  // ---- rendering ---------------------------------------------------------------------------------------------

  override render(): TemplateResult {
    const comfortable = this.isComfortable;
    const total = this.cache ? this.cache.totalCount : this.result?.totalCount;
    const filtered = this.cache ? this.cache.totalCount : this.result?.filteredCount;
    const tree = !!this.childrenSelector;
    const rootClasses = {
      'sl-data-grid': true,
      'sl-data-grid--comfortable': comfortable,
      'sl-data-grid--striped': this.striped,
      'sl-data-grid--bordered': this.bordered,
      [`sl-data-grid--selection-${this.selectionMode}`]: true,
      [`sl-data-grid--edit-${this.editMode}`]: true,
      [`sl-data-grid--pagination-${this.pagination}`]: true,
      'is-keyboard': this.keyboard,
      'is-loading': this.loading || (!!this.cache && this.cache.totalCount === null),
    };
    const ariaRows = (this.cache ? (this.cache.totalCount ?? 0) : this.result?.viewRowCount ?? 0) + this.headerRowCount + (this.showFooter ? 1 : 0);
    const activeId = this.slots.length ? this.cellId(this.active.row, this.active.column) : undefined;
    const empty = this.slots.length === 0 && !this.loading;

    return html`<div class=${classMap(rootClasses)} part="base">
      <span class="sl-data-grid__probe" aria-hidden="true"></span>
      <span class="sl-data-grid__probe sl-data-grid__probe--head" aria-hidden="true"></span>
      ${this.renderBars(total, filtered)}
      <div class="sl-data-grid__viewport"
        role=${tree ? 'treegrid' : 'grid'}
        tabindex="0"
        aria-label=${this.label ?? 'Data grid'}
        aria-rowcount=${ariaRows}
        aria-colcount=${this.layout.columns.length + (this.selectionMode === 'multi' ? 1 : 0) + (this.hasDetail ? 1 : 0)}
        aria-multiselectable=${this.selectionMode === 'multi' ? 'true' : nothing}
        aria-activedescendant=${activeId ?? nothing}
        aria-busy=${this.loading ? 'true' : nothing}
        @scroll=${this.onScroll}
        @keydown=${this.onKeyDown}
        @pointerdown=${() => { this.keyboard = false; }}
        @focus=${() => this.requestUpdate()}
        @blur=${() => this.requestUpdate()}>
        <div class="sl-data-grid__canvas" style=${styleMap({ width: `${this.layout.totalWidth + this.leadingWidth}px`, height: `${this.totalHeight + this.headerHeight + (this.showFooter ? this.rowHeight : 0)}px` })}>
          ${this.renderHeader()}
          ${this.renderRows()}
        </div>
        ${this.showFooter ? this.renderFooter() : nothing}
      </div>
      ${this.loading || (this.cache && this.cache.totalCount === null) ? html`<div class="sl-data-grid__loading-bar" role="presentation"></div>` : nothing}
      ${empty ? this.renderEmpty() : nothing}
      ${this.pagination === 'pages' ? this.renderPager() : nothing}
      <div class="sl-data-grid__sr-only" role="status" aria-live="polite">${this.announcement}</div>
      <slot name="columns" hidden @slotchange=${() => this.readSlots()}></slot>
      <slot name="row-detail" hidden @slotchange=${() => this.readSlots()}></slot>
    </div>`;
  }

  private get headerRowCount(): number {
    return this.layout.columns.some((c) => (c.column as DataGridColumn).headerGroup) ? 2 : 1;
  }

  private cellId(row: number, column: number): string {
    return `${this.gridId}-r${row}-c${column}`;
  }

  private renderBars(total: number | null | undefined, filtered: number | null | undefined): TemplateResult {
    const count = this.selection.count;
    const showSelectionBar = this.selectionMode === 'multi' && count > 0;
    const pending = this.edit.pending.length;
    const cols = this.effectiveColumns;
    const chips = this.state.filters.map((f) => ({ f, c: cols.find((c) => c.field === f.field) })).filter((x): x is { f: GridFilter; c: DataGridColumn } => !!x.c);
    const matching = this.cache ? this.cache.totalCount ?? 0 : this.result?.filteredCount ?? 0;

    return html`
      ${showSelectionBar
        ? html`<div class="sl-data-grid__selection-bar" part="toolbar">
            <span class="sl-data-grid__selection-count">${count.toLocaleString()} selected</span>
            ${!this.selection.allMatching && this.cache && count < matching
              ? html`<button class="sl-data-grid__link" type="button" @click=${() => this.selection.selectAllMatching(matching)}>Select all ${matching.toLocaleString()} matching</button>`
              : nothing}
            <slot name="selection-actions"></slot>
            <span class="sl-data-grid__toolbar-spacer"></span>
            <button class="sl-data-grid__link" type="button" @click=${() => this.selection.clear()}>Clear selection</button>
          </div>`
        : this.showToolbar
          ? html`<div class="sl-data-grid__toolbar" part="toolbar">
              <sl-text-field class="sl-data-grid__quick-filter" size="small" input-type="search" start-icon="search" placeholder="Search" clearable
                aria-label="Search rows" .value=${this.state.quickFilter}
                @sl-value-changed=${(e: CustomEvent<{ value: string }>) => this.commitState(setQuickFilter(this.state, e.detail.value ?? ''))}></sl-text-field>
              <span class="sl-data-grid__status">${filtered !== null && filtered !== undefined && total !== null && total !== undefined
                ? filtered === total ? `${total.toLocaleString()} rows` : `${filtered.toLocaleString()} of ${total.toLocaleString()} rows`
                : 'Loading…'}</span>
              <slot name="toolbar-content"></slot>
              <span class="sl-data-grid__toolbar-spacer"></span>
              <sl-button size="small" variant="ghost" start-icon="layers" @click=${() => { this.density = this.isComfortable ? 'compact' : 'comfortable'; }}>
                ${this.isComfortable ? 'Comfortable' : 'Compact'}
              </sl-button>
              ${this.renderChooser()}
              <sl-menu placement="bottom-end" @sl-select=${(e: CustomEvent<{ item: HTMLElement }>) => {
                const [format, scope] = (e.detail.item.dataset.export ?? 'csv:visible').split(':') as [ExportFormat, ExportScope];
                void this.export(format, scope);
              }}>
                <sl-button slot="trigger" size="small" variant="ghost" start-icon="download" end-icon="chevron-down">Export</sl-button>
                <sl-menu-item data-export="csv:visible" label="CSV · current view"></sl-menu-item>
                <sl-menu-item data-export="csv:selected" label="CSV · selected rows" ?disabled=${count === 0}></sl-menu-item>
                <sl-menu-item data-export="csv:all" label="CSV · all rows"></sl-menu-item>
                <sl-menu-item separator></sl-menu-item>
                <sl-menu-item data-export="tsv:visible" label="TSV · current view"></sl-menu-item>
                <sl-menu-item data-export="tsv:selected" label="TSV · selected rows" ?disabled=${count === 0}></sl-menu-item>
              </sl-menu>
            </div>`
          : html`<slot name="toolbar-content" hidden></slot>`}
      ${chips.length
        ? html`<div class="sl-data-grid__chips" role="list" aria-label="Active filters">
            <span class="sl-data-grid__bar-label">Filters</span>
            ${chips.map(({ f, c }) => html`<span class="sl-data-grid__chip" role="listitem">
              <button class="sl-data-grid__link" type="button" style="text-decoration:none;color:inherit" @click=${() => this.openFilterFor(c.field)}>${describeFilter(c, f)}</button>
              <button class="sl-data-grid__chip-remove" type="button" aria-label=${`Remove filter ${col.title(c)}`}
                @click=${() => this.commitState(clearFilter(this.state, c.field), `Filter removed from ${col.title(c)}`)}>${renderIcon('x')}</button>
            </span>`)}
            <button class="sl-data-grid__link" type="button" @click=${() => this.commitState(clearFilters(this.state), 'Filters cleared')}>Clear all</button>
          </div>`
        : nothing}
      ${this.groupable && !this.childrenSelector
        ? html`<div class=${classMap({ 'sl-data-grid__group-bar': true, 'is-drop-target': !!this.drag?.overGroupBar })} aria-label="Group by">
            <span class="sl-data-grid__bar-label">Group by</span>
            ${this.state.groupBy.length === 0
              ? html`<span>Drag a column header here, or use a column's menu</span>`
              : this.state.groupBy.map((field, i) => {
                  const c = cols.find((x) => x.field === field);
                  return html`${i > 0 ? renderIcon('chevron-right') : nothing}<span class="sl-data-grid__chip">
                    ${c ? col.title(c) : field}
                    <button class="sl-data-grid__chip-remove" type="button" aria-label=${`Ungroup ${c ? col.title(c) : field}`}
                      @click=${() => this.commitState(removeGroupBy(this.state, field), `Ungrouped ${c ? col.title(c) : field}`)}>${renderIcon('x')}</button>
                  </span>`;
                })}
          </div>`
        : nothing}
      ${this.editMode === 'batch' && pending > 0
        ? html`<div class="sl-data-grid__batch-bar" role="status">
            ${renderIcon('alert-triangle')}
            <span class="sl-data-grid__batch-count">${pending} unsaved change${pending === 1 ? '' : 's'}</span>
            <span class="sl-data-grid__toolbar-spacer"></span>
            <sl-button size="small" variant="ghost" @click=${() => this.discardEdits()}>Discard</sl-button>
            <sl-button size="small" variant="solid" tone="accent" @click=${() => this.commitEdits()}>Commit</sl-button>
          </div>`
        : nothing}`;
  }

  private renderChooser(): TemplateResult {
    const cols = this.effectiveColumns;
    return html`<sl-popover placement="bottom-end" .open=${this.chooserOpen}
      @sl-open-changed=${(e: CustomEvent<{ open: boolean }>) => { this.chooserOpen = e.detail.open; this.requestUpdate(); }}>
      <sl-button slot="anchor" size="small" variant="ghost" start-icon="columns">Columns</sl-button>
      ${this.chooserOpen
        ? html`<div class="sl-data-grid__panel" part="column-chooser">
            <span class="sl-data-grid__panel-title">Columns</span>
            ${this.layout.order.map((field) => {
              const c = cols.find((x) => x.field === field)!;
              const cs = columnState(this.state, field);
              const hidden = cs?.hidden ?? col.hidden(c);
              const pin = cs?.pinned ?? col.pinned(c);
              return html`<div class="sl-data-grid__chooser-item">
                <label class="sl-checkbox sl-tone-accent sl-checkbox--small sl-checkbox--label-end">
                  <input type="checkbox" class="sl-checkbox__input" .checked=${!hidden} ?disabled=${c.hideable === false}
                    @change=${(e: Event) => this.commitState(setColumnHidden(this.state, field, !(e.target as HTMLInputElement).checked))} />
                  <span class="sl-checkbox__text"><span class="sl-checkbox__label">${col.title(c)}</span></span>
                </label>
                ${pin !== 'none' ? html`<span class="sl-data-grid__muted" title="Pinned">${renderIcon('pin')}</span>` : nothing}
              </div>`;
            })}
            <div class="sl-data-grid__panel-actions">
              <sl-button size="small" variant="ghost" @click=${() => this.commitState({ ...this.state, columns: [] }, 'Column layout reset')}>Reset layout</sl-button>
            </div>
          </div>`
        : nothing}
    </sl-popover>`;
  }

  /** Tri-state of the select-all checkbox; memoised because it scans every matching key (100k+) and scrolling re-renders. */
  private selectAllState(): 'none' | 'some' | 'all' {
    if (this.cache) return this.selection.allMatching ? (this.selection.excludedKeys.size ? 'some' : 'all') : this.selection.count ? 'some' : 'none';
    const keys = this.result?.itemKeys ?? this.visibleKeys();
    if (this.headerStateMemo?.keys !== keys) this.headerStateMemo = { keys, value: this.selection.headerState(keys) };
    return this.headerStateMemo.value;
  }
  private headerStateMemo: { keys: readonly string[]; value: 'none' | 'some' | 'all' } | null = null;

  private renderHeader(): TemplateResult {
    const cols = this.visibleColumnSet();
    const headerState = this.selectAllState();
    const groups = this.headerRowCount === 2 ? this.headerGroups(cols.cells) : null;

    return html`<div class="sl-data-grid__header" role="rowgroup" part="header"
      style=${styleMap({ width: `${this.layout.totalWidth + this.leadingWidth}px` })}>
      ${groups
        ? html`<div class="sl-data-grid__header-groups" role="row" aria-rowindex="1">
            ${this.leadingWidth ? html`<span class="sl-data-grid__header-group" role="presentation" style="width:${this.leadingWidth}px"></span>` : nothing}
            ${groups.map((g) => html`<span class="sl-data-grid__header-group" role="columnheader" style=${styleMap({ width: `${g.width}px` })}>${g.label}</span>`)}
          </div>`
        : nothing}
      <div class="sl-data-grid__header-row" role="row" aria-rowindex=${this.headerRowCount}
        @pointermove=${(e: PointerEvent) => { this.onHeaderPointerMove(e); this.onResizeMove(e); }}
        @pointerup=${() => { this.onHeaderPointerUp(); this.onResizeUp(); }}
        @pointercancel=${() => { this.drag = null; this.resizing = null; this.requestUpdate(); }}>
        ${this.selectionMode === 'multi'
          ? html`<div class="sl-data-grid__header-cell sl-data-grid__header-cell--select is-pinned-start ${this.layout.columns.some((c) => c.pin === 'start') || this.hasDetail ? '' : 'is-pinned-edge-start'}"
              role="columnheader" aria-colindex="1" style="width:${SELECT_WIDTH}px;left:0">
              <label class="sl-checkbox sl-tone-accent sl-data-grid__check">
                <input type="checkbox" class=${classMap({ 'sl-checkbox__input': true, 'is-indeterminate': headerState === 'some' })}
                  aria-label="Select all rows" .checked=${headerState === 'all'} .indeterminate=${headerState === 'some'}
                  aria-checked=${headerState === 'some' ? 'mixed' : headerState === 'all' ? 'true' : 'false'}
                  @change=${(e: Event) => { e.preventDefault(); this.toggleHeaderSelection(); }} />
              </label>
            </div>`
          : nothing}
        ${this.hasDetail
          ? html`<div class="sl-data-grid__header-cell sl-data-grid__header-cell--select is-pinned-start" role="columnheader"
              style="width:${DETAIL_TOGGLE_WIDTH}px;left:${this.selectionMode === 'multi' ? SELECT_WIDTH : 0}px"><span class="sl-data-grid__sr-only">Details</span></div>`
          : nothing}
        ${cols.cells.map((c) => (c === null ? html`<span class="sl-data-grid__spacer" style="width:${cols.spacerBefore}px"></span>` : this.renderHeaderCell(c)))}
        ${cols.spacerAfter ? html`<span class="sl-data-grid__spacer" style="width:${cols.spacerAfter}px"></span>` : nothing}
      </div>
    </div>`;
  }

  private headerGroups(cells: (ResolvedColumn<Item> | null)[]): { label: string; width: number }[] {
    const out: { label: string; width: number }[] = [];
    for (const c of cells) {
      const width = c ? c.width : 0;
      const label = c ? (c.column as DataGridColumn).headerGroup ?? '' : '';
      const last = out[out.length - 1];
      if (last && last.label === label) last.width += width;
      else out.push({ label, width });
    }
    return out;
  }

  /** Visible columns: pinned + scrolling range (column virtualisation for wide grids). */
  private visibleColumnSet(): { cells: (ResolvedColumn<Item> | null)[]; spacerBefore: number; spacerAfter: number } {
    const all = this.layout.columns;
    if (all.length <= 40) return { cells: [...all], spacerBefore: 0, spacerAfter: 0 };
    const [first, last] = scrollingColumnRange(this.layout, Math.max(0, this.vLeft - this.leadingWidth), this.viewportWidth || 960, 300);
    const start = all.filter((c) => c.pin === 'start');
    const end = all.filter((c) => c.pin === 'end');
    const scrolling = all.filter((c) => c.pin === 'none');
    if (first < 0) return { cells: [...start, ...end], spacerBefore: 0, spacerAfter: 0 };
    const firstScroll = all[first]!;
    const lastScroll = all[last]!;
    const scrollStart = scrolling[0]?.left ?? 0;
    const scrollEnd = scrolling.length ? scrolling[scrolling.length - 1]!.left + scrolling[scrolling.length - 1]!.width : 0;
    const visible = all.slice(first, last + 1);
    const before = firstScroll.left - scrollStart;
    const after = scrollEnd - (lastScroll.left + lastScroll.width);
    return { cells: [...start, ...(before > 0 ? [null] : []), ...visible, ...end], spacerBefore: before, spacerAfter: after };
  }

  private pinStyle(c: ResolvedColumn<Item>): Record<string, string> {
    const style: Record<string, string> = { width: `${c.width}px` };
    if (c.pin === 'start') style.left = `${c.stickyOffset + this.leadingWidth}px`;
    if (c.pin === 'end') style.right = `${c.stickyOffset}px`;
    return style;
  }

  private pinClasses(c: ResolvedColumn<Item>): Record<string, boolean> {
    const cols = this.layout.columns;
    const lastStart = [...cols].reverse().find((x) => x.pin === 'start');
    const firstEnd = cols.find((x) => x.pin === 'end');
    return {
      'is-pinned-start': c.pin === 'start',
      'is-pinned-end': c.pin === 'end',
      'is-pinned-edge-start': c === lastStart,
      'is-pinned-edge-end': c === firstEnd,
    };
  }

  private renderHeaderCell(c: ResolvedColumn<Item>): TemplateResult {
    const column = c.column as DataGridColumn;
    const dir = sortOf(this.state, c.field);
    const sortIndex = this.state.sorts.findIndex((s) => s.field === c.field);
    const filtered = this.state.filters.some((f) => f.field === c.field);
    const sortable = col.sortable(column);
    const filterable = column.filterable !== false && col.type(column) !== 'actions' && col.type(column) !== 'sparkline';
    // Progress cells fill from the start (bar, then value), so their title starts there too; an end-aligned title sits
    // where a pinned-end section or the viewport edge clips it first while the bar is still visible.
    const end = col.align(column) === 'end' && col.type(column) !== 'progress';
    const title = col.title(column);
    const drag = this.drag?.moved ? this.drag : null;
    const grouped = this.state.groupBy.includes(c.field);
    const cls = {
      'sl-data-grid__header-cell': true,
      'sl-data-grid__header-cell--end': end,
      'is-sorted': !!dir,
      'is-filtered': filtered,
      'is-dragging': drag?.field === c.field,
      'is-drop-before': drag?.over === c.field && !drag.after && drag.field !== c.field,
      'is-drop-after': drag?.over === c.field && drag.after && drag.field !== c.field,
      ...this.pinClasses(c),
    };
    return html`<div class=${classMap(cls)} role="columnheader" part="header-cell" data-field=${c.field}
      aria-colindex=${c.index + 1 + (this.selectionMode === 'multi' ? 1 : 0) + (this.hasDetail ? 1 : 0)}
      aria-sort=${dir ?? (sortable ? 'none' : nothing)}
      title=${column.description ?? nothing}
      style=${styleMap(this.pinStyle(c))}>
      <button class="sl-data-grid__sort" type="button" ?disabled=${!sortable && column.reorderable === false}
        @click=${(e: MouseEvent) => this.onSortClick(e, c)}
        @pointerdown=${(e: PointerEvent) => this.onHeaderPointerDown(e, c)}
        @keydown=${(e: KeyboardEvent) => {
          if (e.altKey && (e.key === 'ArrowLeft' || e.key === 'ArrowRight') && column.reorderable !== false) {
            e.preventDefault();
            e.stopPropagation();
            const order = this.layout.order;
            this.commitState(moveColumn(this.state, order, c.field, order.indexOf(c.field) + (e.key === 'ArrowLeft' ? -1 : 1)), `${title} moved`);
            this.updateComplete.then(() => this.renderRoot.querySelector<HTMLElement>(`.sl-data-grid__header-cell[data-field="${CSS.escape(c.field)}"] .sl-data-grid__sort`)?.focus());
          }
        }}
        aria-label=${`${title}${dir ? `, sorted ${dir}` : ''}${sortable ? '. Click to sort, Shift+click to add a sort' : ''}`}>
        <span class="sl-data-grid__title">${title}</span>
        ${sortable
          ? html`<span class="sl-data-grid__sort-icon" aria-hidden="true">${renderIcon(dir === 'descending' ? 'arrow-down' : 'arrow-up')}
              ${dir && this.state.sorts.length > 1 ? html`<span class="sl-data-grid__sort-order">${sortIndex + 1}</span>` : nothing}</span>`
          : nothing}
      </button>
      <span class="sl-data-grid__header-actions">
      ${filterable
        ? html`<sl-popover placement="bottom-start" modal .open=${this.openFilter === c.field}
            @sl-open-changed=${(e: CustomEvent<{ open: boolean }>) => {
              if (e.target !== e.currentTarget) return;
              if (e.detail.open) this.openFilterFor(c.field);
              else if (this.openFilter === c.field) { this.openFilter = null; this.filterDraft = null; this.requestUpdate(); }
            }}>
            <button slot="anchor" type="button" class=${classMap({ 'sl-data-grid__header-button': true, 'is-active': filtered })}
              aria-label=${`Filter ${title}`}>${renderIcon('filter')}</button>
            ${this.openFilter === c.field && this.filterDraft
              ? renderFilterEditor(column, this.filterDraft, filterKind(column) === 'enum' ? this.filterOptions(column) : [],
                  (patch) => { this.filterDraft = { ...this.filterDraft!, ...patch }; this.requestUpdate(); },
                  () => this.applyFilter(column),
                  () => { this.filterDraft = draftFrom(column, undefined); this.applyFilter(column); })
              : nothing}
          </sl-popover>`
        : nothing}
      <sl-menu placement="bottom-end" @sl-select=${(e: CustomEvent<{ item: HTMLElement }>) => this.onMenuSelect(e, c)}>
        <button slot="trigger" type="button" class="sl-data-grid__header-button" aria-label=${`${title} column menu`}>${renderIcon('more-horizontal')}</button>
        ${sortable
          ? html`<sl-menu-item data-action="asc" label="Sort ascending" icon="arrow-up"></sl-menu-item>
              <sl-menu-item data-action="desc" label="Sort descending" icon="arrow-down"></sl-menu-item>
              ${dir ? html`<sl-menu-item data-action="unsort" label="Clear sort" icon="x"></sl-menu-item>` : nothing}
              <sl-menu-item separator></sl-menu-item>`
          : nothing}
        ${filterable ? html`<sl-menu-item data-action="filter" label="Filter…" icon="filter"></sl-menu-item>` : nothing}
        ${this.groupable && !this.childrenSelector
          ? grouped
            ? html`<sl-menu-item data-action="ungroup" label="Ungroup" icon="group"></sl-menu-item>`
            : html`<sl-menu-item data-action="group" label=${`Group by ${title}`} icon="group"></sl-menu-item>`
          : nothing}
        <sl-menu-item separator></sl-menu-item>
        ${c.pin !== 'start' ? html`<sl-menu-item data-action="pin-start" label="Pin to start" icon="pin"></sl-menu-item>` : nothing}
        ${c.pin !== 'end' ? html`<sl-menu-item data-action="pin-end" label="Pin to end" icon="pin"></sl-menu-item>` : nothing}
        ${c.pin !== 'none' ? html`<sl-menu-item data-action="unpin" label="Unpin" icon="x"></sl-menu-item>` : nothing}
        <sl-menu-item data-action="autosize" label="Autosize column" icon="arrow-right"></sl-menu-item>
        <sl-menu-item separator></sl-menu-item>
        ${column.hideable !== false ? html`<sl-menu-item data-action="hide" label="Hide column" icon="eye-off"></sl-menu-item>` : nothing}
        <sl-menu-item data-action="columns" label="Columns…" icon="columns"></sl-menu-item>
      </sl-menu>
      </span>
      ${column.resizable !== false
        ? html`<span class=${classMap({ 'sl-data-grid__resize': true, 'is-resizing': this.resizing?.field === c.field })}
            role="separator" aria-orientation="vertical" aria-label=${`Resize ${title}`}
            @pointerdown=${(e: PointerEvent) => this.onResizeDown(e, c)}
            @dblclick=${() => this.autosize(c)}></span>`
        : nothing}
    </div>`;
  }

  private renderRows(): TemplateResult {
    const w = this.window();
    this.renderedWindow = `${w.first}:${w.count}:`;
    const visible = this.slots.slice(w.first, w.first + w.count);
    const cols = this.visibleColumnSet();
    return html`<div class="sl-data-grid__rows" role="rowgroup" part="body"
      style=${styleMap({ transform: `translateY(${w.offsetTop + this.headerHeight}px)`, width: `${this.layout.totalWidth + this.leadingWidth}px` })}>
      ${visible.map((s, i) => this.renderRow(s, w.first + i, cols))}
    </div>`;
  }

  private rowAriaIndex(row: number): number {
    const base = this.pagination === 'pages' ? this.state.pageIndex * this.state.pageSize : 0;
    return base + row + 1 + this.headerRowCount;
  }

  private renderRow(slot: RowSlot, row: number, cols: ReturnType<SlDataGrid['visibleColumnSet']>): TemplateResult {
    if (slot.kind === 'group') return this.renderGroupRow(slot, row);
    if (slot.kind === 'detail') return this.renderDetailRow(slot, row);
    const item = slot.item;
    const selected = slot.kind === 'data' && this.selection.isSelected(slot.key);
    const tone = item !== undefined ? this.rowTone?.(item) : null;
    const classes = {
      'sl-data-grid__row': true,
      'is-odd': row % 2 === 1,
      'is-selected': selected,
      'is-toned': !!tone,
      [`sl-tone-${tone}`]: !!tone,
      'is-flash': this.flashing.has(slot.key),
      'sl-data-grid__row--skeleton': slot.kind === 'skeleton',
    };
    const view = slot.view;
    const expanded = view?.hasChildren ? view.expanded : undefined;
    return html`<div class=${classMap(classes)} role="row" part="row" data-key=${slot.key}
      aria-rowindex=${this.rowAriaIndex(row)}
      aria-selected=${this.selectionMode !== 'none' && slot.kind === 'data' ? String(selected) : nothing}
      aria-level=${this.childrenSelector ? slot.depth + 1 : nothing}
      aria-expanded=${expanded === undefined ? nothing : String(expanded)}
      @click=${(e: MouseEvent) => this.onRowClick(e, slot, row, -1)}>
      ${this.selectionMode === 'multi'
        ? html`<div class="sl-data-grid__cell sl-data-grid__cell--select is-pinned-start ${this.layout.columns.some((c) => c.pin === 'start') || this.hasDetail ? '' : 'is-pinned-edge-start'}"
            role="gridcell" style="width:${SELECT_WIDTH}px;left:0">
            ${slot.kind === 'data'
              ? html`<label class="sl-checkbox sl-tone-accent sl-data-grid__check" @click=${(e: Event) => e.stopPropagation()}>
                  <input type="checkbox" class="sl-checkbox__input" tabindex="-1" .checked=${selected}
                    aria-label=${`Select row ${this.rowAriaIndex(row) - this.headerRowCount}`}
                    @click=${(e: MouseEvent) => {
                      e.stopPropagation();
                      if (e.shiftKey) this.selection.click(slot.key, this.visibleKeys(), false, true);
                      else this.selection.toggle(slot.key);
                      this.active = { row, column: this.active.column };
                    }} />
                </label>`
              : nothing}
          </div>`
        : nothing}
      ${this.hasDetail
        ? html`<div class="sl-data-grid__cell sl-data-grid__cell--select is-pinned-start" role="gridcell"
            style="width:${DETAIL_TOGGLE_WIDTH}px;left:${this.selectionMode === 'multi' ? SELECT_WIDTH : 0}px">
            ${slot.kind === 'data'
              ? html`<button class="sl-data-grid__expander" type="button" tabindex="-1"
                  aria-expanded=${String(this.state.expandedDetails.includes(slot.key))} aria-label="Toggle details"
                  @click=${(e: Event) => { e.stopPropagation(); this.commitState(toggleDetail(this.state, slot.key)); }}>${renderIcon('chevron-right')}</button>`
              : nothing}
          </div>`
        : nothing}
      ${cols.cells.map((c) => (c === null ? html`<span class="sl-data-grid__spacer" style="width:${cols.spacerBefore}px"></span>` : this.renderCell(slot, row, c)))}
      ${cols.spacerAfter ? html`<span class="sl-data-grid__spacer" style="width:${cols.spacerAfter}px"></span>` : nothing}
    </div>`;
  }

  private renderCell(slot: RowSlot, row: number, c: ResolvedColumn<Item>): TemplateResult {
    const column = c.column as DataGridColumn;
    const type = col.type(column);
    const active = this.active.row === row && this.active.column === c.index;
    const editing = active && this.edit.current !== null && this.edit.current.rowKey === slot.key && this.edit.current.column === column;
    const dirty = slot.kind === 'data' && this.edit.hasPending(slot.key, c.field);
    const align = col.align(column);
    const classes = {
      'sl-data-grid__cell': true,
      [`sl-data-grid__cell--${type}`]: true,
      'sl-data-grid__cell--end': align === 'end',
      'sl-data-grid__cell--center': align === 'center',
      'is-active': active,
      'is-editing': editing,
      'is-dirty': dirty,
      'is-invalid': editing && !!this.edit.current?.error,
      ...this.pinClasses(c),
    };
    let content: unknown = nothing;
    if (slot.kind === 'skeleton') content = html`<span class="sl-data-grid__skeleton"></span>`;
    else if (editing) content = this.renderEditor(column);
    else if (slot.item !== undefined) {
      const value = this.edit.getValue(slot.item, slot.key, column);
      const text = value === null || value === undefined ? '' : displayText(column, value);
      const ctx: DataGridCellContext<Item> = { item: slot.item, value, text, column, rowKey: slot.key, rowIndex: row };
      content = renderCellContent(column, value, slot.item, text, ctx);
    }
    const tree = this.childrenSelector && c.index === 0 && slot.view;
    return html`<div class=${classMap(classes)} role="gridcell" part="cell" id=${this.cellId(row, c.index)} data-field=${c.field}
      aria-colindex=${c.index + 1 + (this.selectionMode === 'multi' ? 1 : 0) + (this.hasDetail ? 1 : 0)}
      aria-readonly=${this.editMode !== 'none' && !column.editable ? 'true' : nothing}
      style=${styleMap(this.pinStyle(c))}
      @click=${(e: MouseEvent) => { e.stopPropagation(); this.onRowClick(e, slot, row, c.index); }}
      @dblclick=${() => this.onRowDblClick(slot, row, c.index)}>
      ${tree
        ? html`<span style="width:${slot.depth * 20}px;flex:none"></span>${slot.view!.hasChildren
            ? html`<button class="sl-data-grid__expander" type="button" tabindex="-1" aria-expanded=${String(slot.view!.expanded)}
                aria-label=${slot.view!.expanded ? 'Collapse' : 'Expand'}
                @click=${(e: Event) => { e.stopPropagation(); this.toggleSlot(row); }}>${renderIcon('chevron-right')}</button>`
            : html`<span class="sl-data-grid__expander-spacer"></span>`}`
        : nothing}
      ${content}
      ${editing && this.edit.current?.error ? html`<span class="sl-data-grid__cell-error" role="alert">${this.edit.current.error}</span>` : nothing}
    </div>`;
  }

  private renderEditor(column: DataGridColumn): TemplateResult {
    const e = this.edit.current!;
    const kind = editorKind(column);
    const text = e.draftText ?? editorText(column, e.draft);
    const label = `Edit ${col.title(column)}`;
    if (kind === 'checkbox') {
      return html`<input class="sl-data-grid__editor" type="checkbox" aria-label=${label} .checked=${e.draft === true}
        @change=${this.onEditorInput} @keydown=${this.onEditorKey} @blur=${this.onEditorBlur} style="width:auto" />`;
    }
    if (kind === 'select') {
      const options = column.enumOrder ?? this.filterOptions(column);
      return html`<select class="sl-data-grid__editor" aria-label=${label} @change=${this.onEditorInput} @keydown=${this.onEditorKey} @blur=${this.onEditorBlur}>
        ${options.map((o) => html`<option value=${o} ?selected=${o === text}>${o}</option>`)}
      </select>`;
    }
    return html`<input class="sl-data-grid__editor" aria-label=${label}
      type=${kind === 'date' ? 'date' : 'text'} inputmode=${kind === 'number' ? 'decimal' : nothing}
      .value=${text} aria-invalid=${e.error ? 'true' : 'false'}
      @input=${this.onEditorInput} @keydown=${this.onEditorKey} @blur=${this.onEditorBlur} />`;
  }

  private renderGroupRow(slot: RowSlot, row: number): TemplateResult {
    const v = slot.view!;
    const column = this.effectiveColumns.find((c) => c.field === v.groupField);
    const aggregates = Object.entries(v.aggregates ?? {});
    const active = this.active.row === row;
    return html`<div class="sl-data-grid__row sl-data-grid__row--group" role="row" part="group-row" data-key=${slot.key}
      aria-rowindex=${this.rowAriaIndex(row)} aria-expanded=${String(v.expanded)} aria-level=${v.depth + 1}
      @click=${(e: MouseEvent) => this.onRowClick(e, slot, row, 0)}>
      <div class=${classMap({ 'sl-data-grid__cell': true, 'sl-data-grid__cell--wide': true, 'is-active': active })} role="gridcell"
        id=${this.cellId(row, this.active.column)} aria-colspan=${this.layout.columns.length}
        style="position:sticky;left:0;flex:none;width:${this.viewportWidth || 960}px;padding-left:${8 + v.depth * 20}px">
        <button class="sl-data-grid__expander" type="button" tabindex="-1" aria-expanded=${String(v.expanded)}
          aria-label=${v.expanded ? 'Collapse group' : 'Expand group'} @click=${(e: Event) => { e.stopPropagation(); this.toggleSlot(row); }}>${renderIcon('chevron-right')}</button>
        <span class="sl-data-grid__group">
          <span class="sl-data-grid__group-label">${column ? col.title(column) : v.groupField}:</span>
          <span>${v.groupKeyText || '(empty)'}</span>
          <span class="sl-data-grid__group-count">${v.rowCount.toLocaleString()}</span>
          ${aggregates.map(([field, value]) => {
            const c = this.effectiveColumns.find((x) => x.field === field);
            if (!c) return nothing;
            const agg = col.aggregate(c);
            return html`<span class="sl-data-grid__aggregate"><span class="sl-data-grid__aggregate-label">${aggregateLabel[agg] ?? agg} ${col.title(c)}</span>${aggregateText(c, agg, value)}</span>`;
          })}
        </span>
      </div>
    </div>`;
  }

  private renderDetailRow(slot: RowSlot, row: number): TemplateResult {
    let content: unknown = nothing;
    if (this.rowDetail && slot.item !== undefined) content = this.rowDetail(slot.item);
    else if (this.detailTemplate && slot.item !== undefined) {
      const frag = this.detailTemplate.content.cloneNode(true) as DocumentFragment;
      for (const el of frag.querySelectorAll<HTMLElement>('[data-field]')) {
        const c = this.effectiveColumns.find((x) => x.field === el.dataset.field);
        const value = c ? cellValue(c, slot.item) : (slot.item as Record<string, unknown>)?.[el.dataset.field!];
        el.textContent = c ? displayText(c, value) : String(value ?? '');
      }
      content = frag;
    }
    return html`<div class="sl-data-grid__row sl-data-grid__row--detail" role="row" data-key=${slot.key}
      aria-rowindex=${this.rowAriaIndex(row)} style="height:${this.detailHeight}px">
      <div class="sl-data-grid__cell sl-data-grid__cell--wide" role="gridcell" aria-colspan=${this.layout.columns.length}
        id=${this.cellId(row, this.active.column)} style="position:sticky;left:0;flex:none;width:${this.viewportWidth || 960}px;height:100%;padding:0">
        <div class="sl-data-grid__detail">${content}</div>
      </div>
    </div>`;
  }

  private renderFooter(): TemplateResult {
    const totals = this.result?.totals ?? {};
    const cols = this.visibleColumnSet();
    return html`<div class="sl-data-grid__row sl-data-grid__row--footer" role="row" part="footer" aria-rowindex=${this.rowAriaIndex(this.slots.length)}
      style=${styleMap({ width: `${this.layout.totalWidth + this.leadingWidth}px`, position: 'sticky' })}>
      ${this.leadingWidth ? html`<div class="sl-data-grid__cell is-pinned-start" role="gridcell" style="width:${this.leadingWidth}px;left:0"></div>` : nothing}
      ${cols.cells.map((c) => {
        if (c === null) return html`<span class="sl-data-grid__spacer" style="width:${cols.spacerBefore}px"></span>`;
        const agg = col.aggregate(c.column);
        const value = totals[c.field];
        const cls = { 'sl-data-grid__cell': true, 'sl-data-grid__cell--end': col.align(c.column) === 'end', ...this.pinClasses(c) };
        return html`<div class=${classMap(cls)} role="gridcell" data-field=${c.field} style=${styleMap(this.pinStyle(c))}>
          ${agg !== 'none' ? html`<span class="sl-data-grid__aggregate-label">${aggregateLabel[agg] ?? agg}</span>${aggregateText(c.column as DataGridColumn, agg, value)}` : nothing}
        </div>`;
      })}
      ${cols.spacerAfter ? html`<span class="sl-data-grid__spacer" style="width:${cols.spacerAfter}px"></span>` : nothing}
    </div>`;
  }

  private renderEmpty(): TemplateResult {
    const filtered = (this.result?.totalCount ?? 0) > 0;
    return html`<div class="sl-data-grid__empty" part="empty">
      <slot name="empty-content">
        <div>
          <div class="sl-data-grid__empty-title">${filtered ? 'No matching rows' : 'No data'}</div>
          ${filtered
            ? html`<div>Try a different search, or <button class="sl-data-grid__link" type="button" @click=${() => this.commitState(clearFilters(this.state), 'Filters cleared')}>clear the filters</button>.</div>`
            : nothing}
        </div>
      </slot>
    </div>`;
  }

  private renderPager(): TemplateResult {
    const total = this.cache ? this.cache.totalCount ?? 0 : this.result?.viewRowCount ?? 0;
    const pages = this.cache ? Math.max(1, Math.ceil(total / Math.max(1, this.state.pageSize))) : this.result?.pageCount ?? 1;
    const page = (this.cache ? this.state.pageIndex : this.result?.pageIndex ?? 0) + 1;
    return html`<div class="sl-data-grid__pager" part="pager">
      <sl-pagination size="small" .page=${page} .pageCount=${pages} .pageSize=${this.state.pageSize} .pageSizes=${[25, 50, 100, 250]} .totalCount=${total}
        @sl-page-changed=${(e: CustomEvent<{ page: number }>) => { if (e.detail.page !== page) this.commitState(setPage(this.state, e.detail.page - 1)); }}
        @sl-page-size-changed=${(e: CustomEvent<{ pageSize: number }>) => this.commitState(setPageSize(this.state, e.detail.pageSize))}></sl-pagination>
    </div>`;
  }
}

function sameList(a: readonly string[] | undefined, b: readonly string[] | undefined): boolean {
  if (a === b) return true;
  if (!a || !b || a.length !== b.length) return false;
  return a.every((x, i) => x === b[i]);
}

/** Declarative column: any element with slot="columns" and a field attribute (e.g. <sl-grid-column>, <div>). */
function columnFromElement(el: Element): DataGridColumn {
  const a = (n: string) => el.getAttribute(n) ?? undefined;
  const num = (n: string) => (el.hasAttribute(n) ? Number(el.getAttribute(n)) : undefined);
  const bool = (n: string) => (el.hasAttribute(n) ? el.getAttribute(n) !== 'false' : undefined);
  const c: DataGridColumn = { field: a('field')! };
  const set = <K extends keyof DataGridColumn>(k: K, v: DataGridColumn[K] | undefined) => { if (v !== undefined) c[k] = v; };
  set('title', a('title'));
  set('type', a('type') as DataGridColumn['type']);
  set('width', num('width'));
  set('minWidth', num('min-width'));
  set('maxWidth', num('max-width'));
  set('flex', num('flex'));
  set('pinned', a('pinned') as GridPin | undefined);
  set('align', a('align') as DataGridColumn['align']);
  set('format', a('format'));
  set('aggregate', a('aggregate') as DataGridColumn['aggregate']);
  set('sortable', bool('sortable'));
  set('filterable', bool('filterable'));
  set('resizable', bool('resizable'));
  set('hidden', bool('hidden'));
  set('headerGroup', a('header-group'));
  set('description', a('description'));
  return c;
}
