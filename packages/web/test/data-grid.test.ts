import { afterEach, describe, expect, it, vi } from 'vitest';
import '../src/index';
import type { SlDataGrid, DataGridColumn } from '../src/index';
import { createGridState, gridStateFromJson, gridStateToJson, setFilter, setSorts } from '../src/core/grid/state';
import { InMemoryGridDataSource } from '../src/core/grid/data-source';
import { toCsv } from '../src/core/grid/export';
import { describeFilter, draftFrom, filterFromDraft } from '../src/components/data-grid-cells';
import { cleanup, settle, shadow } from './helpers';

interface Row {
  id: number;
  name: string;
  status: 'Ready' | 'Building' | 'Failed';
  size: number;
  created: string;
  done: boolean;
  children?: Row[];
}

const statuses = ['Ready', 'Building', 'Failed'] as const;

function rows(n: number): Row[] {
  return Array.from({ length: n }, (_, i) => ({
    id: i + 1,
    name: `pkg-${String(i + 1).padStart(5, '0')}`,
    status: statuses[i % 3]!,
    size: (i * 37) % 1000,
    created: `2026-0${(i % 9) + 1}-1${i % 10}`,
    done: i % 2 === 0,
  }));
}

function columns(): DataGridColumn<Row>[] {
  return [
    { field: 'id', title: 'ID', type: 'number', width: 80, pinned: 'start' },
    {
      field: 'name', title: 'Name', flex: 1, editable: true,
      setter: (r: Row, v: unknown) => { r.name = String(v); },
      validate: (v: unknown) => (String(v ?? '').length < 2 ? 'Name is too short' : null),
    },
    { field: 'status', title: 'Status', type: 'enum', enumOrder: ['Ready', 'Building', 'Failed'], enumTones: { Ready: 'success', Failed: 'danger', Building: 'warning' } },
    { field: 'size', title: 'Size', type: 'number', aggregate: 'sum', editable: true, setter: (r: Row, v: unknown) => { r.size = Number(v); } },
    { field: 'created', title: 'Created', type: 'date', format: 'yyyy-MM-dd' },
    { field: 'done', title: 'Done', type: 'boolean' },
  ];
}

async function grid(props: Partial<SlDataGrid> = {}, items: Row[] = rows(50)): Promise<SlDataGrid> {
  const el = document.createElement('sl-data-grid') as SlDataGrid;
  el.rowKey = (r: unknown) => (r as Row).id;
  el.columns = columns();
  el.items = items;
  Object.assign(el, props);
  document.body.append(el);
  await settle();
  await el.updateComplete;
  return el;
}

const q = <T extends Element = HTMLElement>(el: Element, sel: string) => shadow(el).querySelector<T>(sel)!;
const qa = <T extends Element = HTMLElement>(el: Element, sel: string) => [...shadow(el).querySelectorAll<T>(sel)];
const viewport = (el: Element) => q(el, '.sl-data-grid__viewport');
const dataRows = (el: Element) => qa(el, '.sl-data-grid__row[part="row"]');
const headerCell = (el: Element, field: string) => q(el, `.sl-data-grid__header-cell[data-field="${field}"]`);
const press = async (el: SlDataGrid, key: string, init: KeyboardEventInit = {}) => {
  viewport(el).dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true, composed: true, cancelable: true, ...init }));
  await el.updateComplete;
};
const flush = () => new Promise((r) => setTimeout(r, 0));

afterEach(cleanup);

describe('sl-data-grid: rendering & ARIA', () => {
  it('renders a grid with column headers, rows and counts', async () => {
    const el = await grid();
    const vp = viewport(el);
    expect(vp.getAttribute('role')).toBe('grid');
    expect(vp.getAttribute('aria-rowcount')).toBe('51');
    expect(vp.getAttribute('aria-colcount')).toBe('6');
    const headers = qa(el, '[role="columnheader"]');
    expect(headers.map((h) => h.textContent!.trim().split(/\s+/)[0])).toEqual(['ID', 'Name', 'Status', 'Size', 'Created', 'Done']);
    expect(headerCell(el, 'name').getAttribute('aria-sort')).toBe('none');
    expect(dataRows(el)[0]!.getAttribute('aria-rowindex')).toBe('2');
    expect(qa(el, '[role="gridcell"]').length).toBeGreaterThan(0);
  });

  it('renders typed cells: mono numbers, enum badges with tones, booleans, dates', async () => {
    const el = await grid();
    const row = dataRows(el)[0]!;
    expect(row.querySelector('.sl-data-grid__cell--number')).not.toBeNull();
    const badge = row.querySelector('.sl-badge')!;
    expect(badge.textContent!.trim()).toBe('Ready');
    expect(badge.classList.contains('sl-tone-success')).toBe(true);
    expect(row.querySelector('.sl-data-grid__bool')).not.toBeNull();
    expect(row.querySelector('.sl-data-grid__cell--date')!.textContent!.trim()).toBe('2026-01-10');
  });

  it('pinned columns are sticky with offsets and an edge shadow hook', async () => {
    const el = await grid({ selectionMode: 'multi' });
    const id = dataRows(el)[0]!.querySelector<HTMLElement>('.is-pinned-start[part="cell"]')!;
    expect(id.style.left).toBe('44px'); // after the checkbox column
    expect(id.classList.contains('is-pinned-edge-start')).toBe(true);
  });

  it('virtualises rows: only the viewport window is in the DOM, and scrolling moves it', async () => {
    const el = await grid({}, rows(100_000));
    expect(viewport(el).getAttribute('aria-rowcount')).toBe('100001');
    const before = dataRows(el);
    expect(before.length).toBeGreaterThan(5);
    expect(before.length).toBeLessThan(60);
    const vp = viewport(el);
    vp.scrollTop = 32 * 50_000;
    vp.dispatchEvent(new Event('scroll'));
    await new Promise((r) => requestAnimationFrame(() => r(null)));
    await el.updateComplete;
    const indices = dataRows(el).map((r) => Number(r.getAttribute('aria-rowindex')));
    expect(Math.min(...indices)).toBeGreaterThan(49_900);
    expect(Math.max(...indices)).toBeLessThan(50_100);
  });

  it('measures: scrolling 100k rows re-renders only the window, within a frame budget', async () => {
    // Generous budget for happy-dom on CI (SLATE_PERF_FACTOR relaxes it); real-browser numbers come from
    // scripts/grid-perf.mjs. The pipeline is memoised, so a scroll re-render must not re-sort or re-scan 100k rows.
    const factor = Number(process.env.SLATE_PERF_FACTOR ?? '') || 1;
    const el = await grid({ selectionMode: 'multi', showFooter: true }, rows(100_000));
    const vp = viewport(el);
    const view = el.view;
    const times: number[] = [];
    let seed = 7;
    for (let i = 0; i < 40; i++) {
      seed = (seed * 16807) % 2147483647;
      vp.scrollTop = Math.floor((seed / 2147483647) * 32 * 99_000);
      const t = performance.now();
      el.tick++;
      await el.updateComplete;
      times.push(performance.now() - t);
      expect(dataRows(el).length).toBeLessThan(60);
    }
    times.sort((a, b) => a - b);
    const median = times[times.length >> 1]!;
    expect(median).toBeLessThan(50 * factor);
    expect(el.view).toBe(view); // same memoised pipeline result throughout
  });

  it('header, body and footer render the same column window with the same geometry', async () => {
    // 80 columns → column virtualisation; pinned start and end sections; probe scroll positions up to the end.
    const wide = Array.from({ length: 80 }, (_, i) => ({
      field: `c${i}`, title: `Column ${i}`, width: 90 + (i % 5) * 20, aggregate: 'count' as const,
      type: (i % 3 === 0 ? 'progress' : i % 3 === 1 ? 'number' : 'text') as 'progress' | 'number' | 'text',
      pinned: i === 0 ? ('start' as const) : i === 79 ? ('end' as const) : undefined,
      accessor: (r: Row) => (i % 3 === 2 ? r.name : r.size % 100),
    }));
    const el = await grid({ columns: wide as DataGridColumn<Row>[], showFooter: true, selectionMode: 'multi' }, rows(200));
    const vp = viewport(el);
    const geometry = (cell: HTMLElement) => `${cell.style.width}|${cell.style.left}|${cell.style.right}`;
    const total = 80 * 130;
    for (const left of [0, 900, total / 2, total - 1400, total - 960, total]) {
      vp.scrollLeft = left;
      vp.dispatchEvent(new Event('scroll'));
      await new Promise((r) => requestAnimationFrame(() => r(null)));
      await el.updateComplete;
      const header = new Map(qa(el, '.sl-data-grid__header-row .sl-data-grid__header-cell[data-field]').map((h) => [h.dataset.field!, h]));
      const footer = new Map(qa(el, '.sl-data-grid__row--footer .sl-data-grid__cell[data-field]').map((f) => [f.dataset.field!, f]));
      const body = [...dataRows(el)[0]!.querySelectorAll<HTMLElement>('.sl-data-grid__cell[data-field]')];
      expect(body.length).toBeGreaterThan(3);
      expect(body.length).toBeLessThan(40); // column window, not all 80
      expect([...header.keys()]).toEqual(body.map((b) => b.dataset.field));
      expect([...footer.keys()]).toEqual(body.map((b) => b.dataset.field));
      for (const cell of body) {
        const h = header.get(cell.dataset.field!)!;
        expect(geometry(h)).toBe(geometry(cell));
        expect(geometry(footer.get(cell.dataset.field!)!)).toBe(geometry(cell));
        expect(h.querySelector('.sl-data-grid__title')!.textContent).toBe(`Column ${cell.dataset.field!.slice(1)}`);
      }
      // Spacers keep the scrolling section the same width in every row.
      const spacers = (sel: string) => qa(el, `${sel} > .sl-data-grid__spacer`).map((x) => x.style.width).join();
      expect(spacers('.sl-data-grid__header-row')).toBe(spacers('.sl-data-grid__row[part="row"]:first-of-type'));
      expect(spacers('.sl-data-grid__row--footer')).toBe(spacers('.sl-data-grid__header-row'));
    }
    expect(headerCell(el, 'c79').classList.contains('is-pinned-end')).toBe(true);
    // Progress titles start-align (their bar fills from the start); numbers stay end-aligned.
    expect(headerCell(el, 'c78').classList.contains('sl-data-grid__header-cell--end')).toBe(false);
    expect(headerCell(el, 'c79').classList.contains('sl-data-grid__header-cell--end')).toBe(true);
  });

  it('virtualises columns in wide grids', async () => {
    const wide: DataGridColumn[] = Array.from({ length: 80 }, (_, i) => ({ field: `c${i}`, title: `C${i}`, width: 120 }));
    const data = Array.from({ length: 10 }, (_, r) => Object.fromEntries(wide.map((c, i) => [c.field, r * 100 + i])));
    const el = document.createElement('sl-data-grid') as SlDataGrid;
    el.columns = wide;
    el.items = data;
    document.body.append(el);
    await settle();
    const rendered = qa(el, '.sl-data-grid__header-cell[data-field]');
    expect(rendered.length).toBeLessThan(40);
    expect(qa(el, '.sl-data-grid__header-row .sl-data-grid__spacer').length).toBeGreaterThan(0);
  });

  it('shows an empty state with a clear-filters action', async () => {
    const el = await grid({ state: setFilter(createGridState(), { field: 'name', operator: 'equals', value: 'nothing' }) });
    expect(q(el, '[part="empty"]').textContent).toContain('No matching rows');
    q(el, '[part="empty"] button').click();
    await el.updateComplete;
    expect(el.state.filters).toEqual([]);
    expect(dataRows(el).length).toBeGreaterThan(0);
  });
});

describe('sl-data-grid: sorting, filtering, state', () => {
  it('header click cycles the sort and announces it; Shift adds secondary sorts with order badges', async () => {
    const el = await grid();
    const changes: unknown[] = [];
    el.addEventListener('sl-state-changed', (e) => changes.push((e as CustomEvent).detail.json));
    q(el, '[data-field="status"] .sl-data-grid__sort').click();
    await el.updateComplete;
    expect(el.state.sorts).toEqual([{ field: 'status', direction: 'ascending' }]);
    expect(headerCell(el, 'status').getAttribute('aria-sort')).toBe('ascending');
    expect(q(el, '[role="status"]').textContent).toContain('Sorted by Status');

    q(el, '[data-field="size"] .sl-data-grid__sort').dispatchEvent(new MouseEvent('click', { bubbles: true, shiftKey: true }));
    await el.updateComplete;
    expect(el.state.sorts.map((s) => s.field)).toEqual(['status', 'size']);
    expect(q(el, '[data-field="size"] .sl-data-grid__sort-order').textContent).toBe('2');
    expect(changes).toHaveLength(2);
    // engine order: Building, Failed, Ready (enum order is Ready, Building, Failed → sorted by enum order)
    const firstStatus = dataRows(el)[0]!.querySelector('.sl-badge')!.textContent!.trim();
    expect(firstStatus).toBe('Ready');
  });

  it('filters, quick filter and chips are wired to the engine', async () => {
    const el = await grid({ showToolbar: true });
    el.state = setFilter(el.state, { field: 'status', operator: 'anyOf', values: ['Failed'] });
    await el.updateComplete;
    expect(dataRows(el).every((r) => r.querySelector('.sl-badge')!.textContent!.trim() === 'Failed')).toBe(true);
    const chip = q(el, '.sl-data-grid__chip');
    expect(chip.textContent).toContain('Status: Failed');
    q(el, '.sl-data-grid__chip-remove').click();
    await el.updateComplete;
    expect(el.state.filters).toEqual([]);

    el.quickFilter = 'pkg-00007';
    await el.updateComplete;
    expect(el.state.quickFilter).toBe('pkg-00007');
    expect(dataRows(el)).toHaveLength(1);
    expect(q(el, '.sl-data-grid__status').textContent).toContain('1 of 50 rows');
  });

  it('filter editor drafts map to engine filters for every column kind', () => {
    const [id, name, status, , created, done] = columns();
    expect(filterFromDraft(name!, { ...draftFrom(name!, undefined), value: 'pkg' })).toEqual({ field: 'name', operator: 'contains', value: 'pkg' });
    expect(filterFromDraft(id!, { ...draftFrom(id!, undefined), operator: 'between', value: '5', value2: '9' }))
      .toEqual({ field: 'id', operator: 'between', value: 5, value2: 9 });
    expect(filterFromDraft(status!, { ...draftFrom(status!, undefined), values: ['Ready'] })).toEqual({ field: 'status', operator: 'anyOf', values: ['Ready'] });
    expect(filterFromDraft(created!, { ...draftFrom(created!, undefined), operator: 'lessThan', value: '2026-03-01' }))
      .toEqual({ field: 'created', operator: 'lessThan', value: '2026-03-01' });
    expect(filterFromDraft(done!, { ...draftFrom(done!, undefined), value: 'true' })).toEqual({ field: 'done', operator: 'equals', value: true });
    expect(filterFromDraft(name!, draftFrom(name!, undefined))).toBeNull(); // no value → no filter
    expect(filterFromDraft(name!, { ...draftFrom(name!, undefined), operator: 'isEmpty' })).toEqual({ field: 'name', operator: 'isEmpty' });
    expect(describeFilter(id!, { field: 'id', operator: 'greaterThan', value: 3 })).toBe('ID > 3');
    expect(describeFilter(done!, { field: 'done', operator: 'equals', value: false })).toBe('Done is No');
  });

  it('the filter popover applies a typed filter', async () => {
    const el = await grid();
    q(el, '[data-field="size"] sl-popover button[slot="anchor"]').click();
    await settle(shadow(el));
    const panel = q(el, '[data-field="size"] .sl-data-grid__panel');
    expect(panel).not.toBeNull();
    const field = panel.querySelector('sl-text-field')!;
    field.dispatchEvent(new CustomEvent('sl-value-changed', { detail: { value: '990' }, bubbles: true, composed: true }));
    await el.updateComplete;
    (q(el, '[data-field="size"] .sl-data-grid__panel sl-button[type="submit"]') as HTMLElement).click();
    await el.updateComplete;
    expect(el.state.filters).toEqual([{ field: 'size', operator: 'equals', value: 990 }]);
    expect(headerCell(el, 'size').classList.contains('is-filtered')).toBe(true);
  });

  it('state is two-way and JSON round-trips exactly', async () => {
    const el = await grid({ groupable: true });
    const state = setSorts(setFilter(createGridState({ pageSize: 25, groupBy: ['status'] }), { field: 'size', operator: 'greaterThan', value: 500 }), { field: 'name', direction: 'descending' });
    el.state = gridStateFromJson(JSON.stringify(gridStateToJson(state)));
    await el.updateComplete;
    expect(el.groupBy).toEqual(['status']);
    expect(el.pageSize).toBe(25);
    expect(gridStateToJson(el.state)).toEqual(gridStateToJson(state));
  });

  it('column menu actions pin, hide, group and autosize through state transitions', async () => {
    const el = await grid({ groupable: true });
    const menu = (field: string) => q(el, `[data-field="${field}"] sl-menu`);
    const act = async (field: string, action: string) => {
      const item = document.createElement('div');
      item.dataset.action = action;
      menu(field).dispatchEvent(new CustomEvent('sl-select', { detail: { item }, bubbles: true, composed: true }));
      await el.updateComplete;
    };
    await act('size', 'pin-end');
    expect(el.state.columns.find((c) => c.field === 'size')?.pinned).toBe('end');
    expect(headerCell(el, 'size').classList.contains('is-pinned-end')).toBe(true);
    await act('done', 'hide');
    expect(qa(el, '[data-field="done"]')).toHaveLength(0);
    await act('status', 'group');
    expect(el.groupBy).toEqual(['status']);
    await act('name', 'autosize');
    expect(el.state.columns.find((c) => c.field === 'name')?.width).toBeGreaterThan(48);
  });

  it('Alt+arrows on a header move the column', async () => {
    const el = await grid();
    const btn = q(el, '[data-field="status"] .sl-data-grid__sort');
    btn.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowLeft', altKey: true, bubbles: true, composed: true }));
    await el.updateComplete;
    const order = qa(el, '.sl-data-grid__header-cell[data-field]').map((h) => h.dataset.field);
    expect(order.indexOf('status')).toBeLessThan(order.indexOf('name'));
  });
});

describe('sl-data-grid: grouping, tree, detail, footer', () => {
  it('header groups add a header row and push the body below both rows', async () => {
    const cols = columns().map((c) => (c.field === 'status' || c.field === 'size' ? { ...c, headerGroup: 'Build' } : c));
    const el = await grid({ columns: cols });
    const group = q(el, '.sl-data-grid__header-groups');
    expect(group.textContent).toContain('Build');
    expect(viewport(el).getAttribute('aria-rowcount')).toBe('52'); // 2 header rows + 50
    const one = await grid();
    const offset = (g: SlDataGrid) => Number(/translateY\((\d+)px\)/.exec(q(g, '.sl-data-grid__rows').style.transform)![1]);
    expect(offset(el)).toBe(offset(one) * 2);
  });


  it('groups with counts, aggregates and collapse', async () => {
    const el = await grid({ groupable: true, groupBy: ['status'], showFooter: true }, rows(12));
    const groups = qa(el, '[part="group-row"]');
    expect(groups).toHaveLength(3);
    expect(groups[0]!.getAttribute('aria-expanded')).toBe('true');
    expect(groups[0]!.textContent).toContain('Status:');
    expect(groups[0]!.querySelector('.sl-data-grid__group-count')!.textContent).toBe('4');
    expect(groups[0]!.querySelector('.sl-data-grid__aggregate')!.textContent).toContain('Size');
    groups[0]!.querySelector<HTMLButtonElement>('.sl-data-grid__expander')!.click();
    await el.updateComplete;
    expect(qa(el, '[part="group-row"]')[0]!.getAttribute('aria-expanded')).toBe('false');
    const footer = q(el, '[part="footer"]');
    const sum = rows(12).reduce((a, r) => a + r.size, 0);
    expect(footer.textContent!.replace(/\s/g, '')).toContain(String(sum));
  });

  it('renders tree data as a treegrid with levels and expanders', async () => {
    const tree: Row[] = rows(3).map((r) => ({ ...r, children: rows(2).map((c) => ({ ...c, id: r.id * 100 + c.id })) }));
    const el = await grid({ childrenSelector: (r: unknown) => (r as Row).children }, tree);
    expect(viewport(el).getAttribute('role')).toBe('treegrid');
    let all = dataRows(el);
    expect(all).toHaveLength(9);
    expect(all[1]!.getAttribute('aria-level')).toBe('2');
    expect(all[0]!.getAttribute('aria-expanded')).toBe('true');
    all[0]!.querySelector<HTMLButtonElement>('.sl-data-grid__expander')!.click();
    await el.updateComplete;
    all = dataRows(el);
    expect(all).toHaveLength(7);
  });

  it('expands row details from a template function', async () => {
    const el = await grid({ rowDetail: (r: unknown) => `Details for ${(r as Row).name}` });
    dataRows(el)[0]!.querySelector<HTMLButtonElement>('.sl-data-grid__expander')!.click();
    await el.updateComplete;
    expect(q(el, '.sl-data-grid__row--detail').textContent).toContain('Details for pkg-00001');
    expect(el.state.expandedDetails).toEqual(['1']);
  });
});

describe('sl-data-grid: selection', () => {
  it('click / Ctrl / Shift selection and the tri-state header', async () => {
    const el = await grid({ selectionMode: 'multi' });
    const events: number[] = [];
    el.addEventListener('sl-selection-changed', (e) => events.push((e as CustomEvent).detail.count));
    const cell = (r: number) => dataRows(el)[r]!.querySelector<HTMLElement>('[part="cell"]')!;
    cell(0).click();
    await el.updateComplete;
    cell(3).dispatchEvent(new MouseEvent('click', { bubbles: true, composed: true, shiftKey: true }));
    await el.updateComplete;
    expect(el.selectedCount).toBe(4);
    cell(1).dispatchEvent(new MouseEvent('click', { bubbles: true, composed: true, ctrlKey: true }));
    await el.updateComplete;
    expect(el.selectedCount).toBe(3);
    expect((el.selectedItems as Row[]).map((r) => r.id)).toEqual([1, 3, 4]);
    expect(dataRows(el)[0]!.getAttribute('aria-selected')).toBe('true');
    const header = q<HTMLInputElement>(el, '[aria-label="Select all rows"]');
    expect(header.getAttribute('aria-checked')).toBe('mixed');
    header.dispatchEvent(new Event('change'));
    await el.updateComplete;
    expect(el.selectedCount).toBe(50);
    expect(q(el, '.sl-data-grid__selection-bar').textContent).toContain('50 selected');
    expect(events.at(-1)).toBe(50);
  });

  it('keyboard: Space toggles, Shift+Down extends, Ctrl+A selects all, Escape clears', async () => {
    const el = await grid({ selectionMode: 'multi' });
    await press(el, ' ');
    expect(el.selectedCount).toBe(1);
    await press(el, 'ArrowDown', { shiftKey: true });
    await press(el, 'ArrowDown', { shiftKey: true });
    expect(el.selectedCount).toBe(3);
    await press(el, 'a', { ctrlKey: true });
    expect(el.selectedCount).toBe(50);
    await press(el, 'Escape');
    expect(el.selectedCount).toBe(0);
  });

  it('selection survives sorting (keyed)', async () => {
    const el = await grid({ selectionMode: 'multi' });
    el.selectKeys(['7']);
    el.state = setSorts(el.state, { field: 'id', direction: 'descending' });
    await el.updateComplete;
    expect((el.selectedItems as Row[]).map((r) => r.id)).toEqual([7]);
  });
});

describe('sl-data-grid: keyboard navigation', () => {
  it('moves the active cell with arrows, Home/End and PageDown (aria-activedescendant)', async () => {
    const el = await grid();
    const active = () => shadow(el).getElementById(viewport(el).getAttribute('aria-activedescendant')!)!;
    expect(active().getAttribute('aria-colindex')).toBe('1');
    await press(el, 'ArrowRight');
    await press(el, 'ArrowDown');
    expect(active().getAttribute('aria-colindex')).toBe('2');
    expect(active().closest('[role="row"]')!.getAttribute('aria-rowindex')).toBe('3');
    await press(el, 'End');
    expect(active().getAttribute('aria-colindex')).toBe('6');
    await press(el, 'Home');
    expect(active().getAttribute('aria-colindex')).toBe('1');
    await press(el, 'PageDown');
    expect(Number(active().closest('[role="row"]')!.getAttribute('aria-rowindex'))).toBeGreaterThan(5);
    await press(el, 'End', { ctrlKey: true });
    expect(active().closest('[role="row"]')!.getAttribute('aria-rowindex')).toBe('51');
    expect(q(el, '.sl-data-grid').classList.contains('is-keyboard')).toBe(true);
  });

  it('Enter activates a row when the cell is not editable', async () => {
    const el = await grid();
    const spy = vi.fn();
    el.addEventListener('sl-row-activated', (e) => spy((e as CustomEvent).detail.key));
    await press(el, 'Enter');
    expect(spy).toHaveBeenCalledWith('1');
  });

  it('plus/minus and arrows expand and collapse groups', async () => {
    const el = await grid({ groupable: true, groupBy: ['status'] });
    await press(el, 'ArrowLeft');
    expect(el.state.toggledGroups).toHaveLength(1);
    await press(el, 'ArrowRight');
    expect(el.state.toggledGroups).toHaveLength(0);
  });
});

describe('sl-data-grid: editing', () => {
  it('cell mode: Enter edits, Enter commits and moves down; invalid values block the commit', async () => {
    const items = rows(10);
    const el = await grid({ editMode: 'cell' }, items);
    const committed = vi.fn();
    el.addEventListener('sl-cell-edit-committed', (e) => committed((e as CustomEvent).detail));
    await press(el, 'ArrowRight'); // Name
    await press(el, 'Enter');
    let editor = q<HTMLInputElement>(el, '.sl-data-grid__editor');
    expect(editor.value).toBe('pkg-00001');
    editor.value = 'x';
    editor.dispatchEvent(new Event('input'));
    await el.updateComplete;
    expect(q(el, '.sl-data-grid__cell-error').textContent).toContain('too short');
    editor.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, composed: true, cancelable: true }));
    await el.updateComplete;
    expect(q(el, '.sl-data-grid__editor')).not.toBeNull(); // still editing
    editor = q<HTMLInputElement>(el, '.sl-data-grid__editor');
    editor.value = 'renamed';
    editor.dispatchEvent(new Event('input'));
    editor.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, composed: true, cancelable: true }));
    await el.updateComplete;
    expect(items[0]!.name).toBe('renamed');
    expect(committed).toHaveBeenCalledWith(expect.objectContaining({ field: 'name', oldValue: 'pkg-00001', newValue: 'renamed' }));
    const active = shadow(el).getElementById(viewport(el).getAttribute('aria-activedescendant')!)!;
    expect(active.closest('[role="row"]')!.getAttribute('aria-rowindex')).toBe('3');
  });

  it('typing starts editing; Escape cancels', async () => {
    const el = await grid({ editMode: 'cell' });
    await press(el, 'ArrowRight');
    await press(el, 'z');
    const editor = q<HTMLInputElement>(el, '.sl-data-grid__editor');
    expect(editor.value).toBe('z');
    editor.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, composed: true, cancelable: true }));
    await el.updateComplete;
    expect(shadow(el).querySelector('.sl-data-grid__editor')).toBeNull();
  });

  it('batch mode collects changes behind the unsaved-changes bar', async () => {
    const items = rows(10);
    const el = await grid({ editMode: 'batch' }, items);
    for (const value of ['500', '600']) {
      await press(el, 'End'); // last column
      await press(el, 'Home');
      for (let i = 0; i < 3; i++) await press(el, 'ArrowRight'); // Size
      await press(el, 'Enter');
      const editor = q<HTMLInputElement>(el, '.sl-data-grid__editor');
      editor.value = value;
      editor.dispatchEvent(new Event('input'));
      editor.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, composed: true, cancelable: true }));
      await el.updateComplete;
    }
    expect(q(el, '.sl-data-grid__batch-bar').textContent).toContain('2 unsaved changes');
    expect(qa(el, '.is-dirty')).toHaveLength(2);
    expect(items[0]!.size).toBe(0); // not applied yet
    el.commitEdits();
    await el.updateComplete;
    expect(items[0]!.size).toBe(500);
    expect(items[1]!.size).toBe(600);
    expect(shadow(el).querySelector('.sl-data-grid__batch-bar')).toBeNull();
  });

  it('editable plain-field columns write back to the field without a setter', async () => {
    const items = rows(5);
    const cols = columns().map((c) => (c.field === 'name' ? { field: 'name', title: 'Name', editable: true } : c));
    const el = await grid({ editMode: 'cell', columns: cols }, items);
    await press(el, 'ArrowRight');
    await press(el, 'F2');
    const editor = q<HTMLInputElement>(el, '.sl-data-grid__editor');
    editor.value = 'renamed';
    editor.dispatchEvent(new Event('input'));
    editor.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, composed: true, cancelable: true }));
    await el.updateComplete;
    expect(items[0]!.name).toBe('renamed');
    expect(el.effectiveColumns).toBe(el.effectiveColumns); // stable identity: no pipeline rebuild per render
  });
});

describe('sl-data-grid: clipboard, export, live updates', () => {
  it('Ctrl+C copies the selection as TSV (Ctrl+Shift+C adds headers)', async () => {
    const el = await grid({ selectionMode: 'multi' });
    el.selectKeys(['2', '1']);
    const texts: string[] = [];
    el.addEventListener('sl-copy', (e) => texts.push((e as CustomEvent).detail.text));
    await press(el, 'c', { ctrlKey: true });
    await flush();
    expect(texts[0]!.split('\n')[0]).toBe('1\tpkg-00001\tReady\t0\t2026-01-10\ttrue');
    await press(el, 'c', { ctrlKey: true, shiftKey: true });
    await flush();
    expect(texts[1]!.split('\n')[0]).toBe('ID\tName\tStatus\tSize\tCreated\tDone');
  });

  it('exports CSV for the current view and lets listeners take over the download', async () => {
    const el = await grid();
    el.quickFilter = 'pkg-0001';
    await el.updateComplete;
    const csv = await el.exportText('csv', 'visible');
    expect(csv).toBe(toCsv(columns(), el.view!.items as Row[], true));
    const handler = vi.fn((e: Event) => e.preventDefault());
    el.addEventListener('sl-export', handler);
    await el.export('csv', 'all');
    expect(handler).toHaveBeenCalled();
    expect((handler.mock.calls[0]![0] as CustomEvent).detail.text.split('\n')).toHaveLength(51); // header + 50 rows
  });

  it('flashes rows whose item was replaced', async () => {
    const items = rows(10);
    const el = await grid({}, items);
    el.upsert([{ ...items[2]!, size: 999 }]);
    await el.updateComplete;
    const row = q(el, '.sl-data-grid__row[data-key="3"]');
    expect(row.classList.contains('is-flash')).toBe(true);
  });

  it('row tone tints rows', async () => {
    const el = await grid({ rowTone: (r: unknown) => ((r as Row).status === 'Failed' ? 'danger' : null) });
    const failed = q(el, '.sl-data-grid__row[data-key="3"]');
    expect(failed.classList.contains('sl-tone-danger')).toBe(true);
  });
});

describe('sl-data-grid: server mode & paging', () => {
  it('shows skeletons, loads blocks from the data source and re-queries on sort', async () => {
    const data = rows(1000);
    const source = new InMemoryGridDataSource<Row>(data, columns(), 0);
    const spy = vi.spyOn(source, 'query');
    const el = document.createElement('sl-data-grid') as SlDataGrid;
    el.rowKey = (r: unknown) => (r as Row).id;
    el.columns = columns();
    el.selectionMode = 'multi';
    el.dataSource = source;
    document.body.append(el);
    await el.updateComplete;
    expect(qa(el, '.sl-data-grid__skeleton').length).toBeGreaterThan(0);
    await flush();
    await el.updateComplete;
    await flush();
    await el.updateComplete;
    expect(viewport(el).getAttribute('aria-rowcount')).toBe('1001');
    expect(dataRows(el)[0]!.textContent).toContain('pkg-00001');
    q(el, '[data-field="id"] .sl-data-grid__sort').click();
    q(el, '[data-field="id"] .sl-data-grid__sort').click(); // descending
    await el.updateComplete;
    await flush();
    await el.updateComplete;
    expect(dataRows(el)[0]!.textContent).toContain('pkg-01000');
    expect(spy.mock.calls.at(-1)![0].sorts).toEqual([{ field: 'id', direction: 'descending' }]);

    q<HTMLInputElement>(el, '[aria-label="Select all rows"]').dispatchEvent(new Event('change'));
    await el.updateComplete;
    expect(el.selectedCount).toBe(1000);
  });

  it('pages through rows with sl-pagination', async () => {
    const el = await grid({ pagination: 'pages', pageSize: 20 });
    expect(dataRows(el)).toHaveLength(20);
    const pager = q(el, '[part="pager"] sl-pagination');
    pager.dispatchEvent(new CustomEvent('sl-page-changed', { detail: { page: 3 }, bubbles: true, composed: true }));
    await el.updateComplete;
    expect(el.state.pageIndex).toBe(2);
    expect(dataRows(el)).toHaveLength(10);
    expect(dataRows(el)[0]!.getAttribute('aria-rowindex')).toBe('42');
  });
});

describe('sl-data-grid: declarative columns', () => {
  it('reads columns from slotted elements', async () => {
    const wrapper = document.createElement('div');
    wrapper.innerHTML = `<sl-data-grid>
      <div slot="columns" field="name" title="Package"></div>
      <div slot="columns" field="size" type="number" aggregate="sum"></div>
    </sl-data-grid>`;
    document.body.append(wrapper);
    const el = wrapper.firstElementChild as SlDataGrid;
    el.items = rows(3);
    await settle();
    await el.updateComplete;
    expect(qa(el, '.sl-data-grid__header-cell[data-field]').map((h) => h.dataset.field)).toEqual(['name', 'size']);
    expect(dataRows(el)).toHaveLength(3);
  });
});
