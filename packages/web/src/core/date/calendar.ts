/**
 * Calendar model behind sl-date-picker — a port of Slate.Core's Slate.Dates (CalendarModel, DateText).
 * Dates are ISO strings ("2026-10-06"); weekdays are numbers 0 (Sunday) – 6 (Saturday), as in JS and .NET.
 * Results are identical to the C# implementation (tests/fixtures/components.json).
 */

export type IsoDate = string;
export type Weekday = 0 | 1 | 2 | 3 | 4 | 5 | 6;

export interface DateRange {
  start: IsoDate;
  /** Null while the second date is pending. */
  end: IsoDate | null;
}

export interface CalendarOptions {
  firstDayOfWeek?: Weekday;
  today?: IsoDate | null;
  min?: IsoDate | null;
  max?: IsoDate | null;
  isDateDisabled?: (date: IsoDate) => boolean;
  selected?: IsoDate | null;
  range?: DateRange | null;
  /** Hovered/focused day: previews the range while its end is pending. */
  hover?: IsoDate | null;
}

export interface CalendarDay {
  date: IsoDate;
  inMonth: boolean;
  isToday: boolean;
  isDisabled: boolean;
  isSelected: boolean;
  isRangeStart: boolean;
  isRangeEnd: boolean;
  inRange: boolean;
  inPreview: boolean;
}

export interface CalendarWeek {
  /** ISO-8601 week of the row's Thursday. */
  isoWeek: number;
  days: CalendarDay[];
}

export interface CalendarMonth {
  year: number;
  month: number;
  weekdays: Weekday[];
  weeks: CalendarWeek[];
}

export type CalendarKey = 'left' | 'right' | 'up' | 'down' | 'home' | 'end' | 'page-up' | 'page-down';

export type DatePresetKind = 'today' | 'yesterday' | 'last7-days' | 'last30-days' | 'this-month' | 'last-month' | 'this-year';

export interface DatePreset {
  label: string;
  kind: DatePresetKind;
}

// ---- date arithmetic on ISO strings (UTC, no time zones involved) -----------------------------------------

const DAY_MS = 86_400_000;

function parts(iso: IsoDate): [number, number, number] {
  const m = /^(\d{4})-(\d{2})-(\d{2})$/.exec(iso);
  if (!m) throw new RangeError(`Not an ISO date: '${iso}'.`);
  return [Number(m[1]), Number(m[2]), Number(m[3])];
}

function pad(n: number, width: number): string {
  return String(n).padStart(width, '0');
}

export function isoDate(year: number, month: number, day: number): IsoDate {
  return `${pad(year, 4)}-${pad(month, 2)}-${pad(day, 2)}`;
}

function toDays(iso: IsoDate): number {
  const [y, m, d] = parts(iso);
  const date = new Date(0);
  date.setUTCFullYear(y, m - 1, d);
  return Math.round(date.getTime() / DAY_MS);
}

function fromDays(days: number): IsoDate {
  const d = new Date(days * DAY_MS);
  return isoDate(d.getUTCFullYear(), d.getUTCMonth() + 1, d.getUTCDate());
}

export function daysInMonth(year: number, month: number): number {
  return [31, (year % 4 === 0 && year % 100 !== 0) || year % 400 === 0 ? 29 : 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31][month - 1];
}

export function addDays(iso: IsoDate, n: number): IsoDate {
  return fromDays(toDays(iso) + n);
}

/** Adds months, clamping the day to the target month's length (Jan 31 + 1 month = Feb 28/29), like .NET. */
export function addMonths(iso: IsoDate, n: number): IsoDate {
  const [y, m, d] = parts(iso);
  const total = y * 12 + (m - 1) + n;
  const year = Math.floor(total / 12);
  const month = (total % 12 + 12) % 12 + 1;
  return isoDate(year, month, Math.min(d, daysInMonth(year, month)));
}

export function addYears(iso: IsoDate, n: number): IsoDate {
  return addMonths(iso, n * 12);
}

export function dayOfWeek(iso: IsoDate): Weekday {
  return (((toDays(iso) % 7) + 7 + 4) % 7) as Weekday; // 1970-01-01 was a Thursday
}

/** ISO-8601 week number (weeks start Monday; week 1 contains the year's first Thursday). */
export function isoWeek(iso: IsoDate): number {
  const days = toDays(iso);
  const dow = (dayOfWeek(iso) + 6) % 7; // Monday = 0
  const thursday = days - dow + 3;
  const year = Number(fromDays(thursday).slice(0, 4));
  const jan1 = toDays(isoDate(year, 1, 1));
  return Math.floor((thursday - jan1) / 7) + 1;
}

/** First day of week for a locale (Intl weekInfo where available; Sunday for US-style locales, else Monday). */
export function firstDayOfWeek(locale: string): Weekday {
  try {
    const info = (new Intl.Locale(locale) as Intl.Locale & { weekInfo?: { firstDay: number }; getWeekInfo?: () => { firstDay: number } });
    const first = info.getWeekInfo?.().firstDay ?? info.weekInfo?.firstDay;
    if (first) return (first % 7) as Weekday;
  } catch {
    // fall through
  }
  return /^(en-(US|CA|PH)|ja|ko|zh-(TW|HK)|he|pt-BR|es-MX)\b/i.test(locale) ? 0 : 1;
}

const isDisabled = (d: IsoDate, o: CalendarOptions) =>
  (o.min != null && d < o.min) || (o.max != null && d > o.max) || (o.isDateDisabled?.(d) ?? false);

const inRange = (r: DateRange | null | undefined, d: IsoDate) => r != null && r.end != null && d >= r.start && d <= r.end;

// ---- model -------------------------------------------------------------------------------------------------

export const calendarRows = 6;

export function buildMonth(year: number, month: number, options: CalendarOptions = {}): CalendarMonth {
  const first = options.firstDayOfWeek ?? 1;
  const firstOfMonth = isoDate(year, month, 1);
  const lead = (dayOfWeek(firstOfMonth) - first + 7) % 7;
  const start = addDays(firstOfMonth, -lead);
  const weekdays = Array.from({ length: 7 }, (_, i) => ((first + i) % 7) as Weekday);

  const range = options.range ?? null;
  let preview: DateRange | null = null;
  if (range && range.end == null && options.hover != null) {
    const hover = options.hover;
    preview = hover < range.start ? { start: hover, end: range.start } : { start: range.start, end: hover };
  }

  const weeks: CalendarWeek[] = [];
  for (let row = 0; row < calendarRows; row++) {
    const days: CalendarDay[] = [];
    let thursday = start;
    for (let col = 0; col < 7; col++) {
      const d = addDays(start, row * 7 + col);
      if (dayOfWeek(d) === 4) thursday = d;
      const [y, m] = parts(d);
      days.push({
        date: d,
        inMonth: m === month && y === year,
        isToday: options.today === d,
        isDisabled: isDisabled(d, options),
        isSelected: options.selected === d || (range != null && (range.start === d || range.end === d)),
        isRangeStart: range != null && range.start === d,
        isRangeEnd: range != null && range.end === d,
        inRange: inRange(range, d),
        inPreview: inRange(preview, d),
      });
    }
    weeks.push({ isoWeek: isoWeek(thursday), days });
  }
  return { year, month, weekdays, weeks };
}

/**
 * Moves the focused day for a key (Shift with page keys moves a year), landing on the nearest enabled day in the
 * direction of travel; stays put if nothing is reachable.
 */
export function navigateCalendar(focused: IsoDate, key: CalendarKey, shift: boolean, options: CalendarOptions = {}): IsoDate {
  const first = options.firstDayOfWeek ?? 1;
  const intoWeek = (dayOfWeek(focused) - first + 7) % 7;
  let target: IsoDate;
  let step: number;
  switch (key) {
    case 'left': target = addDays(focused, -1); step = -1; break;
    case 'right': target = addDays(focused, 1); step = 1; break;
    case 'up': target = addDays(focused, -7); step = -7; break;
    case 'down': target = addDays(focused, 7); step = 7; break;
    case 'home': target = addDays(focused, -intoWeek); step = 1; break;
    case 'end': target = addDays(focused, 6 - intoWeek); step = -1; break;
    case 'page-up': target = shift ? addYears(focused, -1) : addMonths(focused, -1); step = -1; break;
    case 'page-down': target = shift ? addYears(focused, 1) : addMonths(focused, 1); step = 1; break;
    default: throw new RangeError(`Unknown key '${key as string}'.`);
  }

  if (options.min != null && target < options.min) target = options.min;
  if (options.max != null && target > options.max) target = options.max;

  for (let i = 0; i < 400; i++) {
    if (!isDisabled(target, options)) return target;
    const next = addDays(target, step);
    if ((options.min != null && next < options.min) || (options.max != null && next > options.max)) break;
    target = next;
  }
  return focused;
}

/** Range picking: first click starts, second completes (either order), a click after a complete range restarts. */
export function pickRange(current: DateRange | null, clicked: IsoDate): DateRange {
  if (current && current.end == null) {
    return clicked < current.start ? { start: clicked, end: current.start } : { start: current.start, end: clicked };
  }
  return { start: clicked, end: null };
}

export const defaultPresets: readonly DatePreset[] = [
  { label: 'Today', kind: 'today' },
  { label: 'Yesterday', kind: 'yesterday' },
  { label: 'Last 7 days', kind: 'last7-days' },
  { label: 'Last 30 days', kind: 'last30-days' },
  { label: 'This month', kind: 'this-month' },
  { label: 'Last month', kind: 'last-month' },
  { label: 'This year', kind: 'this-year' },
];

/** Resolves a preset to an inclusive range relative to `today`. */
export function resolvePreset(kind: DatePresetKind, today: IsoDate): { start: IsoDate; end: IsoDate } {
  const [y, m] = parts(today);
  switch (kind) {
    case 'today': return { start: today, end: today };
    case 'yesterday': return { start: addDays(today, -1), end: addDays(today, -1) };
    case 'last7-days': return { start: addDays(today, -6), end: today };
    case 'last30-days': return { start: addDays(today, -29), end: today };
    case 'this-month': return { start: isoDate(y, m, 1), end: isoDate(y, m, daysInMonth(y, m)) };
    case 'last-month': {
      const first = addMonths(isoDate(y, m, 1), -1);
      const [fy, fm] = parts(first);
      return { start: first, end: isoDate(fy, fm, daysInMonth(fy, fm)) };
    }
    case 'this-year': return { start: isoDate(y, 1, 1), end: isoDate(y, 12, 31) };
    default: throw new RangeError(`Unknown preset '${kind as string}'.`);
  }
}

// ---- typed dates -------------------------------------------------------------------------------------------

export const isoPattern = 'yyyy-MM-dd';

type Token = 'yyyy' | 'yy' | 'MM' | 'M' | 'dd' | 'd';

function tokenize(pattern: string): Array<{ token: Token | null; literal: string }> {
  const out: Array<{ token: Token | null; literal: string }> = [];
  let i = 0;
  while (i < pattern.length) {
    const c = pattern[i];
    if (c === 'y' || c === 'M' || c === 'd') {
      let j = i;
      while (j < pattern.length && pattern[j] === c) j++;
      const run = pattern.slice(i, j);
      const token: Token = c === 'y' ? (run.length <= 2 ? 'yy' : 'yyyy') : c === 'M' ? (run.length === 1 ? 'M' : 'MM') : run.length === 1 ? 'd' : 'dd';
      out.push({ token, literal: run });
      i = j;
    } else {
      out.push({ token: null, literal: c });
      i++;
    }
  }
  return out;
}

/** Formats with tokens yyyy, yy, MM, M, dd, d (everything else literal). */
export function formatDate(date: IsoDate, pattern: string): string {
  const [y, m, d] = parts(date);
  return tokenize(pattern)
    .map(({ token, literal }) => {
      switch (token) {
        case 'yyyy': return pad(y, 4);
        case 'yy': return pad(y % 100, 2);
        case 'MM': return pad(m, 2);
        case 'M': return String(m);
        case 'dd': return pad(d, 2);
        case 'd': return String(d);
        default: return literal;
      }
    })
    .join('');
}

function make(y: number, m: number, d: number): IsoDate | null {
  return y >= 1 && y <= 9999 && m >= 1 && m <= 12 && d >= 1 && d <= daysInMonth(y, m) ? isoDate(y, m, d) : null;
}

/**
 * Lenient parse: ISO (yyyy-M-d) always works; otherwise three numbers are read in the pattern's order with any
 * separators. Two-digit years pivot at 50 (00–49 → 2000s). Null if invalid.
 */
export function parseDate(text: string | null | undefined, pattern: string): IsoDate | null {
  const s = (text ?? '').trim();
  if (!s) return null;

  const iso = /^(\d{4})-(\d{1,2})-(\d{1,2})$/.exec(s);
  if (iso) return make(Number(iso[1]), Number(iso[2]), Number(iso[3]));

  const numbers = s.match(/\d+/g) ?? [];
  const order = tokenize(pattern).map((t) => t.token).filter((t): t is Token => t !== null);
  if (numbers.length !== 3 || order.length !== 3) return null;

  let year = 0;
  let month = 0;
  let day = 0;
  for (let i = 0; i < 3; i++) {
    const n = numbers[i];
    if (n.length > 4) return null;
    const v = Number(n);
    switch (order[i]) {
      case 'yyyy':
      case 'yy':
        year = n.length <= 2 ? (v <= 49 ? 2000 + v : 1900 + v) : v;
        break;
      case 'MM':
      case 'M':
        month = v;
        break;
      default:
        day = v;
    }
  }
  return make(year, month, day);
}

/** A locale's short date pattern in DateText tokens (e.g. en-US → "M/d/yyyy"). */
export function shortPattern(locale: string): string {
  const parts = new Intl.DateTimeFormat(locale, { year: 'numeric', month: 'numeric', day: 'numeric', timeZone: 'UTC' }).formatToParts(new Date(Date.UTC(2026, 0, 5)));
  return parts
    .map((p) => {
      switch (p.type) {
        case 'year': return p.value.length <= 2 ? 'yy' : 'yyyy';
        case 'month': return p.value.length === 1 ? 'M' : 'MM';
        case 'day': return p.value.length === 1 ? 'd' : 'dd';
        case 'literal': return p.value.replace(/[^/.\- ]/g, '');
        default: return '';
      }
    })
    .join('');
}
