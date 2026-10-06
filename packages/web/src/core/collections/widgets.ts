/**
 * Pagination ranges, avatar initials/tone and slider maths — a port of Slate.Core's PaginationRange, AvatarText and
 * SliderMath. Results match the C# implementation exactly (tests/fixtures/components.json).
 */
import { foldText } from './list';

// ---- pagination --------------------------------------------------------------------------------------------

/** A page number, or the string '…' for a gap. */
export type PaginationItem = number | '…';

const span = (from: number, to: number) => (from > to ? [] : Array.from({ length: to - from + 1 }, (_, i) => from + i));

/**
 * Page slots: `boundaries` pages at each end, `siblings` around the current page, '…' for gaps. The slot count is
 * constant while pages exceed it, so a pager never changes width.
 */
export function paginationRange(page: number, pageCount: number, siblings = 1, boundaries = 1): PaginationItem[] {
  if (pageCount < 1) return [];
  if (siblings < 0 || boundaries < 0) throw new RangeError('siblings and boundaries must be non-negative.');
  page = Math.min(Math.max(page, 1), pageCount);

  const startPages = span(1, Math.min(boundaries, pageCount));
  const endPages = span(Math.max(pageCount - boundaries + 1, boundaries + 1), pageCount);
  const siblingsStart = Math.max(Math.min(page - siblings, pageCount - boundaries - siblings * 2 - 1), boundaries + 2);
  const siblingsEnd = Math.min(Math.max(page + siblings, boundaries + siblings * 2 + 2), endPages.length > 0 ? endPages[0] - 2 : pageCount - 1);

  const items: PaginationItem[] = [...startPages];
  if (siblingsStart > boundaries + 2) items.push('…');
  else if (boundaries + 1 < pageCount - boundaries) items.push(boundaries + 1);
  items.push(...span(siblingsStart, siblingsEnd));
  if (siblingsEnd < pageCount - boundaries - 1) items.push('…');
  else if (pageCount - boundaries > boundaries) items.push(pageCount - boundaries);
  items.push(...endPages);

  const seen = new Set<number>();
  return items.filter((i) => i === '…' || (i >= 1 && i <= pageCount && !seen.has(i) && (seen.add(i), true)));
}

export function pageCount(totalCount: number, pageSize: number): number {
  if (pageSize < 1) throw new RangeError('pageSize must be at least 1.');
  return Math.max(1, Math.ceil(Math.max(0, totalCount) / pageSize));
}

/** After a page-size change, the (1-based) page that still shows the first item of the current page. */
export function pageForFirstItem(page: number, oldPageSize: number, newPageSize: number): number {
  if (oldPageSize < 1 || newPageSize < 1) throw new RangeError('Page sizes must be at least 1.');
  const firstIndex = (Math.max(page, 1) - 1) * oldPageSize;
  return Math.floor(firstIndex / newPageSize) + 1;
}

// ---- avatar ------------------------------------------------------------------------------------------------

export type AvatarTone = 'accent' | 'info' | 'success' | 'warning' | 'danger';
const palette: AvatarTone[] = ['accent', 'info', 'success', 'warning', 'danger'];

function firstLetter(word: string): string {
  const cp = [...word][0];
  const upper = cp.toUpperCase();
  // Single code point upper-casing only, like .NET's Rune.ToUpperInvariant.
  return [...upper].length === 1 ? upper : cp;
}

/** "Aaron Griffin" → "AG", "aaron" → "A", "jo.marsh@x.io" → "JM", "@handle" → "H", "" → "?". */
export function avatarInitials(name: string | null | undefined): string {
  let s = (name ?? '').trim().replace(/^@+/, '');
  const at = s.indexOf('@');
  if (at > 0) s = s.slice(0, at);
  const separators = /\s/.test(s) ? /[ \t\n\r]+/ : /[._-]+/;
  const words = s.split(separators).filter((w) => w.length > 0);
  if (words.length === 0) return '?';
  const first = firstLetter(words[0]);
  return words.length === 1 ? first : first + firstLetter(words[words.length - 1]);
}

/** A stable tone per name (FNV-1a over the folded UTF-16 units + MurmurHash3 finaliser), same as .NET. */
export function avatarTone(name: string | null | undefined): AvatarTone {
  const folded = foldText((name ?? '').trim());
  let hash = 0x811c9dc5;
  for (let i = 0; i < folded.length; i++) {
    hash ^= folded.charCodeAt(i);
    hash = Math.imul(hash, 0x01000193) >>> 0;
  }
  hash ^= hash >>> 16;
  hash = Math.imul(hash, 0x85ebca6b) >>> 0;
  hash ^= hash >>> 13;
  hash = Math.imul(hash, 0xc2b2ae35) >>> 0;
  hash ^= hash >>> 16;
  return palette[(hash >>> 0) % palette.length];
}

// ---- slider ------------------------------------------------------------------------------------------------

export type SliderKey = 'increase' | 'decrease' | 'page-increase' | 'page-decrease' | 'home' | 'end';

const clampValue = (v: number, min: number, max: number) => Math.min(Math.max(v, min), max);

/** Removes binary floating-point noise at 10 decimal places (same arithmetic as .NET). */
const tidy = (v: number) => Math.floor(v * 1e10 + 0.5) / 1e10;

export function snapValue(value: number, min: number, max: number, step: number): number {
  if (max < min) throw new RangeError('max must be ≥ min.');
  if (step <= 0) return clampValue(value, min, max);
  const steps = Math.floor(tidy((value - min) / step) + 0.5);
  let snapped = tidy(min + steps * step);
  while (snapped > max && snapped - step >= min) snapped = tidy(snapped - step);
  while (snapped < min) snapped = tidy(snapped + step);
  return clampValue(snapped, min, max);
}

export function sliderKey(value: number, key: SliderKey, min: number, max: number, step: number): number {
  const page = Math.max(step, snapValue(min + (max - min) / 10, min, max, step) - min);
  switch (key) {
    case 'increase': return snapValue(value + step, min, max, step);
    case 'decrease': return snapValue(value - step, min, max, step);
    case 'page-increase': return snapValue(value + page, min, max, step);
    case 'page-decrease': return snapValue(value - page, min, max, step);
    case 'home': return min;
    case 'end': return max;
    default: throw new RangeError(`Unknown key '${key as string}'.`);
  }
}

export function valueToFraction(value: number, min: number, max: number): number {
  return max === min ? 0 : clampValue((value - min) / (max - min), 0, 1);
}

export function fractionToValue(fraction: number, min: number, max: number, step: number): number {
  return snapValue(min + clampValue(fraction, 0, 1) * (max - min), min, max, step);
}

/** Moves one thumb of a range slider without letting it cross the other. */
export function setRangeThumb(range: [number, number], thumb: 0 | 1, value: number, min: number, max: number, step: number): [number, number] {
  const v = snapValue(value, min, max, step);
  return thumb === 0 ? [Math.min(v, range[1]), range[1]] : [range[0], Math.max(v, range[0])];
}
