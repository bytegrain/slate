/** Server mode: queries, an in-memory source and the infinite-scroll block cache — port of Slate.Core's. */
import { DataPipeline } from './pipeline';
import { createGridState, type GridFilter, type GridSort, type GridState } from './state';
import type { GridColumn } from './values';

export interface GridQuery {
  readonly sorts: readonly GridSort[];
  readonly filters: readonly GridFilter[];
  readonly quickFilter: string;
  readonly offset: number;
  readonly count: number;
}

export interface GridResult<T> {
  readonly items: readonly T[];
  readonly totalCount: number;
}

/** Server-side data for a grid. Implementations should honour the abort signal. */
export interface GridDataSource<T> {
  query(query: GridQuery, signal?: AbortSignal): Promise<GridResult<T>>;
}

export function queryFromState(state: GridState, offset = 0, count = 100): GridQuery {
  return { sorts: state.sorts, filters: state.filters, quickFilter: state.quickFilter, offset, count };
}

export function sameResultSet(a: GridQuery, b: GridQuery): boolean {
  return JSON.stringify([a.sorts, a.filters, a.quickFilter]) === JSON.stringify([b.sorts, b.filters, b.quickFilter]);
}

const abortError = () => (typeof DOMException !== 'undefined' ? new DOMException('Aborted', 'AbortError') : Object.assign(new Error('Aborted'), { name: 'AbortError' }));

/** Data source over an array, with the client pipeline's filter/sort semantics and optional latency. */
export class InMemoryGridDataSource<T> implements GridDataSource<T> {
  private readonly pipeline: DataPipeline<T>;
  queryCount = 0;

  constructor(private readonly items: readonly T[], columns: readonly GridColumn<T>[], private readonly latencyMs = 0) {
    this.pipeline = new DataPipeline(columns);
  }

  async query(query: GridQuery, signal?: AbortSignal): Promise<GridResult<T>> {
    this.queryCount++;
    if (this.latencyMs > 0) {
      await new Promise<void>((resolve, reject) => {
        const t = setTimeout(resolve, this.latencyMs);
        signal?.addEventListener('abort', () => { clearTimeout(t); reject(abortError()); }, { once: true });
      });
    }
    if (signal?.aborted) throw abortError();
    const state = createGridState({ sorts: query.sorts, filters: query.filters, quickFilter: query.quickFilter });
    const predicate = this.pipeline.buildPredicate(state);
    const indices: number[] = [];
    for (let i = 0; i < this.items.length; i++) if (!predicate || predicate(this.items[i]!)) indices.push(i);
    this.pipeline.sort(this.items, indices, query.sorts);
    const window = indices.slice(Math.max(0, query.offset), Math.max(0, query.offset) + Math.max(0, query.count)).map((i) => this.items[i]!);
    return { items: window, totalCount: indices.length };
  }
}

/** Block cache for infinite scrolling: loads blocks on demand, de-dupes in-flight loads, cancels on query change. */
export class GridRowCache<T> {
  readonly blockSize: number;
  totalCount: number | null = null;
  lastError: unknown = null;
  private readonly blocks = new Map<number, readonly T[]>();
  private readonly inFlight = new Map<number, Promise<void>>();
  private controller = new AbortController();
  private generation = 0;
  private current: GridQuery = { sorts: [], filters: [], quickFilter: '', offset: 0, count: 100 };
  private listeners = new Set<() => void>();

  constructor(private readonly source: GridDataSource<T>, blockSize = 100) {
    if (blockSize < 1) throw new RangeError('blockSize must be at least 1.');
    this.blockSize = blockSize;
  }

  get query(): GridQuery { return this.current; }
  get loadedBlockCount(): number { return this.blocks.size; }

  onChange(listener: () => void): () => void {
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  }

  setQuery(query: GridQuery): void {
    this.controller.abort();
    this.controller = new AbortController();
    this.generation++;
    this.blocks.clear();
    this.inFlight.clear();
    this.current = query;
    this.totalCount = null;
    this.lastError = null;
    this.raise();
  }

  get(index: number): T | undefined {
    const block = this.blocks.get(Math.floor(index / this.blockSize));
    return block ? block[index % this.blockSize] : undefined;
  }

  has(index: number): boolean {
    const block = this.blocks.get(Math.floor(index / this.blockSize));
    return !!block && index % this.blockSize < block.length;
  }

  isLoading(index: number): boolean {
    return this.inFlight.has(Math.floor(index / this.blockSize));
  }

  ensureRange(first: number, count: number): Promise<void> {
    if (count <= 0) return Promise.resolve();
    let lastIndex = first + count - 1;
    if (this.totalCount !== null) lastIndex = Math.min(lastIndex, this.totalCount - 1);
    const tasks: Promise<void>[] = [];
    for (let b = Math.floor(Math.max(0, first) / this.blockSize); lastIndex >= 0 && b <= Math.floor(lastIndex / this.blockSize); b++) {
      if (this.blocks.has(b)) continue;
      let t = this.inFlight.get(b);
      if (!t) {
        t = this.load(b, this.generation, this.controller.signal);
        this.inFlight.set(b, t);
      }
      tasks.push(t);
    }
    return Promise.all(tasks).then(() => undefined);
  }

  private async load(block: number, generation: number, signal: AbortSignal): Promise<void> {
    try {
      const result = await this.source.query({ ...this.current, offset: block * this.blockSize, count: this.blockSize }, signal);
      if (generation !== this.generation) return;
      this.blocks.set(block, [...result.items]);
      this.totalCount = result.totalCount;
      this.inFlight.delete(block);
      this.raise();
    } catch (err) {
      if (signal.aborted) return;
      if (generation !== this.generation) return;
      this.inFlight.delete(block);
      this.lastError = err;
      this.raise();
    }
  }

  private raise() {
    for (const l of this.listeners) l();
  }
}
