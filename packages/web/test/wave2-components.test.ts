import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest';
import type { SlTab, SlTabPanel, SlTabs } from '../src/components/tabs';
import type { SlDatePicker } from '../src/components/date-picker';
import type { SlTreeView } from '../src/components/tree-view';
import type { SlAvatar, SlBreadcrumbs, SlPagination, SlSegmented, SlSkeleton, SlSlider } from '../src/components/widgets';
import type { SlSelect } from '../src/components/select';
import { $, cleanup, fixture, key, settle, shadow } from './helpers';

const frame = () => new Promise<void>((r) => requestAnimationFrame(() => r()));

afterEach(cleanup);

describe('sl-tabs', () => {
  const markup = `<sl-tabs value="b">
      <sl-tab key="a" label="Overview"></sl-tab>
      <sl-tab key="b" label="Logs" badge="12"></sl-tab>
      <sl-tab key="c" label="Disabled" disabled></sl-tab>
      <sl-tab key="d" label="Settings" closable></sl-tab>
      <sl-tab-panel key="a">A</sl-tab-panel>
      <sl-tab-panel key="b">B</sl-tab-panel>
      <sl-tab-panel key="c">C</sl-tab-panel>
      <sl-tab-panel key="d">D</sl-tab-panel>
    </sl-tabs>`;

  it('wires tabs and panels with ARIA and shows only the selected panel', async () => {
    const el = await fixture<SlTabs>(markup);
    await settle();
    const tabs = [...el.querySelectorAll<SlTab>('sl-tab')];
    const panels = [...el.querySelectorAll<SlTabPanel>('sl-tab-panel')];
    expect($(el, '[part="list"]').getAttribute('role')).toBe('tablist');
    expect(tabs.map((t) => t.getAttribute('role'))).toEqual(['tab', 'tab', 'tab', 'tab']);
    expect(tabs[1].getAttribute('aria-selected')).toBe('true');
    expect(tabs[1].tabIndex).toBe(0);
    expect(tabs[0].tabIndex).toBe(-1);
    expect(tabs[1].getAttribute('aria-controls')).toBe(panels[1].id);
    expect(panels[1].getAttribute('aria-labelledby')).toBe(tabs[1].id);
    expect(panels.map((p) => p.hidden)).toEqual([true, false, true, true]);
    expect(panels[1].getAttribute('role')).toBe('tabpanel');
    expect(shadow(tabs[1]).querySelector('.sl-tab__badge')?.textContent).toBe('12');
  });

  it('arrow keys move and select (skipping disabled, wrapping) and fire sl-value-changed', async () => {
    const el = await fixture<SlTabs>(markup);
    await settle();
    const tabs = [...el.querySelectorAll<SlTab>('sl-tab')];
    const values: string[] = [];
    el.addEventListener('sl-value-changed', (e) => values.push((e as CustomEvent<{ value: string }>).detail.value));
    tabs[1].focus();
    key(tabs[1], 'ArrowRight');
    await settle();
    expect(el.value).toBe('d');
    expect(document.activeElement).toBe(tabs[3]);
    key(tabs[3], 'ArrowRight');
    await settle();
    expect(el.value).toBe('a');
    key(tabs[0], 'End');
    await settle();
    expect(values).toEqual(['d', 'a', 'd']);
  });

  it('Delete on a closable tab fires sl-closed', async () => {
    const el = await fixture<SlTabs>(markup);
    await settle();
    const tab = el.querySelector<SlTab>('sl-tab[key="d"]')!;
    const closed: string[] = [];
    el.addEventListener('sl-closed', (e) => closed.push((e as CustomEvent<{ key: string }>).detail.key));
    tab.focus();
    key(tab, 'Delete');
    expect(closed).toEqual(['d']);
  });

  it('vertical tabs use Up/Down and propagate variant and size to tabs', async () => {
    const el = await fixture<SlTabs>(`<sl-tabs direction="column" variant="pills" size="small" value="a">
      <sl-tab key="a" label="A"></sl-tab><sl-tab key="b" label="B"></sl-tab>
      <sl-tab-panel key="a"></sl-tab-panel><sl-tab-panel key="b"></sl-tab-panel></sl-tabs>`);
    await settle();
    const [a, b] = [...el.querySelectorAll<SlTab>('sl-tab')];
    expect($(el, '[part="list"]').getAttribute('aria-orientation')).toBe('vertical');
    expect($(a, '[part="tab"]').className).toContain('sl-tab--pills');
    expect($(a, '[part="tab"]').className).toContain('sl-tab--small');
    a.focus();
    key(a, 'ArrowDown');
    await settle();
    expect(el.value).toBe('b');
    expect(document.activeElement).toBe(b);
  });
});

describe('sl-date-picker', () => {
  it('opens the calendar, navigates with the keyboard and picks a day', async () => {
    const el = await fixture<SlDatePicker>('<sl-date-picker label="Due" value="2026-10-06" format="yyyy-MM-dd"></sl-date-picker>');
    const values: Array<string | null> = [];
    el.addEventListener('sl-value-changed', (e) => values.push((e as CustomEvent<{ value: string | null }>).detail.value));
    ($(el, '.sl-date-picker__trigger') as HTMLButtonElement).click();
    await settle();
    expect(el.open).toBe(true);
    const grid = $(el, '[role="grid"]');
    expect(shadow(el).querySelector('.sl-calendar__title')?.textContent).toMatch(/2026/);
    expect(shadow(el).querySelector('.sl-calendar__day[tabindex="0"]')?.getAttribute('data-date')).toBe('2026-10-06');

    key(grid, 'ArrowRight');
    await settle();
    expect(el.focused).toBe('2026-10-07');
    key(grid, 'ArrowDown');
    await settle();
    expect(el.focused).toBe('2026-10-14');
    key(grid, 'PageDown');
    await settle();
    expect(el.focused).toBe('2026-11-14');
    expect(el.view).toEqual({ year: 2026, month: 11 });
    key(grid, 'Enter');
    await settle();
    expect(el.value).toBe('2026-11-14');
    expect(el.open).toBe(false);
    expect(values).toEqual(['2026-11-14']);
    expect(($(el, 'input') as HTMLInputElement).value).toBe('2026-11-14');
  });

  it('disables days outside min/max and skips them when navigating', async () => {
    const el = await fixture<SlDatePicker>('<sl-date-picker value="2026-10-10" min="2026-10-05" max="2026-10-12" inline></sl-date-picker>');
    const day = (d: string) => shadow(el).querySelector<HTMLButtonElement>(`[data-date="${d}"]`)!;
    expect(day('2026-10-04').disabled).toBe(true);
    expect(day('2026-10-13').disabled).toBe(true);
    expect(day('2026-10-05').disabled).toBe(false);
    key($(el, '[role="grid"]'), 'ArrowDown');
    await settle();
    expect(el.focused).toBe('2026-10-12'); // clamped to max
  });

  it('range selection takes two picks and fires sl-range-changed', async () => {
    const el = await fixture<SlDatePicker>('<sl-date-picker selection="range" inline></sl-date-picker>');
    el.view = { year: 2026, month: 10 };
    await settle();
    const ranges: unknown[] = [];
    el.addEventListener('sl-range-changed', (e) => ranges.push((e as CustomEvent).detail.range));
    el.pick('2026-10-20');
    el.pick('2026-10-12');
    await settle();
    expect(el.range).toEqual({ start: '2026-10-12', end: '2026-10-20' });
    expect(ranges).toEqual([{ start: '2026-10-12', end: '2026-10-20' }]);
    expect(shadow(el).querySelector('[data-date="2026-10-15"]')?.classList.contains('is-in-range')).toBe(true);
    expect(shadow(el).querySelector('[data-date="2026-10-12"]')?.classList.contains('is-range-start')).toBe(true);
  });

  it('presets and typed dates set the value', async () => {
    const el = await fixture<SlDatePicker>('<sl-date-picker format="dd/MM/yyyy" presets=\'[{"label":"Today","kind":"today"}]\'></sl-date-picker>');
    const input = $(el, 'input') as HTMLInputElement;
    input.value = '24/12/2026';
    input.dispatchEvent(new Event('input'));
    input.dispatchEvent(new Event('change'));
    await settle();
    expect(el.value).toBe('2026-12-24');
    expect(input.value).toBe('24/12/2026');

    (shadow(el).querySelector('.sl-calendar__preset') as HTMLButtonElement).click();
    await settle();
    const now = new Date();
    expect(el.value).toBe(`${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}-${String(now.getDate()).padStart(2, '0')}`);
  });

  it('day buttons have full accessible names and today is marked', async () => {
    const el = await fixture<SlDatePicker>('<sl-date-picker inline></sl-date-picker>');
    const today = shadow(el).querySelector('[aria-current="date"]');
    expect(today).not.toBeNull();
    expect(today?.getAttribute('aria-label')?.length).toBeGreaterThan(8);
  });
});

describe('sl-tree-view', () => {
  const tree = [
    { id: 'src', label: 'src', icon: 'folder', children: [{ id: 'app', label: 'App.razor', icon: 'file' }, { id: 'btn', label: 'Button.razor', icon: 'file' }] },
    { id: 'docs', label: 'docs', icon: 'folder', children: [{ id: 'readme', label: 'README.md' }] },
    { id: 'license', label: 'LICENSE' },
  ];

  async function make(attrs = '') {
    const el = (await fixture<SlTreeView>(`<sl-tree-view ${attrs}></sl-tree-view>`)) as SlTreeView;
    el.items = structuredClone(tree);
    await settle();
    return el;
  }

  const items = (el: SlTreeView) => [...shadow(el).querySelectorAll<HTMLElement>('[role="treeitem"]')];
  const byLabel = (el: SlTreeView, label: string) => items(el).find((r) => r.querySelector(':scope > .sl-tree__row > .sl-tree__label')?.textContent === label)!;

  it('renders a tree with levels, set sizes and a single tab stop', async () => {
    const el = await make();
    const rows = items(el);
    expect($(el, '[role="tree"]')).toBeTruthy();
    expect(rows.map((r) => r.getAttribute('aria-level'))).toEqual(['1', '1', '1']);
    expect(rows[0].getAttribute('aria-expanded')).toBe('false');
    expect(rows[2].hasAttribute('aria-expanded')).toBe(false);
    expect(rows.filter((r) => r.tabIndex === 0)).toHaveLength(1);
  });

  it('keyboard: Right expands then enters, Left returns, Enter activates', async () => {
    const el = await make();
    const activated: string[] = [];
    el.addEventListener('sl-item-activated', (e) => activated.push((e as CustomEvent<{ item: { id: string } }>).detail.item.id));
    const tree = $(el, '[role="tree"]');
    items(el)[0].focus();
    key(tree, 'ArrowRight');
    await settle();
    expect(items(el)[0].getAttribute('aria-expanded')).toBe('true');
    key(tree, 'ArrowRight');
    await settle();
    expect(el.focusId).toBe('app');
    expect(byLabel(el, 'App.razor').getAttribute('aria-level')).toBe('2');
    key(tree, 'ArrowLeft');
    await settle();
    expect(el.focusId).toBe('src');
    key(tree, 'Enter');
    expect(activated).toEqual(['src']);
  });

  it('multi selection extends with Shift and toggles with Ctrl', async () => {
    const el = await make('selection-mode="multi"');
    const rows = items(el);
    rows[0].querySelector<HTMLElement>('.sl-tree__row')!.click();
    rows[2].querySelector<HTMLElement>('.sl-tree__row')!.dispatchEvent(new MouseEvent('click', { bubbles: true, shiftKey: true }));
    await settle();
    expect(el.selectedItems.map((i) => (i as { id: string }).id)).toEqual(['src', 'docs', 'license']);
    items(el)[1].querySelector<HTMLElement>('.sl-tree__row')!.dispatchEvent(new MouseEvent('click', { bubbles: true, ctrlKey: true }));
    await settle();
    expect(el.selectedItems.map((i) => (i as { id: string }).id)).toEqual(['src', 'license']);
    expect($(el, '[role="tree"]').getAttribute('aria-multiselectable')).toBe('true');
  });

  it('checkbox mode propagates tri-state', async () => {
    const el = await make('selection-mode="checkbox"');
    el.expanded = ['src'];
    await settle();
    const app = byLabel(el, 'App.razor');
    app.querySelector<HTMLElement>('.sl-tree__row')!.click();
    await settle();
    const src = items(el)[0];
    expect(src.getAttribute('aria-checked')).toBe('mixed');
    expect(src.querySelector('[part="checkbox"]')?.getAttribute('data-state')).toBe('indeterminate');
    src.querySelector<HTMLElement>('.sl-tree__row')!.click();
    await settle();
    expect(items(el)[0].getAttribute('aria-checked')).toBe('true');
  });

  it('filter keeps ancestors, auto-expands them and highlights matches', async () => {
    const el = await make();
    el.filter = 'butt';
    await settle();
    const rows = items(el);
    expect(rows.map((r) => r.querySelector('.sl-tree__label')?.textContent)).toEqual(['src', 'Button.razor']);
    expect(rows[1].querySelector('mark')?.textContent).toBe('Butt');
  });

  it('lazy loads children when expanded', async () => {
    const el = (await fixture<SlTreeView>('<sl-tree-view></sl-tree-view>')) as SlTreeView;
    let calls = 0;
    el.hasChildren = (n) => (n as { lazy?: boolean }).lazy === true;
    el.loadChildren = async () => {
      calls++;
      return [{ id: 'child', label: 'Loaded child' }];
    };
    el.items = [{ id: 'root', label: 'Remote', lazy: true }];
    await settle();
    expect(items(el)[0].getAttribute('aria-expanded')).toBe('false');
    el.toggle('root');
    await settle();
    await new Promise((r) => setTimeout(r, 0));
    await settle();
    expect(calls).toBe(1);
    expect(items(el).map((r) => r.querySelector('.sl-tree__label')?.textContent)).toEqual(['Remote', 'Loaded child']);
  });
});

describe('sl-segmented', () => {
  it('is a radio group with roving focus', async () => {
    const el = await fixture<SlSegmented>(`<sl-segmented label="Range" items='["Day","Week","Month"]' value="Week"></sl-segmented>`);
    const radios = [...shadow(el).querySelectorAll<HTMLButtonElement>('[role="radio"]')];
    expect($(el, '[role="radiogroup"]').getAttribute('aria-label')).toBe('Range');
    expect(radios.map((r) => r.getAttribute('aria-checked'))).toEqual(['false', 'true', 'false']);
    expect(radios.map((r) => r.tabIndex)).toEqual([-1, 0, -1]);
    key(radios[1], 'ArrowRight');
    await settle();
    expect(el.value).toBe('Month');
    key(radios[2], 'ArrowRight');
    await settle();
    expect(el.value).toBe('Day');
  });
});

describe('sl-slider', () => {
  it('thumbs are ARIA sliders that step with the keyboard', async () => {
    const el = await fixture<SlSlider>('<sl-slider label="Volume" value="40" step="5" show-value></sl-slider>');
    const thumb = $(el, '[role="slider"]');
    expect(thumb.getAttribute('aria-valuenow')).toBe('40');
    expect(thumb.getAttribute('aria-valuemin')).toBe('0');
    expect(thumb.getAttribute('aria-valuemax')).toBe('100');
    key(thumb, 'ArrowRight');
    await settle();
    expect(el.value).toBe(45);
    key(thumb, 'PageUp');
    await settle();
    expect(el.value).toBe(55);
    key(thumb, 'End');
    await settle();
    expect(el.value).toBe(100);
    expect($(el, '.sl-slider__value').textContent).toBe('100');
  });

  it('range thumbs never cross', async () => {
    const el = await fixture<SlSlider>('<sl-slider value="20" range-end="30"></sl-slider>');
    const [low, high] = [...shadow(el).querySelectorAll<HTMLElement>('[role="slider"]')];
    expect(low.getAttribute('aria-label')).toBe('Minimum');
    for (let i = 0; i < 20; i++) key(low, 'ArrowRight');
    await settle();
    expect(el.value).toBeLessThanOrEqual(el.rangeEnd!);
    expect(high.getAttribute('aria-valuemin')).toBe(String(el.value));
  });

  it('renders ticks per step when there are few', async () => {
    const el = await fixture<SlSlider>('<sl-slider min="0" max="10" step="2" ticks></sl-slider>');
    expect(shadow(el).querySelectorAll('.sl-slider__tick')).toHaveLength(6);
  });
});

describe('display widgets', () => {
  it('avatar shows initials, a deterministic tone and a status', async () => {
    const el = await fixture<SlAvatar>('<sl-avatar name="Aaron Griffin" status="online"></sl-avatar>');
    const base = $(el, '[part="base"]');
    expect(base.textContent?.trim()).toBe('AG');
    expect(base.className).toMatch(/sl-tone-(accent|info|success|warning|danger)/);
    expect(base.getAttribute('aria-label')).toBe('Aaron Griffin, online');
    expect(shadow(el).querySelector('.sl-avatar__status--online')).not.toBeNull();
    const again = await fixture<SlAvatar>('<sl-avatar name="Aaron Griffin"></sl-avatar>');
    expect($(again, '[part="base"]').className.match(/sl-tone-\w+/)?.[0]).toBe(base.className.match(/sl-tone-\w+/)?.[0]);
  });

  it('breadcrumbs mark the current page and collapse the middle', async () => {
    const items = JSON.stringify([
      { label: 'Workspace', href: '#w' },
      { label: 'slate', href: '#s' },
      { label: 'src', href: '#src' },
      { label: 'Components', href: '#c' },
      { label: 'Button.razor' },
    ]);
    const el = await fixture<SlBreadcrumbs>(`<sl-breadcrumbs max-items="3" items='${items}'></sl-breadcrumbs>`);
    expect($(el, 'nav').getAttribute('aria-label')).toBe('Breadcrumb');
    expect($(el, '[aria-current="page"]').textContent).toBe('Button.razor');
    const more = shadow(el).querySelector('sl-menu');
    expect(more?.querySelectorAll('sl-menu-item')).toHaveLength(2);
    const clicks: number[] = [];
    el.addEventListener('sl-item-click', (e) => {
      clicks.push((e as CustomEvent<{ index: number }>).detail.index);
      e.preventDefault();
    });
    (shadow(el).querySelector('a') as HTMLAnchorElement).click();
    expect(clicks).toEqual([0]);
  });

  it('pagination shows ellipses, moves pages and keeps the first item on size change', async () => {
    const el = await fixture<SlPagination>('<sl-pagination page="6" total-count="300" page-size="20" page-sizes="[20,50]"></sl-pagination>');
    const labels = () => [...shadow(el).querySelectorAll('.sl-pagination__pages li')].map((li) => li.textContent?.trim() || 'btn');
    expect(labels()).toEqual(['btn', '1', '…', '5', '6', '7', '…', '15', 'btn']);
    expect($(el, '[aria-current="page"]').textContent?.trim()).toBe('6');
    expect($(el, '.sl-pagination__summary').textContent).toBe('101–120 of 300');

    const pages: number[] = [];
    el.addEventListener('sl-page-changed', (e) => pages.push((e as CustomEvent<{ page: number }>).detail.page));
    ($(el, '[aria-label="Next page"]') as HTMLButtonElement).click();
    await settle();
    expect(el.page).toBe(7);

    const select = $(el, 'select') as HTMLSelectElement;
    select.value = '50';
    select.dispatchEvent(new Event('change'));
    await settle();
    expect(el.pageSize).toBe(50);
    expect(el.page).toBe(3); // item 121 is on page 3 of 50
    expect(pages).toEqual([7, 3]);
  });

  it('skeleton renders lines with a short last line and can be static', async () => {
    const el = await fixture<SlSkeleton>('<sl-skeleton lines="3" animated="false"></sl-skeleton>');
    const lines = [...shadow(el).querySelectorAll<HTMLElement>('.sl-skeleton')];
    expect(lines).toHaveLength(3);
    expect(lines[2].style.width).toBe('60%');
    expect(lines[0].classList.contains('is-animated')).toBe(false);
    expect(el.getAttribute('aria-hidden')).toBe('true');
  });
});

// ---- form association (happy-dom has no ElementInternals: a recording fake stands in) ----

class RecordingInternals {
  value: unknown = null;
  flags: ValidityStateFlags = {};
  constructor(private readonly host: HTMLElement) {}
  get form(): HTMLFormElement | null {
    return this.host.closest('form');
  }
  setFormValue(value: unknown): void {
    this.value = value;
  }
  setValidity(flags: ValidityStateFlags = {}): void {
    this.flags = { ...flags };
  }
  checkValidity(): boolean {
    return !Object.values(this.flags).some(Boolean);
  }
}

describe('form association', () => {
  const original = Object.getOwnPropertyDescriptor(HTMLElement.prototype, 'attachInternals');
  beforeAll(() => {
    Object.defineProperty(HTMLElement.prototype, 'attachInternals', {
      configurable: true,
      value(this: HTMLElement) {
        return new RecordingInternals(this);
      },
    });
  });
  afterAll(() => {
    if (original) Object.defineProperty(HTMLElement.prototype, 'attachInternals', original);
    else delete (HTMLElement.prototype as { attachInternals?: unknown }).attachInternals;
  });

  const internals = (el: Element) => (el as unknown as { internals: RecordingInternals }).internals;

  it('select submits its value and multiple submits one entry per value', async () => {
    const single = await fixture<SlSelect>(`<sl-select name="region" value="eu" items='["eu","us"]' required></sl-select>`);
    await settle();
    expect(internals(single).value).toBe('eu');
    expect(internals(single).checkValidity()).toBe(true);
    single.value = undefined;
    await settle();
    expect(internals(single).flags.valueMissing).toBe(true);

    const multi = await fixture<SlSelect>(`<sl-select name="tags" multiple values='["a","b"]' items='["a","b","c"]'></sl-select>`);
    await settle();
    expect((internals(multi).value as FormData).getAll('tags')).toEqual(['a', 'b']);
  });

  it('date picker submits ISO dates (ranges as start/end) and resets', async () => {
    const form = await fixture<HTMLFormElement>('<form><sl-date-picker name="due" value="2026-10-06"></sl-date-picker><sl-date-picker name="span" selection="range"></sl-date-picker></form>');
    const [due, span] = [...form.querySelectorAll<SlDatePicker>('sl-date-picker')];
    expect(internals(due).value).toBe('2026-10-06');
    span.range = { start: '2026-10-01', end: '2026-10-07' };
    due.value = '2026-12-25';
    await settle(form);
    expect(internals(span).value).toBe('2026-10-01/2026-10-07');
    due.formResetCallback();
    await settle(form);
    expect(due.value).toBe('2026-10-06');
  });

  it('slider submits its value (two entries for a range)', async () => {
    const one = await fixture<SlSlider>('<sl-slider name="v" value="30"></sl-slider>');
    expect(internals(one).value).toBe('30');
    const range = await fixture<SlSlider>('<sl-slider name="r" value="10" range-end="90"></sl-slider>');
    expect((internals(range).value as FormData).getAll('r')).toEqual(['10', '90']);
  });

  it('select form reset restores the initial value', async () => {
    await frame();
    const form = await fixture<HTMLFormElement>(`<form><sl-select name="s" value="a" items='["a","b"]'></sl-select></form>`);
    const select = form.querySelector<SlSelect>('sl-select')!;
    select.value = 'b';
    select.formResetCallback();
    expect(select.value).toBe('a');
  });
});
