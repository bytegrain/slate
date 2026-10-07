/**
 * Grid view state — port of Slate.Core `GridState`. Plain immutable objects with pure transition functions;
 * the JSON shape (and therefore persisted state) is identical to the C# engine's.
 */
import { roundTo, type FilterOperator, type GridPin, type SortDirection } from './values';

export type FilterValue = string | number | boolean | null;

export interface GridSort {
  readonly field: string;
  readonly direction: SortDirection;
}

export interface GridFilter {
  readonly field: string;
  readonly operator: FilterOperator;
  readonly value?: FilterValue;
  readonly value2?: FilterValue;
  readonly values?: readonly FilterValue[];
}

export interface GridColumnState {
  readonly field: string;
  readonly width?: number;
  readonly hidden?: boolean;
  readonly pinned?: GridPin;
}

export interface GridState {
  /**
   * Explicit display order, set only by moveColumn. Empty = definition order; unlisted fields follow in
   * definition order. Separate from `columns` so resizing, pinning or hiding never moves a column.
   */
  readonly order: readonly string[];
  /** Per-column overrides (width, visibility, pinning); list order carries no meaning. */
  readonly columns: readonly GridColumnState[];
  readonly sorts: readonly GridSort[];
  readonly filters: readonly GridFilter[];
  readonly quickFilter: string;
  /** Group-by fields, outermost first (ignored for tree data). */
  readonly groupBy: readonly string[];
  readonly groupsCollapsedByDefault: boolean;
  /** Group ids (and "row:{key}" tree node ids) whose expansion differs from the default. */
  readonly toggledGroups: readonly string[];
  readonly expandedDetails: readonly string[];
  readonly pageIndex: number;
  readonly pageSize: number;
}

export const emptyGridState: GridState = Object.freeze({
  order: [],
  columns: [],
  sorts: [],
  filters: [],
  quickFilter: '',
  groupBy: [],
  groupsCollapsedByDefault: false,
  toggledGroups: [],
  expandedDetails: [],
  pageIndex: 0,
  pageSize: 50,
});

export function createGridState(partial: Partial<GridState> = {}): GridState {
  return { ...emptyGridState, ...partial };
}

export const isCollapsed = (s: GridState, groupId: string): boolean => s.groupsCollapsedByDefault !== s.toggledGroups.includes(groupId);
export const hasFilters = (s: GridState): boolean => s.filters.length > 0 || s.quickFilter.trim().length > 0;
export const columnState = (s: GridState, field: string): GridColumnState | undefined => s.columns.find((c) => c.field === field);
export const sortOf = (s: GridState, field: string): SortDirection | undefined => s.sorts.find((x) => x.field === field)?.direction;

/** Header click: ascending → descending → none. `additive` (Shift) keeps other sorts. */
export function toggleSort(s: GridState, field: string, additive = false): GridState {
  const current = sortOf(s, field);
  const next: SortDirection | null = current === undefined ? 'ascending' : current === 'ascending' ? 'descending' : null;
  if (!additive) return { ...s, sorts: next ? [{ field, direction: next }] : [], pageIndex: 0 };
  const list = [...s.sorts];
  const i = list.findIndex((x) => x.field === field);
  if (next === null) list.splice(i, 1);
  else if (i >= 0) list[i] = { field, direction: next };
  else list.push({ field, direction: next });
  return { ...s, sorts: list, pageIndex: 0 };
}

export const setSorts = (s: GridState, ...sorts: GridSort[]): GridState => ({ ...s, sorts, pageIndex: 0 });

export const setFilter = (s: GridState, filter: GridFilter): GridState =>
  ({ ...s, filters: [...s.filters.filter((f) => f.field !== filter.field), filter], pageIndex: 0 });

export const clearFilter = (s: GridState, field: string): GridState =>
  ({ ...s, filters: s.filters.filter((f) => f.field !== field), pageIndex: 0 });

export const clearFilters = (s: GridState): GridState => ({ ...s, filters: [], quickFilter: '', pageIndex: 0 });

export const setQuickFilter = (s: GridState, text: string): GridState => ({ ...s, quickFilter: text ?? '', pageIndex: 0 });

export function moveColumn(s: GridState, currentOrder: readonly string[], field: string, toIndex: number): GridState {
  const order = [...currentOrder];
  const i = order.indexOf(field);
  if (i < 0) return s;
  order.splice(i, 1);
  order.splice(Math.min(Math.max(toIndex, 0), order.length), 0, field);
  return { ...s, order };
}

function update(s: GridState, field: string, change: (c: GridColumnState) => GridColumnState): GridState {
  const list = [...s.columns];
  const i = list.findIndex((c) => c.field === field);
  if (i >= 0) list[i] = change(list[i]!);
  else list.push(change({ field }));
  return { ...s, columns: list };
}

export const resizeColumn = (s: GridState, field: string, width: number, min = 48, max = 2000): GridState =>
  update(s, field, (c) => ({ ...c, width: roundTo(Math.min(Math.max(width, min), max), 1) }));

export const pinColumn = (s: GridState, field: string, pin: GridPin): GridState => update(s, field, (c) => ({ ...c, pinned: pin }));

export const setColumnHidden = (s: GridState, field: string, hidden: boolean): GridState => update(s, field, (c) => ({ ...c, hidden }));

export const setGroupBy = (s: GridState, ...fields: string[]): GridState => ({ ...s, groupBy: fields, toggledGroups: [], pageIndex: 0 });

export const addGroupBy = (s: GridState, field: string): GridState =>
  s.groupBy.includes(field) ? s : { ...s, groupBy: [...s.groupBy, field], toggledGroups: [], pageIndex: 0 };

export const removeGroupBy = (s: GridState, field: string): GridState =>
  ({ ...s, groupBy: s.groupBy.filter((g) => g !== field), toggledGroups: [], pageIndex: 0 });

export const toggleGroup = (s: GridState, groupId: string): GridState =>
  ({ ...s, toggledGroups: s.toggledGroups.includes(groupId) ? s.toggledGroups.filter((g) => g !== groupId) : [...s.toggledGroups, groupId] });

export const setGroupExpanded = (s: GridState, groupId: string, expanded: boolean): GridState =>
  isCollapsed(s, groupId) === !expanded ? s : toggleGroup(s, groupId);

export const expandAll = (s: GridState): GridState => ({ ...s, groupsCollapsedByDefault: false, toggledGroups: [] });
export const collapseAll = (s: GridState): GridState => ({ ...s, groupsCollapsedByDefault: true, toggledGroups: [] });

export const toggleDetail = (s: GridState, rowKey: string): GridState =>
  ({ ...s, expandedDetails: s.expandedDetails.includes(rowKey) ? s.expandedDetails.filter((k) => k !== rowKey) : [...s.expandedDetails, rowKey] });

export const setPage = (s: GridState, pageIndex: number): GridState => ({ ...s, pageIndex: Math.max(0, pageIndex) });
export const setPageSize = (s: GridState, pageSize: number): GridState => ({ ...s, pageSize: Math.max(1, pageSize), pageIndex: 0 });

/** The JSON object (same shape and key order as C# `GridState.ToJsonNode`). */
export function gridStateToJson(s: GridState): Record<string, unknown> {
  return {
    order: [...s.order],
    columns: s.columns.map((c) => {
      const o: Record<string, unknown> = { field: c.field };
      if (c.width !== undefined) o.width = c.width;
      if (c.hidden !== undefined) o.hidden = c.hidden;
      if (c.pinned !== undefined) o.pinned = c.pinned;
      return o;
    }),
    sorts: s.sorts.map((x) => ({ field: x.field, direction: x.direction })),
    filters: s.filters.map((f) => {
      const o: Record<string, unknown> = { field: f.field, operator: f.operator };
      if (f.value !== undefined && f.value !== null) o.value = f.value;
      if (f.value2 !== undefined && f.value2 !== null) o.value2 = f.value2;
      if (f.values !== undefined) o.values = [...f.values];
      return o;
    }),
    quickFilter: s.quickFilter,
    groupBy: [...s.groupBy],
    groupsCollapsedByDefault: s.groupsCollapsedByDefault,
    toggledGroups: [...s.toggledGroups],
    expandedDetails: [...s.expandedDetails],
    pageIndex: s.pageIndex,
    pageSize: s.pageSize,
  };
}

export const serializeGridState = (s: GridState): string => JSON.stringify(gridStateToJson(s));

const PINS = new Set(['none', 'start', 'end']);
const DIRECTIONS = new Set(['ascending', 'descending']);
const OPERATORS = new Set<string>(['contains', 'equals', 'notEquals', 'startsWith', 'endsWith', 'isEmpty', 'isNotEmpty',
  'lessThan', 'lessThanOrEqual', 'greaterThan', 'greaterThanOrEqual', 'between', 'anyOf']);

function enumValue<T extends string>(set: Set<string>, text: unknown, name: string): T {
  if (typeof text === 'string') {
    for (const v of set) if (v.toLowerCase() === text.toLowerCase()) return v as T;
  }
  throw new Error(`Unknown ${name} '${String(text)}'.`);
}

const prim = (v: unknown): FilterValue => (typeof v === 'string' || typeof v === 'number' || typeof v === 'boolean' ? v : null);

/** Parses persisted state (C# or TS produced). */
export function gridStateFromJson(json: string | Record<string, unknown>): GridState {
  const o = (typeof json === 'string' ? JSON.parse(json) : json) as Record<string, unknown>;
  if (!o || typeof o !== 'object' || Array.isArray(o)) throw new Error('Grid state JSON must be an object.');
  const arr = (k: string) => (Array.isArray(o[k]) ? (o[k] as Record<string, unknown>[]) : []);
  return {
    order: Array.isArray(o.order) ? (o.order as unknown[]).map(String) : [],
    columns: arr('columns').map((c) => {
      const out: { field: string; width?: number; hidden?: boolean; pinned?: GridPin } = { field: String(c.field) };
      if (typeof c.width === 'number') out.width = c.width;
      if (typeof c.hidden === 'boolean') out.hidden = c.hidden;
      if (c.pinned !== undefined) out.pinned = enumValue<GridPin>(PINS, c.pinned, 'GridPin');
      return out;
    }),
    sorts: arr('sorts').map((x) => ({ field: String(x.field), direction: enumValue<SortDirection>(DIRECTIONS, x.direction, 'SortDirection') })),
    filters: arr('filters').map((f) => {
      const out: { field: string; operator: FilterOperator; value?: FilterValue; value2?: FilterValue; values?: FilterValue[] } =
        { field: String(f.field), operator: enumValue<FilterOperator>(OPERATORS, f.operator, 'FilterOperator') };
      if (f.value !== undefined && f.value !== null) out.value = prim(f.value);
      if (f.value2 !== undefined && f.value2 !== null) out.value2 = prim(f.value2);
      if (Array.isArray(f.values)) out.values = f.values.map(prim);
      return out;
    }),
    quickFilter: typeof o.quickFilter === 'string' ? o.quickFilter : '',
    groupBy: (Array.isArray(o.groupBy) ? o.groupBy : []).map(String),
    groupsCollapsedByDefault: o.groupsCollapsedByDefault === true,
    toggledGroups: (Array.isArray(o.toggledGroups) ? o.toggledGroups : []).map(String),
    expandedDetails: (Array.isArray(o.expandedDetails) ? o.expandedDetails : []).map(String),
    pageIndex: typeof o.pageIndex === 'number' ? o.pageIndex : 0,
    pageSize: typeof o.pageSize === 'number' ? o.pageSize : 50,
  };
}
