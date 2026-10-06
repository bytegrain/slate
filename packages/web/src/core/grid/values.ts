/**
 * Data grid value semantics — a line-for-line port of Slate.Core's `Slate.Data.GridValues` / `GridColumn<T>`.
 * Normalisation, ordering, portable formatting and parsing are identical in C# and TypeScript; the shared
 * fixture tests/fixtures/data-grid.json proves it.
 */

export type GridColumnType = 'text' | 'number' | 'date' | 'boolean' | 'enum' | 'progress' | 'sparkline' | 'actions' | 'custom';
export type SortDirection = 'ascending' | 'descending';
export type GridPin = 'none' | 'start' | 'end';
export type GridAggregate = 'none' | 'sum' | 'avg' | 'min' | 'max' | 'count';
export type GridAlign = 'start' | 'center' | 'end';
export type GridSelectionMode = 'none' | 'single' | 'multi';
export type GridPagination = 'none' | 'pages' | 'infinite';
export type GridEditMode = 'none' | 'cell' | 'batch';
export type GridTone = 'neutral' | 'accent' | 'success' | 'warning' | 'danger' | 'info';
export type FilterOperator =
  | 'contains' | 'equals' | 'notEquals' | 'startsWith' | 'endsWith' | 'isEmpty' | 'isNotEmpty'
  | 'lessThan' | 'lessThanOrEqual' | 'greaterThan' | 'greaterThanOrEqual' | 'between' | 'anyOf';

/** A column definition. Only `field` is required; `accessor` defaults to `item[field]`. */
export interface GridColumn<T> {
  field: string;
  title?: string;
  type?: GridColumnType;
  accessor?: (item: T) => unknown;
  setter?: (item: T, value: unknown) => void;
  /** Fixed width (px). Unset with `flex` > 0 = flexible; unset otherwise = 150. */
  width?: number;
  minWidth?: number;
  maxWidth?: number;
  flex?: number;
  sortable?: boolean;
  filterable?: boolean;
  resizable?: boolean;
  reorderable?: boolean;
  hideable?: boolean;
  /** Included in the quick filter (default true). */
  searchable?: boolean;
  pinned?: GridPin;
  hidden?: boolean;
  align?: GridAlign;
  /** Portable format pattern — see {@link formatValue}. */
  format?: string;
  formatter?: (value: unknown) => string;
  aggregate?: GridAggregate;
  editable?: boolean;
  validate?: (value: unknown) => string | null | undefined;
  /** Parses editor text; throw an Error with a user-facing message to reject. */
  parser?: (text: string) => unknown;
  /** Custom ordering for non-null raw values. */
  comparer?: (a: unknown, b: unknown) => number;
  enumOrder?: readonly string[];
  enumTones?: Readonly<Record<string, GridTone>>;
}

export const DEFAULT_COLUMN_WIDTH = 150;

export const col = {
  type: <T>(c: GridColumn<T>): GridColumnType => c.type ?? 'text',
  minWidth: <T>(c: GridColumn<T>): number => c.minWidth ?? 48,
  maxWidth: <T>(c: GridColumn<T>): number => c.maxWidth ?? 2000,
  flex: <T>(c: GridColumn<T>): number => c.flex ?? 0,
  sortable: <T>(c: GridColumn<T>): boolean => c.sortable ?? true,
  searchable: <T>(c: GridColumn<T>): boolean => c.searchable ?? true,
  pinned: <T>(c: GridColumn<T>): GridPin => c.pinned ?? 'none',
  hidden: <T>(c: GridColumn<T>): boolean => c.hidden ?? false,
  aggregate: <T>(c: GridColumn<T>): GridAggregate => c.aggregate ?? 'none',
  title: <T>(c: GridColumn<T>): string => c.title ?? c.field,
  align: <T>(c: GridColumn<T>): GridAlign => c.align ?? (c.type === 'number' || c.type === 'progress' ? 'end' : 'start'),
};

/** Reads a cell value. */
export function cellValue<T>(column: GridColumn<T>, item: T): unknown {
  if (column.accessor) return column.accessor(item);
  if (item == null) return null;
  return (item as Record<string, unknown>)[column.field] ?? null;
}

/** Cell display text (also used by the quick filter, text filters, grouping and export). */
export function displayText<T>(column: GridColumn<T>, value: unknown): string {
  return column.formatter ? column.formatter(value) : formatValue(value, col.type(column), column.format);
}

/** Parses editor text with the column parser or the type's default parser. */
export function parseCell<T>(column: GridColumn<T>, text: string): unknown {
  return column.parser ? column.parser(text) : parseValue(text, col.type(column), column.enumOrder);
}

/** A user-facing parse error (mirrors .NET FormatException). */
export class GridFormatError extends Error {
  override name = 'GridFormatError';
}

const isNil = (v: unknown): v is null | undefined => v === null || v === undefined;

const NUMBER_TEXT = /^\s*[+-]?(\d+\.?\d*|\.\d+)([eE][+-]?\d+)?\s*$|^\s*[+-]?Infinity\s*$/;

export function toDouble(value: unknown): number | null {
  if (isNil(value)) return null;
  if (typeof value === 'number') return value;
  if (typeof value === 'bigint') return Number(value);
  if (typeof value === 'string') return NUMBER_TEXT.test(value) ? Number(value) : null;
  if (typeof value === 'boolean') return value ? 1 : 0;
  return null;
}

const ISO = /^(\d{4})-(\d{2})-(\d{2})(?:[T ](\d{2}):(\d{2})(?::(\d{2})(?:\.(\d{1,7}))?)?)?\s*(Z|[+-]\d{2}:?\d{2})?$/;

/** Parses "yyyy-MM-dd" or an ISO date-time; values without an offset are UTC. Returns epoch ms or null. */
export function parseIsoDate(text: string): number | null {
  const m = ISO.exec(text.trim());
  if (!m) return null;
  const [, y, mo, d, h = '0', mi = '0', s = '0', frac = '', zone] = m;
  const year = +y!, month = +mo!, day = +d!, hour = +h, minute = +mi, second = +s;
  if (month < 1 || month > 12 || day < 1 || hour > 23 || minute > 59 || second > 59) return null;
  const daysInMonth = new Date(Date.UTC(year, month, 0)).getUTCDate();
  if (day > daysInMonth) return null;
  let ms = Date.UTC(year, month - 1, day, hour, minute, second) + Math.floor(Number(('0.' + frac).padEnd(2, '0')) * 1000);
  if (zone && zone !== 'Z') {
    const sign = zone[0] === '-' ? -1 : 1;
    const digits = zone.slice(1).replace(':', '');
    ms -= sign * (Number(digits.slice(0, 2)) * 60 + Number(digits.slice(2, 4))) * 60_000;
  }
  return ms;
}

export function toEpochMs(value: unknown): number | null {
  if (isNil(value)) return null;
  if (value instanceof Date) return Number.isNaN(value.getTime()) ? null : value.getTime();
  if (typeof value === 'string') return parseIsoDate(value);
  if (typeof value === 'number') return value;
  return null;
}

export function toBool(value: unknown): boolean | null {
  if (isNil(value)) return null;
  if (typeof value === 'boolean') return value;
  if (typeof value === 'string') {
    switch (value.trim().toLowerCase()) {
      case 'true': case 'yes': case '1': return true;
      case 'false': case 'no': case '0': return false;
      default: return null;
    }
  }
  const d = toDouble(value);
  return d === null ? null : d !== 0;
}

function plainText(value: unknown): string {
  if (typeof value === 'string') return value;
  if (value instanceof Date) return value.toISOString();
  return String(value);
}

/**
 * A comparable primitive: numbers/progress → number, dates → epoch ms, booleans → boolean, else string. Null stays null.
 */
export function normalize(value: unknown, type: GridColumnType): number | boolean | string | null {
  if (isNil(value)) return null;
  switch (type) {
    case 'number': case 'progress': return toDouble(value);
    case 'date': return toEpochMs(value);
    case 'boolean': return toBool(value);
    default: return plainText(value);
  }
}

/** Ordinal UTF-16 comparison (= .NET string.CompareOrdinal sign). */
export function ordinal(a: string, b: string): number {
  return a < b ? -1 : a > b ? 1 : 0;
}

/** Orders two normalised values: numbers numerically, false < true, text case-insensitively then ordinally; nulls last. */
export function compareNormalized(a: unknown, b: unknown): number {
  if (isNil(a) && isNil(b)) return 0;
  if (isNil(a)) return 1;
  if (isNil(b)) return -1;
  if (typeof a === 'number' && typeof b === 'number') return a < b ? -1 : a > b ? 1 : 0;
  if (typeof a === 'boolean' && typeof b === 'boolean') return a === b ? 0 : a ? 1 : -1;
  if (typeof a === 'string' && typeof b === 'string') {
    const c = ordinal(a.toLowerCase(), b.toLowerCase());
    return c !== 0 ? c : ordinal(a, b);
  }
  return ordinal(String(a), String(b));
}

const MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];

/**
 * Portable display formatting (identical in C#).
 * Numbers: default shortest round-trip; patterns "0", "0.00", "#,##0", "#,##0.0", "0%", "0.0%".
 * Dates (UTC): default "yyyy-MM-dd", or "yyyy-MM-dd HH:mm" when not midnight; tokens yyyy MMM MM dd HH mm ss.
 * Booleans: "true"/"false". Null: "".
 */
export function formatValue(value: unknown, type: GridColumnType, pattern?: string | null): string {
  if (isNil(value)) return '';
  switch (type) {
    case 'number': case 'progress': {
      const d = toDouble(value);
      return d === null ? plainText(value) : formatNumber(d, pattern);
    }
    case 'date': {
      const ms = toEpochMs(value);
      return ms === null ? plainText(value) : formatDate(ms, pattern);
    }
    case 'boolean': {
      const b = toBool(value);
      return b === null ? '' : b ? 'true' : 'false';
    }
    default:
      return plainText(value);
  }
}

export function formatNumber(d: number, pattern?: string | null): string {
  if (!pattern) return String(d);
  const percent = pattern.endsWith('%');
  const core = percent ? pattern.slice(0, -1) : pattern;
  const dot = core.indexOf('.');
  const decimals = dot < 0 ? 0 : core.length - dot - 1;
  let text = (percent ? d * 100 : d).toFixed(decimals);
  if (core.includes(',')) text = group(text);
  return percent ? text + '%' : text;
}

function group(fixed: string): string {
  const negative = fixed.startsWith('-');
  const body = negative ? fixed.slice(1) : fixed;
  const dot = body.indexOf('.');
  const intPart = dot < 0 ? body : body.slice(0, dot);
  const frac = dot < 0 ? '' : body.slice(dot);
  let out = '';
  for (let i = 0; i < intPart.length; i++) {
    if (i > 0 && (intPart.length - i) % 3 === 0) out += ',';
    out += intPart[i];
  }
  return (negative ? '-' : '') + out + frac;
}

const pad = (n: number, w: number) => String(n).padStart(w, '0');

export function formatDate(epochMs: number, pattern?: string | null): string {
  const d = new Date(Math.round(epochMs));
  const midnight = d.getUTCHours() === 0 && d.getUTCMinutes() === 0 && d.getUTCSeconds() === 0 && d.getUTCMilliseconds() === 0;
  const p = pattern ?? (midnight ? 'yyyy-MM-dd' : 'yyyy-MM-dd HH:mm');
  let out = '';
  let i = 0;
  const starts = (t: string) => p.startsWith(t, i);
  while (i < p.length) {
    if (starts('yyyy')) { out += pad(d.getUTCFullYear(), 4); i += 4; }
    else if (starts('MMM')) { out += MONTHS[d.getUTCMonth()]; i += 3; }
    else if (starts('MM')) { out += pad(d.getUTCMonth() + 1, 2); i += 2; }
    else if (starts('dd')) { out += pad(d.getUTCDate(), 2); i += 2; }
    else if (starts('HH')) { out += pad(d.getUTCHours(), 2); i += 2; }
    else if (starts('mm')) { out += pad(d.getUTCMinutes(), 2); i += 2; }
    else if (starts('ss')) { out += pad(d.getUTCSeconds(), 2); i += 2; }
    else { out += p[i]; i++; }
  }
  return out;
}

/** Default editor parsing per type. Throws {@link GridFormatError} with a user-facing message. */
export function parseValue(text: string, type: GridColumnType, enumOrder?: readonly string[]): unknown {
  const t = text.trim();
  if (t.length === 0) return null;
  switch (type) {
    case 'number': case 'progress': {
      const d = toDouble(t);
      if (d === null || !Number.isFinite(d)) throw new GridFormatError('Enter a number.');
      return d;
    }
    case 'date': {
      const ms = parseIsoDate(t);
      if (ms === null) throw new GridFormatError('Enter a date (YYYY-MM-DD).');
      return new Date(ms);
    }
    case 'boolean': {
      const b = toBool(t);
      if (b === null) throw new GridFormatError('Enter true or false.');
      return b;
    }
    case 'enum':
      if (enumOrder && enumOrder.length > 0) {
        const hit = enumOrder.find((v) => v.toLowerCase() === t.toLowerCase());
        if (hit === undefined) throw new GridFormatError(`Choose one of: ${enumOrder.join(', ')}.`);
        return hit;
      }
      return text;
    default:
      return text;
  }
}

/** Row key text (invariant). */
export function keyText(key: unknown): string {
  if (isNil(key)) return '';
  return typeof key === 'string' ? key : String(key);
}

/** .NET Math.Round(x, digits): scale, round half to even, unscale. */
export function roundTo(x: number, digits: number): number {
  const f = 10 ** digits;
  const s = x * f;
  const r = Math.round(s);
  const even = Math.abs(s % 1) === 0.5 ? 2 * Math.round(s / 2) : r;
  return even / f;
}
