/**
 * List navigation, typeahead and searchable-option filtering — a port of Slate.Core's Slate.Collections
 * (ListNavigator, Typeahead, TextFolding, OptionFilter). Results match the C# implementation exactly
 * (tests/fixtures/components.json).
 */

export type ListKey = 'next' | 'previous' | 'first' | 'last' | 'page-down' | 'page-up';

export interface ListNavigationOptions {
  /** Next on the last item goes to the first (and vice versa). */
  wrap?: boolean;
  /** Items moved by PageUp/PageDown (default 10). */
  pageSize?: number;
}

/** Roving active index that skips disabled items. -1 means "nothing active". */
export function moveIndex(current: number, key: ListKey, disabled: readonly boolean[], options: ListNavigationOptions = {}): number {
  const wrap = options.wrap ?? false;
  const pageSize = options.pageSize ?? 10;
  const count = disabled.length;
  if (count === 0) return -1;
  const enabled = (i: number) => i >= 0 && i < count && !disabled[i];
  const firstFrom = (start: number, step: number) => {
    for (let i = start; i >= 0 && i < count; i += step) if (enabled(i)) return i;
    return -1;
  };

  switch (key) {
    case 'first':
      return firstFrom(0, 1);
    case 'last':
      return firstFrom(count - 1, -1);
    case 'next': {
      if (current < 0) return firstFrom(0, 1);
      const next = firstFrom(current + 1, 1);
      if (next >= 0) return next;
      if (wrap) {
        const wrapped = firstFrom(0, 1);
        if (wrapped >= 0) return wrapped;
      }
      return enabled(current) ? current : firstFrom(count - 1, -1);
    }
    case 'previous': {
      if (current < 0) return firstFrom(count - 1, -1);
      const prev = firstFrom(current - 1, -1);
      if (prev >= 0) return prev;
      if (wrap) {
        const wrapped = firstFrom(count - 1, -1);
        if (wrapped >= 0) return wrapped;
      }
      return enabled(current) ? current : firstFrom(0, 1);
    }
    case 'page-down': {
      const target = Math.min(Math.max(current, 0) + pageSize, count - 1);
      const found = firstFrom(target, 1);
      return found >= 0 ? found : firstFrom(target, -1);
    }
    case 'page-up': {
      const target = Math.max((current < 0 ? count - 1 : current) - pageSize, 0);
      const found = firstFrom(target, -1);
      return found >= 0 ? found : firstFrom(target, 1);
    }
    default:
      throw new RangeError(`Unknown key '${key as string}'.`);
  }
}

// ---- folding -----------------------------------------------------------------------------------------------

const MARK = /\p{Mn}/u;

function foldCodePoint(cp: string): string {
  let out = '';
  for (const part of cp.normalize('NFD')) {
    // Per code point, like .NET's Rune.ToLowerInvariant (no multi-character expansions).
    const lowered = part.toLowerCase();
    const lower = [...lowered].length === 1 ? lowered : part;
    if (!MARK.test(lower)) out += lower;
  }
  return out;
}

/** Case- and diacritic-insensitive folding (NFD, lower-case, combining marks removed). */
export function foldText(text: string): string {
  let out = '';
  for (const cp of text) out += foldCodePoint(cp);
  return out;
}

interface FoldMap {
  codePoints: number[];
  sourceStart: number[];
  sourceLength: number[];
}

function foldWithMap(text: string): FoldMap {
  const map: FoldMap = { codePoints: [], sourceStart: [], sourceLength: [] };
  let offset = 0;
  for (const cp of text) {
    for (const folded of foldCodePoint(cp)) {
      map.codePoints.push(folded.codePointAt(0)!);
      map.sourceStart.push(offset);
      map.sourceLength.push(cp.length);
    }
    offset += cp.length;
  }
  return map;
}

// ---- typeahead ---------------------------------------------------------------------------------------------

/**
 * Type-to-jump. Characters typed within `timeout` build a prefix; repeating one letter cycles through items that
 * start with it. Timestamps are passed in, so behaviour is deterministic and testable.
 */
export class Typeahead {
  private buffer = '';
  private last = Number.NEGATIVE_INFINITY;

  constructor(readonly timeout = 500) {}

  get currentBuffer(): string {
    return this.buffer;
  }

  reset(): void {
    this.buffer = '';
    this.last = Number.NEGATIVE_INFINITY;
  }

  /** Returns the matching index, or -1 (the buffer is kept so further typing can still match). */
  search(key: string, timestampMs: number, labels: readonly string[], current: number, disabled?: readonly boolean[]): number {
    if (!key) return -1;
    if (this.last === Number.NEGATIVE_INFINITY || timestampMs - this.last > this.timeout) this.buffer = '';
    this.last = timestampMs;
    this.buffer += foldText(key);

    const buffer = this.buffer;
    const count = labels.length;
    if (count === 0) return -1;

    const repeated = buffer.length > 1 && [...buffer].every((c) => c === buffer[0]);
    const prefix = repeated ? buffer.slice(0, 1) : buffer;
    const start = buffer.length === 1 || repeated ? current + 1 : Math.max(current, 0);

    for (let n = 0; n < count; n++) {
      const i = (((start + n) % count) + count) % count;
      if (disabled && i < disabled.length && disabled[i]) continue;
      if (foldText(labels[i]).startsWith(prefix)) return i;
    }
    return -1;
  }
}

// ---- option filtering --------------------------------------------------------------------------------------

/** A highlighted span in UTF-16 code units of the original label. */
export interface TextRange {
  start: number;
  length: number;
}

export interface OptionMatch {
  index: number;
  score: number;
  ranges: TextRange[];
}

const WORD = /[\p{L}\p{N}]/u;

function bestOccurrence(text: number[], term: number[]): [number, number] {
  let first = -1;
  for (let i = 0; i + term.length <= text.length; i++) {
    let match = true;
    for (let j = 0; j < term.length; j++) {
      if (text[i + j] !== term[j]) {
        match = false;
        break;
      }
    }
    if (!match) continue;
    if (i === 0) return [0, 3];
    if (!WORD.test(String.fromCodePoint(text[i - 1]))) return [i, 2];
    if (first < 0) first = i;
  }
  return first < 0 ? [-1, 0] : [first, 1];
}

function merge(ranges: TextRange[]): TextRange[] {
  if (ranges.length < 2) return ranges;
  const sorted = [...ranges].sort((a, b) => a.start - b.start);
  const merged: TextRange[] = [sorted[0]];
  for (const r of sorted.slice(1)) {
    const last = merged[merged.length - 1];
    if (r.start <= last.start + last.length) {
      merged[merged.length - 1] = { start: last.start, length: Math.max(last.start + last.length, r.start + r.length) - last.start };
    } else {
      merged.push(r);
    }
  }
  return merged;
}

/**
 * Searchable-select filtering: every whitespace-separated term must appear (case/diacritic-insensitive).
 * Scores: label start 3, word start 2, elsewhere 1, summed; sorted by score then original order.
 */
export function filterOptions(labels: readonly string[], query: string | null | undefined): OptionMatch[] {
  const terms = (query ?? '')
    .split(/\s+/)
    .filter((t) => t.length > 0)
    .map((t) => foldWithMap(t).codePoints)
    .filter((t) => t.length > 0);

  if (terms.length === 0) return labels.map((_, index) => ({ index, score: 0, ranges: [] }));

  const results: OptionMatch[] = [];
  labels.forEach((label, index) => {
    const { codePoints, sourceStart, sourceLength } = foldWithMap(label);
    let score = 0;
    const ranges: TextRange[] = [];
    for (const term of terms) {
      const [pos, s] = bestOccurrence(codePoints, term);
      if (pos < 0) return;
      score += s;
      const last = pos + term.length - 1;
      const start = sourceStart[pos];
      ranges.push({ start, length: sourceStart[last] + sourceLength[last] - start });
    }
    results.push({ index, score, ranges: merge(ranges) });
  });

  return results.sort((a, b) => b.score - a.score || a.index - b.index);
}

/** Groups item indices by key, keeping the order in which each group first appears. */
export function groupIndices(keys: readonly string[]): Array<{ key: string; indices: number[] }> {
  const groups = new Map<string, number[]>();
  keys.forEach((key, i) => {
    const list = groups.get(key);
    if (list) list.push(i);
    else groups.set(key, [i]);
  });
  return [...groups].map(([key, indices]) => ({ key, indices }));
}
