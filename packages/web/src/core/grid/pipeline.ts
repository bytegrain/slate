/**
 * Client-side data pipeline — port of Slate.Core `DataPipeline<T>`:
 * filter → stable multi-sort (nulls last) → groups with aggregates (or tree flattening) → detail rows → paging.
 */
import { columnState, isCollapsed, serializeGridState, sortOf, type FilterValue, type GridFilter, type GridSort, type GridState } from './state';
import {
  cellValue, col, compareNormalized, displayText, keyText, normalize, ordinal, toDouble,
  type GridColumn,
} from './values';

export type GridRowKind = 'data' | 'group' | 'detail';

export interface GridViewRow<T> {
  readonly kind: GridRowKind;
  /** Index within `rows`. */
  readonly index: number;
  readonly depth: number;
  readonly item?: T;
  /** Row key (data), group id (group) or "detail:{key}" (detail). */
  readonly key: string;
  readonly groupId?: string;
  readonly groupField?: string;
  readonly groupKey?: unknown;
  readonly groupKeyText?: string;
  readonly rowCount: number;
  readonly aggregates?: Readonly<Record<string, unknown>>;
  readonly expanded: boolean;
  readonly hasChildren: boolean;
}

export interface GridPipelineResult<T> {
  readonly rows: readonly GridViewRow<T>[];
  /** Filtered, sorted items (ignores collapse and paging). */
  readonly items: readonly T[];
  readonly itemKeys: readonly string[];
  /** Data row keys in view order — the selection model's range order. */
  readonly visibleKeys: readonly string[];
  readonly totalCount: number;
  readonly filteredCount: number;
  readonly totals: Readonly<Record<string, unknown>>;
  readonly viewRowCount: number;
  readonly pageIndex: number;
  readonly pageCount: number;
}

export interface DataPipelineOptions<T> {
  rowKey?: (item: T) => unknown;
  childrenSelector?: (item: T) => Iterable<T> | null | undefined;
  paginate?: boolean;
}

interface SortKey {
  isNull: boolean;
  num: number;
  lower: string | null;
  orig: string | null;
  raw: unknown;
}

const NULL_KEY: SortKey = { isNull: true, num: 0, lower: null, orig: null, raw: null };

/** Filter values as text: strings as-is, numbers shortest round-trip, booleans "true"/"false". */
export function filterText(v: FilterValue | undefined): string {
  if (v === null || v === undefined) return '';
  if (typeof v === 'string') return v;
  if (typeof v === 'boolean') return v ? 'true' : 'false';
  return String(v);
}

const isEmpty = (raw: unknown) => raw === null || raw === undefined || (typeof raw === 'string' && raw.trim().length === 0);
const compareText = (a: string, b: string) => ordinal(a.toLowerCase(), b.toLowerCase());
const TYPED = new Set(['number', 'progress', 'date', 'boolean']);

/** Evaluates one filter against a cell value. */
export function filterMatches<T>(column: GridColumn<T>, raw: unknown, f: GridFilter): boolean {
  switch (f.operator) {
    case 'isEmpty': return isEmpty(raw);
    case 'isNotEmpty': return !isEmpty(raw);
    case 'contains': case 'startsWith': case 'endsWith': {
      if (raw === null || raw === undefined) return false;
      const t = displayText(column, raw).toLowerCase();
      const v = filterText(f.value).toLowerCase();
      return f.operator === 'contains' ? t.includes(v) : f.operator === 'startsWith' ? t.startsWith(v) : t.endsWith(v);
    }
    case 'anyOf': {
      if (raw === null || raw === undefined) return false;
      const t = displayText(column, raw).toLowerCase();
      return (f.values ?? []).some((v) => filterText(v).toLowerCase() === t);
    }
  }

  const type = col.type(column);
  const typed = TYPED.has(type);
  let a: unknown, b: unknown, b2: unknown;
  if (typed) {
    a = normalize(raw, type);
    b = normalize(f.value ?? null, type);
    b2 = normalize(f.value2 ?? null, type);
  } else {
    a = raw === null || raw === undefined ? null : displayText(column, raw);
    b = f.value === null || f.value === undefined ? null : filterText(f.value);
    b2 = f.value2 === null || f.value2 === undefined ? null : filterText(f.value2);
  }
  if (b === null) return true;
  if (a === null) return f.operator === 'notEquals';

  const cmp = (x: unknown, y: unknown) => (typed ? compareNormalized(x, y) : compareText(x as string, y as string));
  const c = cmp(a, b);
  switch (f.operator) {
    case 'equals': return c === 0;
    case 'notEquals': return c !== 0;
    case 'lessThan': return c < 0;
    case 'lessThanOrEqual': return c <= 0;
    case 'greaterThan': return c > 0;
    case 'greaterThanOrEqual': return c >= 0;
    case 'between': {
      if (b2 === null) return c >= 0;
      let lo = b, hi = b2;
      if (cmp(lo, hi) > 0) [lo, hi] = [hi, lo];
      return cmp(a, lo) >= 0 && cmp(a, hi) <= 0;
    }
    default: return true;
  }
}

/** Group id segment: field=keyText with '|' and '\' escaped; nested ids joined with '|'. */
export function groupId(parentId: string | null, field: string, text: string): string {
  const seg = field + '=' + text.replaceAll('\\', '\\\\').replaceAll('|', '\\|');
  return parentId === null ? seg : parentId + '|' + seg;
}

export class DataPipeline<T> {
  readonly columns: readonly GridColumn<T>[];
  readonly options: DataPipelineOptions<T>;
  private readonly byField: Map<string, GridColumn<T>>;
  private cache: { items: readonly T[]; version: number; state: string; result: GridPipelineResult<T> } | null = null;
  private version = 0;

  constructor(columns: readonly GridColumn<T>[], options: DataPipelineOptions<T> = {}) {
    this.columns = columns;
    this.options = options;
    this.byField = new Map(columns.map((c) => [c.field, c] as const));
  }

  /** Call after mutating items in place. */
  invalidate(): void {
    this.version++;
  }

  column(field: string): GridColumn<T> | undefined {
    return this.byField.get(field);
  }

  run(items: readonly T[], state: GridState): GridPipelineResult<T> {
    const key = serializeGridState(state);
    const c = this.cache;
    if (c && c.items === items && c.version === this.version && c.state === key) return c.result;
    const result = this.options.childrenSelector ? this.runTree(items, state) : this.runFlat(items, state);
    this.cache = { items, version: this.version, state: key, result };
    return result;
  }

  // ---- filtering -------------------------------------------------------------------------------------------

  buildPredicate(state: GridState): ((item: T) => boolean) | null {
    const tests: ((item: T) => boolean)[] = [];
    for (const f of state.filters) {
      const column = this.column(f.field);
      if (!column) continue;
      tests.push((item) => filterMatches(column, cellValue(column, item), f));
    }
    const terms = (state.quickFilter ?? '').toLowerCase().split(/\s+/).filter((t) => t.length > 0);
    if (terms.length > 0) {
      const searchable = this.columns.filter((c) => col.searchable(c) && !(columnState(state, c.field)?.hidden ?? col.hidden(c)));
      tests.push((item) => {
        const texts = searchable.map((c) => displayText(c, cellValue(c, item)).toLowerCase());
        return terms.every((term) => texts.some((t) => t.includes(term)));
      });
    }
    if (tests.length === 0) return null;
    return (item) => tests.every((t) => t(item));
  }

  // ---- sorting ---------------------------------------------------------------------------------------------

  private keyOf(column: GridColumn<T>, raw: unknown): SortKey {
    if (raw === null || raw === undefined) return NULL_KEY;
    if (column.comparer) return { isNull: false, num: 0, lower: null, orig: null, raw };
    const n = normalize(raw, col.type(column));
    if (n === null) return NULL_KEY;
    if (typeof n === 'number') return { isNull: false, num: n, lower: null, orig: null, raw };
    if (typeof n === 'boolean') return { isNull: false, num: n ? 1 : 0, lower: null, orig: null, raw };
    let idx = 0;
    const order = column.enumOrder;
    if (col.type(column) === 'enum' && order && order.length > 0) {
      idx = order.length;
      const lower = n.toLowerCase();
      for (let i = 0; i < order.length; i++) if (order[i]!.toLowerCase() === lower) { idx = i; break; }
    }
    return { isNull: false, num: idx, lower: n.toLowerCase(), orig: n, raw };
  }

  private compareKeys(column: GridColumn<T>, a: SortKey, b: SortKey): number {
    if (a.isNull || b.isNull) return a.isNull === b.isNull ? 0 : a.isNull ? 1 : -1;
    if (column.comparer) return Math.sign(column.comparer(a.raw, b.raw));
    if (a.num !== b.num) return a.num < b.num ? -1 : 1;
    if (a.lower === null || b.lower === null) return 0;
    const c = ordinal(a.lower, b.lower);
    return c !== 0 ? c : ordinal(a.orig!, b.orig!);
  }

  /** Stable multi-column sort of `indices` (into `items`); nulls last in both directions. Sorts in place. */
  sort(items: readonly T[], indices: number[], sorts: readonly GridSort[]): void {
    const active = sorts
      .map((s) => ({ c: this.column(s.field), dir: s.direction }))
      .filter((s): s is { c: GridColumn<T>; dir: GridSort['direction'] } => !!s.c && col.sortable(s.c));
    if (active.length === 0 || indices.length < 2) return;

    const keys = active.map(({ c }) => {
      const arr = new Array<SortKey>(items.length);
      for (const i of indices) arr[i] = this.keyOf(c, cellValue(c, items[i]!));
      return arr;
    });

    indices.sort((x, y) => {
      for (let k = 0; k < active.length; k++) {
        const a = keys[k]![x]!, b = keys[k]![y]!;
        if (a.isNull || b.isNull) {
          if (a.isNull && b.isNull) continue;
          return a.isNull ? 1 : -1;
        }
        const c = this.compareKeys(active[k]!.c, a, b);
        if (c !== 0) return active[k]!.dir === 'descending' ? -c : c;
      }
      return x - y;
    });
  }

  // ---- aggregates ------------------------------------------------------------------------------------------

  aggregate(items: readonly T[], indices: Iterable<number>): Record<string, unknown> {
    const cols = this.columns.filter((c) => col.aggregate(c) !== 'none');
    const result: Record<string, unknown> = {};
    if (cols.length === 0) return result;
    const list = [...indices];
    for (const c of cols) result[c.field] = this.aggregateColumn(c, list.map((i) => cellValue(c, items[i]!)));
    return result;
  }

  private aggregateColumn(column: GridColumn<T>, values: unknown[]): unknown {
    switch (col.aggregate(column)) {
      case 'count':
        return values.filter((v) => v !== null && v !== undefined).length;
      case 'sum': case 'avg': {
        let sum = 0, n = 0;
        for (const v of values) {
          const d = toDouble(v);
          if (d === null) continue;
          sum += d; n++;
        }
        return col.aggregate(column) === 'sum' ? sum : n === 0 ? null : sum / n;
      }
      case 'min': case 'max': {
        let best: unknown = null;
        let bestKey: SortKey | null = null;
        for (const v of values) {
          if (v === null || v === undefined) continue;
          const k = this.keyOf(column, v);
          if (k.isNull) continue;
          if (bestKey === null) { best = v; bestKey = k; continue; }
          const c = this.compareKeys(column, k, bestKey);
          if (col.aggregate(column) === 'min' ? c < 0 : c > 0) { best = v; bestKey = k; }
        }
        return best;
      }
      default:
        return null;
    }
  }

  // ---- flat (optionally grouped) ---------------------------------------------------------------------------

  private rowKeyText(item: T, index: number): string {
    const k = this.options.rowKey?.(item);
    return k === null || k === undefined ? '#' + index : keyText(k);
  }

  private runFlat(items: readonly T[], state: GridState): GridPipelineResult<T> {
    const predicate = this.buildPredicate(state);
    const indices: number[] = [];
    for (let i = 0; i < items.length; i++) if (!predicate || predicate(items[i]!)) indices.push(i);
    this.sort(items, indices, state.sorts);

    const keys = new Array<string>(items.length);
    for (const i of indices) keys[i] = this.rowKeyText(items[i]!, i);

    const rows: GridViewRow<T>[] = [];
    const groupFields = state.groupBy.filter((f) => this.column(f));
    if (groupFields.length === 0) {
      for (const i of indices) this.addDataRow(rows, items[i]!, keys[i]!, 0, state);
    } else {
      this.addGroups(rows, items, indices, keys, groupFields, 0, null, state);
    }
    return this.finish(rows, indices.map((i) => items[i]!), indices.map((i) => keys[i]!), items.length, indices.length,
      this.aggregate(items, indices), state);
  }

  private addDataRow(rows: GridViewRow<T>[], item: T, key: string, depth: number, state: GridState, hasChildren = false, expanded = false): void {
    rows.push({ kind: 'data', index: rows.length, depth, item, key, rowCount: 0, expanded, hasChildren });
    if (state.expandedDetails.includes(key)) {
      rows.push({ kind: 'detail', index: rows.length, depth, item, key: 'detail:' + key, rowCount: 0, expanded: false, hasChildren: false });
    }
  }

  private addGroups(rows: GridViewRow<T>[], items: readonly T[], indices: number[], keys: string[], fields: string[], level: number,
    parentId: string | null, state: GridState): void {
    const column = this.column(fields[level]!)!;
    const buckets: { text: string; raw: unknown; key: SortKey; rows: number[] }[] = [];
    const lookup = new Map<string, number>();
    for (const i of indices) {
      const raw = cellValue(column, items[i]!);
      const isNil = raw === null || raw === undefined;
      const text = isNil ? '' : displayText(column, raw);
      const bucketKey = isNil ? '\u0000null' : text;
      let b = lookup.get(bucketKey);
      if (b === undefined) {
        b = buckets.length;
        lookup.set(bucketKey, b);
        buckets.push({ text, raw: isNil ? null : raw, key: this.keyOf(column, raw), rows: [] });
      }
      buckets[b]!.rows.push(i);
    }

    const desc = sortOf(state, column.field) === 'descending';
    const order = buckets.map((_, i) => i).sort((x, y) => {
      const a = buckets[x]!.key, b = buckets[y]!.key;
      if (a.isNull || b.isNull) return a.isNull === b.isNull ? x - y : a.isNull ? 1 : -1;
      const c = this.compareKeys(column, a, b);
      if (c !== 0) return desc ? -c : c;
      return x - y;
    });

    for (const bi of order) {
      const bucket = buckets[bi]!;
      const id = groupId(parentId, column.field, bucket.text);
      const expanded = !isCollapsed(state, id);
      rows.push({
        kind: 'group', index: rows.length, depth: level, key: id, groupId: id, groupField: column.field,
        groupKey: bucket.raw, groupKeyText: bucket.text, rowCount: bucket.rows.length,
        aggregates: this.aggregate(items, bucket.rows), expanded, hasChildren: false,
      });
      if (!expanded) continue;
      if (level + 1 < fields.length) this.addGroups(rows, items, bucket.rows, keys, fields, level + 1, id, state);
      else for (const i of bucket.rows) this.addDataRow(rows, items[i]!, keys[i]!, level + 1, state);
    }
  }

  // ---- tree ------------------------------------------------------------------------------------------------

  private runTree(items: readonly T[], state: GridState): GridPipelineResult<T> {
    interface Node { item: T; key: string; matches: boolean; children: Node[] }
    const predicate = this.buildPredicate(state);
    const filtering = predicate !== null;
    let total = 0;

    const build = (source: Iterable<T>, prefix: string): Node[] => {
      const kept: Node[] = [];
      let order = 0;
      for (const item of source) {
        total++;
        const k = this.options.rowKey?.(item);
        const key = k === null || k === undefined ? prefix + order : keyText(k);
        order++;
        const node: Node = { item, key, matches: !predicate || predicate(item), children: [] };
        node.children = build(this.options.childrenSelector!(item) ?? [], key.startsWith('#') ? key + '/' : '#' + key + '/');
        if (node.matches || node.children.length > 0) kept.push(node);
      }
      if (kept.length > 1 && state.sorts.length > 0) {
        const its = kept.map((n) => n.item);
        const idx = kept.map((_, i) => i);
        this.sort(its, idx, state.sorts);
        const sorted = idx.map((i) => kept[i]!);
        kept.length = 0;
        kept.push(...sorted);
      }
      return kept;
    };

    const roots = build(items, '#');
    const rows: GridViewRow<T>[] = [];
    const flat: Node[] = [];
    const walk = (nodes: Node[], depth: number, visible: boolean) => {
      for (const n of nodes) {
        flat.push(n);
        const hasChildren = n.children.length > 0;
        const expanded = hasChildren && (filtering ? true : !isCollapsed(state, 'row:' + n.key));
        if (visible) this.addDataRow(rows, n.item, n.key, depth, state, hasChildren, expanded);
        walk(n.children, depth + 1, visible && expanded);
      }
    };
    walk(roots, 0, true);
    const flatItems = flat.map((n) => n.item);
    return this.finish(rows, flatItems, flat.map((n) => n.key), total, flat.length,
      this.aggregate(flatItems, flatItems.map((_, i) => i)), state);
  }

  // ---- paging & result -------------------------------------------------------------------------------------

  private finish(rows: GridViewRow<T>[], items: readonly T[], keys: readonly string[], total: number, filtered: number,
    totals: Record<string, unknown>, state: GridState): GridPipelineResult<T> {
    const viewCount = rows.length;
    let pageCount = 1, pageIndex = 0;
    let page: readonly GridViewRow<T>[] = rows;
    if (this.options.paginate) {
      const size = Math.max(1, state.pageSize);
      pageCount = Math.max(1, Math.ceil(viewCount / size));
      pageIndex = Math.min(Math.max(state.pageIndex, 0), pageCount - 1);
      page = rows.slice(pageIndex * size, pageIndex * size + size).map((r, i) => (r.index === i ? r : { ...r, index: i }));
    }
    return {
      rows: page,
      items,
      itemKeys: keys,
      visibleKeys: page.filter((r) => r.kind === 'data').map((r) => r.key),
      totalCount: total,
      filteredCount: filtered,
      totals,
      viewRowCount: viewCount,
      pageIndex,
      pageCount,
    };
  }
}
