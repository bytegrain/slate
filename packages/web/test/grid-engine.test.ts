import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { describe, expect, it } from 'vitest';
import {
  computeViewport, createGridState, csvField, DataPipeline, EditSession, estimateColumnWidth, formatValue, gridStateFromJson,
  gridStateToJson, GridRowCache, InMemoryGridDataSource, moveCell, pageRows, parseIsoDate, resolveColumns, scrollingColumnRange,
  scrollToReveal, SelectionModel, serializeGridState, toCsv, toTsv, tsvField,
  addGroupBy, clearFilter, clearFilters, collapseAll, expandAll, moveColumn, pinColumn, removeGroupBy, resizeColumn,
  setColumnHidden, setFilter, setGroupExpanded, setPage, setPageSize, setQuickFilter, toggleDetail, toggleGroup, toggleSort,
  type GridAggregate, type GridColumn, type GridColumnType, type GridDataSource, type GridFilter, type GridKey, type GridPin,
  type GridQuery, type GridResult, type GridRowKind, type GridState,
} from '../src/core/grid';

// ---- shared fixture (produced by Slate.Core.Tests DataGridFixtureTests) ---------------------------------------

type Row = Record<string, unknown>;
/* eslint-disable @typescript-eslint/no-explicit-any */
const repo = resolve(import.meta.dirname, '../../..');
const fixture: any = JSON.parse(readFileSync(resolve(repo, 'tests/fixtures/data-grid.json'), 'utf8'));

function columnsFrom(specs: any[]): GridColumn<Row>[] {
  return specs.map((s) => {
    const type = s.type as GridColumnType;
    const c: GridColumn<Row> = {
      field: s.field,
      type,
      accessor: (row) => {
        const v = row[s.field];
        if (v === null || v === undefined) return null;
        return type === 'date' && typeof v === 'string' ? new Date(parseIsoDate(v)!) : v;
      },
    };
    if (s.title !== undefined) c.title = s.title;
    if (s.width !== undefined) c.width = s.width;
    if (s.flex !== undefined) c.flex = s.flex;
    if (s.minWidth !== undefined) c.minWidth = s.minWidth;
    if (s.searchable !== undefined) c.searchable = s.searchable;
    if (s.hidden !== undefined) c.hidden = s.hidden;
    if (s.pinned !== undefined) c.pinned = s.pinned as GridPin;
    if (s.format !== undefined) c.format = s.format;
    if (s.aggregate !== undefined) c.aggregate = s.aggregate as GridAggregate;
    if (s.enumOrder !== undefined) c.enumOrder = s.enumOrder;
    return c;
  });
}

const cols = columnsFrom(fixture.columns);
const rows: Row[] = fixture.data;
const colBy = new Map(cols.map((c) => [c.field, c] as const));
const cases = (list: any[]): [string, any][] => list.map((c: any) => [c.id as string, c]);

function aggs(a: Readonly<Record<string, unknown>>): Record<string, string | null> {
  const out: Record<string, string | null> = {};
  for (const k of Object.keys(a).sort()) {
    const v = a[k];
    const c = colBy.get(k)!;
    out[k] = v === null || v === undefined ? null : c.aggregate === 'count' ? String(v) : formatValue(v, c.type!, c.format);
  }
  return out;
}

function runPipeline(state: GridState, tree: boolean, paginate: boolean) {
  const byParent = new Map<unknown, Row[]>();
  for (const r of rows) {
    const p = r.parentId ?? null;
    if (!byParent.has(p)) byParent.set(p, []);
    byParent.get(p)!.push(r);
  }
  const pipeline = new DataPipeline<Row>(cols, {
    rowKey: (r) => r.id,
    childrenSelector: tree ? (r) => byParent.get(r.id) ?? [] : undefined,
    paginate,
  });
  const input = tree ? byParent.get(null) ?? [] : rows;
  const result = pipeline.run(input, state);
  return {
    rows: result.rows.map((r) =>
      r.kind === 'group'
        ? { k: 'g', key: r.key, depth: r.depth, field: r.groupField, text: r.groupKeyText, count: r.rowCount, expanded: r.expanded, agg: aggs(r.aggregates!) }
        : r.kind === 'detail'
          ? { k: 'x', key: r.key, depth: r.depth }
          : { k: 'd', key: r.key, depth: r.depth, children: r.hasChildren, expanded: r.expanded }),
    keys: result.itemKeys,
    visibleKeys: result.visibleKeys,
    total: result.totalCount,
    filtered: result.filteredCount,
    viewRows: result.viewRowCount,
    pageIndex: result.pageIndex,
    pageCount: result.pageCount,
    totals: aggs(result.totals),
  };
}

describe('data grid engine — C# parity (tests/fixtures/data-grid.json)', () => {
  it.each(cases(fixture.pipeline))('pipeline: %s', (_id, c: any) => {
    expect(runPipeline(gridStateFromJson(c.state), c.tree, c.paginate)).toEqual(c.expected);
  });

  it('state transitions produce identical JSON', () => {
    let s = createGridState();
    for (const { step, state } of fixture.transitions) {
      switch (step.op) {
        case 'toggleSort': s = toggleSort(s, step.field, step.additive); break;
        case 'setFilter': s = setFilter(s, step.filter as GridFilter); break;
        case 'clearFilter': s = clearFilter(s, step.field); break;
        case 'setQuickFilter': s = setQuickFilter(s, step.text); break;
        case 'moveColumn': s = moveColumn(s, step.order, step.field, step.toIndex); break;
        case 'resizeColumn': s = resizeColumn(s, step.field, step.width, step.min, step.max); break;
        case 'pinColumn': s = pinColumn(s, step.field, step.pin); break;
        case 'setColumnHidden': s = setColumnHidden(s, step.field, step.hidden); break;
        case 'addGroupBy': s = addGroupBy(s, step.field); break;
        case 'removeGroupBy': s = removeGroupBy(s, step.field); break;
        case 'toggleGroup': s = toggleGroup(s, step.id); break;
        case 'setGroupExpanded': s = setGroupExpanded(s, step.id, step.expanded); break;
        case 'collapseAll': s = collapseAll(s); break;
        case 'expandAll': s = expandAll(s); break;
        case 'toggleDetail': s = toggleDetail(s, step.key); break;
        case 'setPageSize': s = setPageSize(s, step.size); break;
        case 'setPage': s = setPage(s, step.index); break;
        case 'clearFilters': s = clearFilters(s); break;
        default: throw new Error('unknown op ' + step.op);
      }
      expect(gridStateToJson(s), step.op).toEqual(state);
      expect(JSON.stringify(gridStateToJson(s))).toBe(JSON.stringify(state)); // same key order too
    }
  });

  it.each(cases(fixture.selection))('selection: %s', (_id, c: any) => {
    const m = new SelectionModel<string>(c.mode);
    for (const step of c.steps) {
      switch (step.op) {
        case 'click': m.click(step.key, c.order, step.ctrl, step.shift); break;
        case 'toggle': m.toggle(step.key); break;
        case 'extendTo': m.extendTo(step.key, c.order); break;
        case 'selectAll': m.selectAll(c.order); break;
        case 'selectAllMatching': m.selectAllMatching(Number(step.key)); break;
        case 'clear': m.clear(); break;
      }
      expect({ selected: m.selectedIn(c.order), anchor: m.hasAnchor ? m.anchor : null, count: m.count, header: m.headerState(c.order) })
        .toEqual({ selected: step.selected, anchor: step.anchor, count: step.count, header: step.header });
    }
  });

  it.each(cases(fixture.navigation))('navigation: %s', (_id, c: any) => {
    const expanded = new Set<number>(c.expanded);
    const kinds: Record<string, GridRowKind> = { d: 'data', g: 'group', x: 'detail' };
    const ctx = {
      rowCount: c.kinds.length, columnCount: c.columns, pageRows: c.pageRows,
      kind: (i: number) => kinds[c.kinds[i]]!,
      canExpand: (i: number) => c.expandable.includes(i),
      isExpanded: (i: number) => expanded.has(i),
    };
    let cell = c.start;
    for (const step of c.steps) {
      const r = moveCell(cell, step.key as GridKey, ctx);
      if (r.action === 'expand') expanded.add(r.cell.row);
      if (r.action === 'collapse') expanded.delete(r.cell.row);
      cell = r.cell;
      expect({ key: step.key, row: cell.row, column: cell.column, action: r.action }).toEqual(step);
    }
  });

  it.each(cases(fixture.export.filter((c: any) => c.state)))('export: %s', (_id, c: any) => {
    const state = gridStateFromJson(c.state);
    const visible = resolveColumns(cols, state).columns.map((x) => x.column);
    expect(visible.map((x) => x.field)).toEqual(c.columns);
    const items = new DataPipeline<Row>(cols, { rowKey: (r) => r.id }).run(rows, state).items.slice(0, c.take);
    expect(toCsv(visible, items)).toBe(c.csv);
    expect(toTsv(visible, items, true)).toBe(c.tsv);
  });

  it('export: field quoting', () => {
    const q = fixture.export.find((c: any) => c.id === 'csv-quoting');
    expect(q.fields.map(csvField)).toEqual(q.csv);
    expect(q.fields.map(tsvField)).toEqual(q.tsv);
  });

  it('viewport maths', () => {
    for (const v of fixture.viewport) {
      const r = computeViewport(v.scrollTop, v.viewportHeight, v.rowHeight, v.rowCount, v.overscan);
      expect({ first: r.first, count: r.count, offsetTop: r.offsetTop, totalHeight: r.totalHeight })
        .toEqual({ first: v.first, count: v.count, offsetTop: v.offsetTop, totalHeight: v.totalHeight });
      expect([0, 5, 20, 50].map((i) => scrollToReveal(i, Math.max(0, v.scrollTop), v.viewportHeight, v.rowHeight))).toEqual(v.reveal);
      expect(pageRows(v.viewportHeight, v.rowHeight)).toBe(v.pageRows);
    }
  });

  it.each(cases(fixture.layout))('layout: %s', (_id, c: any) => {
    const l = resolveColumns(cols, gridStateFromJson(c.state), c.availableWidth);
    expect(l.totalWidth).toBe(c.totalWidth);
    expect(l.order).toEqual(c.order);
    expect(l.columns.map((x) => ({ field: x.field, width: x.width, pin: x.pin, index: x.index, left: x.left, sticky: x.stickyOffset }))).toEqual(c.columns);
    expect(scrollingColumnRange(l, 150, 400, 50)).toEqual(c.scrollingRange);
    for (const column of cols) {
      expect(estimateColumnWidth(column, ['x', 'medium text', 'a considerably longer cell value'])).toBe(c.estimates[column.field]);
    }
  });

  it('formatting', () => {
    for (const f of fixture.format) {
      const v = f.type === 'date' && typeof f.value === 'string' ? new Date(parseIsoDate(f.value)!) : f.value;
      expect(formatValue(v, f.type, f.format), JSON.stringify(f)).toBe(f.text);
    }
  });
});

// ---- behaviour beyond the fixture --------------------------------------------------------------------------

interface Person { id: number; name: string | null; salary: number | null; reports?: Person[] }

const people: Person[] = [
  { id: 1, name: 'Ada', salary: 120 },
  { id: 2, name: 'bob', salary: null },
  { id: 3, name: 'Cy', salary: 90 },
];
const personCols: GridColumn<Person>[] = [
  { field: 'id', type: 'number', searchable: false },
  { field: 'name' },
  {
    field: 'salary', type: 'number', aggregate: 'sum', editable: true,
    setter: (p, v) => { p.salary = v as number | null; },
    validate: (v) => (typeof v === 'number' && v < 0 ? 'Must be positive.' : null),
  },
];

describe('data grid engine — behaviour', () => {
  it('caches results until the state, items or version change', () => {
    const p = new DataPipeline<Person>(personCols);
    const s = toggleSort(createGridState(), 'name');
    const a = p.run(people, s);
    expect(p.run(people, { ...s })).toBe(a);
    expect(p.run(people, gridStateFromJson(serializeGridState(s)))).toBe(a);
    p.invalidate();
    expect(p.run(people, s)).not.toBe(a);
  });

  it('round-trips state JSON and rejects bad enums', () => {
    const s = setFilter(toggleSort(createGridState({ pageSize: 25 }), 'name'), { field: 'salary', operator: 'between', value: 1, value2: '9' });
    expect(gridStateFromJson(serializeGridState(s))).toEqual(s);
    expect(() => gridStateFromJson('{"sorts":[{"field":"a","direction":"sideways"}]}')).toThrow();
    expect(() => gridStateFromJson('[]')).toThrow();
  });

  it('edits in cell mode with parse and validation errors', () => {
    const p = { ...people[0]! };
    const s = new EditSession<Person>('cell');
    const committed: unknown[] = [];
    s.onCommitted((c) => committed.push(c.newValue));
    expect(s.begin(p, '1', personCols[2]!)).toBe(true);
    s.setDraftText('abc');
    expect(s.current!.error).toBe('Enter a number.');
    expect(s.commit()).toBe(false);
    s.setDraftText('-1');
    expect(s.current!.error).toBe('Must be positive.');
    s.setDraftText('42');
    expect(s.commit()).toBe(true);
    expect(p.salary).toBe(42);
    expect(committed).toEqual([42]);
    expect(s.begin(p, '1', personCols[1]!)).toBe(false); // not editable
  });

  it('collects batch edits until commitAll / discardAll', () => {
    const p = { ...people[0]! };
    const s = new EditSession<Person>('batch');
    s.begin(p, '1', personCols[2]!, '200');
    s.commit();
    expect(s.pending).toHaveLength(1);
    expect(p.salary).toBe(120);
    expect(s.getValue(p, '1', personCols[2]!)).toBe(200);
    s.commitAll(personCols);
    expect(p.salary).toBe(200);
    s.begin(p, '1', personCols[2]!, '5');
    s.commit();
    s.discardAll();
    expect(p.salary).toBe(200);
    expect(EditSession.moveAfterCommit('tab', true)).toBe('left');
  });

  it('serves windows from an in-memory data source', async () => {
    const many = Array.from({ length: 500 }, (_, i) => ({ id: i, name: 'p' + i, salary: 500 - i }));
    const src = new InMemoryGridDataSource<Person>(many, personCols);
    const r = await src.query({ sorts: [{ field: 'salary', direction: 'ascending' }], filters: [{ field: 'salary', operator: 'lessThanOrEqual', value: 100 }], quickFilter: '', offset: 10, count: 5 });
    expect(r.totalCount).toBe(100);
    expect(r.items.map((x) => x.salary)).toEqual([11, 12, 13, 14, 15]);
  });

  it('row cache loads blocks once, de-dupes, and discards stale results', async () => {
    const calls: { q: GridQuery; resolve: (r: GridResult<number>) => void; signal?: AbortSignal }[] = [];
    const source: GridDataSource<number> = {
      query: (q, signal) => new Promise((res, rej) => {
        calls.push({ q, resolve: res, signal });
        signal?.addEventListener('abort', () => rej(new Error('aborted')));
      }),
    };
    const done = (i: number, total: number) =>
      calls[i]!.resolve({ items: Array.from({ length: Math.min(calls[i]!.q.count, total - calls[i]!.q.offset) }, (_, k) => calls[i]!.q.offset + k), totalCount: total });

    const cache = new GridRowCache<number>(source, 50);
    const a = cache.ensureRange(30, 40);
    const b = cache.ensureRange(60, 10);
    expect(calls).toHaveLength(2);
    done(0, 120); done(1, 120);
    await Promise.all([a, b]);
    expect(cache.totalCount).toBe(120);
    expect(cache.get(99)).toBe(99);
    expect(cache.has(100)).toBe(false);

    const stale = cache.ensureRange(100, 10);
    cache.setQuery({ sorts: [], filters: [], quickFilter: 'x', offset: 0, count: 100 });
    expect(calls[2]!.signal!.aborted).toBe(true);
    await stale;
    expect(cache.totalCount).toBeNull();
  });

  it('rejects ambiguous dates and parses offsets', () => {
    expect(parseIsoDate('2026-02-30')).toBeNull();
    expect(parseIsoDate('tomorrow')).toBeNull();
    expect(parseIsoDate('2026-01-01T02:00:00+02:00')).toBe(Date.UTC(2026, 0, 1));
  });
});

describe('data grid engine — performance', () => {
  const factor = Number(process.env.SLATE_PERF_FACTOR ?? '') || 1;

  interface Rec { id: number; name: string; team: string; amount: number; when: Date; flag: boolean }
  let seed = 42;
  const rnd = () => ((seed = (seed * 1664525 + 1013904223) >>> 0) / 2 ** 32);
  const teams = ['Platform', 'Web', 'Desktop', 'Tokens', 'Infra', 'Data'];
  const data: Rec[] = Array.from({ length: 100_000 }, (_, i) => ({
    id: i, name: 'Item ' + Math.floor(rnd() * 50_000), team: teams[Math.floor(rnd() * 6)]!,
    amount: Math.round(rnd() * 1_000_000) / 100, when: new Date(Date.UTC(2020, 0, 1) + Math.floor(rnd() * 3_000_000) * 60_000), flag: rnd() < 0.5,
  }));
  const recCols: GridColumn<Rec>[] = [
    { field: 'id', type: 'number', searchable: false },
    { field: 'name' },
    { field: 'team', type: 'enum' },
    { field: 'amount', type: 'number', aggregate: 'sum', searchable: false },
    { field: 'when', type: 'date', searchable: false },
    { field: 'flag', type: 'boolean', searchable: false },
  ];
  const best = (fn: () => void) => {
    let min = Infinity;
    for (let i = 0; i < 3; i++) {
      const t = performance.now();
      fn();
      min = Math.min(min, performance.now() - t);
    }
    return min;
  };

  it('sorts + filters + quick-filters 100k rows within budget', () => {
    let s = toggleSort(toggleSort(createGridState(), 'team'), 'amount', true);
    s = setQuickFilter(setFilter(s, { field: 'amount', operator: 'greaterThan', value: 1000 }), 'item 1');
    let filtered = 0;
    const ms = best(() => { filtered = new DataPipeline<Rec>(recCols).run(data, s).filteredCount; });
    expect(filtered).toBeGreaterThan(0);
    expect(ms, `took ${ms.toFixed(0)} ms`).toBeLessThan(150 * factor * 2); // JS budget: 2× the .NET budget
  });

  it('groups 100k rows within budget', () => {
    const s = createGridState({ groupBy: ['team'] });
    let groups = 0;
    const ms = best(() => { groups = new DataPipeline<Rec>(recCols).run(data, s).rows.filter((r) => r.kind === 'group').length; });
    expect(groups).toBe(6);
    expect(ms, `took ${ms.toFixed(0)} ms`).toBeLessThan(200 * factor * 2);
  });
});
