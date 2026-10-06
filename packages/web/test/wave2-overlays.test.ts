import { afterEach, describe, expect, it, vi } from 'vitest';
import type { SlPopover, SlTooltip } from '../src/components/popover';
import type { SlMenu, SlMenuItem } from '../src/components/menu';
import type { SlSelect } from '../src/components/select';
import { Overlay, openOverlayCount } from '../src/internal/overlay';
import { $, cleanup, fixture, key, settle, shadow } from './helpers';

const frame = () => new Promise<void>((r) => requestAnimationFrame(() => r()));

function pointerDown(target: EventTarget): void {
  target.dispatchEvent(new Event('pointerdown', { bubbles: true, composed: true }));
}

function rect(x: number, y: number, width: number, height: number): DOMRect {
  return { x, y, left: x, top: y, width, height, right: x + width, bottom: y + height, toJSON: () => ({}) } as DOMRect;
}

afterEach(() => {
  cleanup();
  vi.useRealTimers();
});

describe('overlay positioning', () => {
  it('places the panel with Slate.Core positioning and flips when there is no room below', () => {
    document.documentElement.style.width = '800px';
    const anchor = document.createElement('button');
    const panel = document.createElement('div');
    panel.hidden = true;
    document.body.append(anchor, panel);
    anchor.getBoundingClientRect = () => rect(100, 0, 80, 30);
    Object.defineProperty(panel, 'offsetWidth', { value: 200 });
    Object.defineProperty(panel, 'offsetHeight', { value: 120 });
    Object.defineProperty(document.documentElement, 'clientWidth', { configurable: true, value: 800 });
    Object.defineProperty(document.documentElement, 'clientHeight', { configurable: true, value: 600 });

    const overlay = new Overlay({ anchor: () => anchor, panel: () => panel, placement: () => 'bottom-start', onDismiss: () => {} });
    overlay.open();
    expect(panel.hidden).toBe(false);
    expect(panel.style.left).toBe('100px');
    expect(panel.style.top).toBe('36px'); // 30 + 6 offset
    expect(panel.dataset.actualPlacement).toBe('bottom-start');
    overlay.close();

    // Anchor near the bottom: flips to the top.
    anchor.getBoundingClientRect = () => rect(100, 560, 80, 30);
    overlay.open();
    expect(panel.dataset.actualPlacement).toBe('top-start');
    expect(panel.style.top).toBe('434px'); // 560 - 6 - 120
    overlay.close();
    expect(panel.hidden).toBe(true);
    expect(openOverlayCount()).toBe(0);
  });

  it('Escape dismisses only the topmost overlay', () => {
    const a = document.createElement('div');
    const b = document.createElement('div');
    document.body.append(a, b);
    const dismissed: string[] = [];
    const first = new Overlay({ anchor: () => null, panel: () => a, placement: () => 'bottom', onDismiss: () => dismissed.push('first') });
    const second = new Overlay({ anchor: () => null, panel: () => b, placement: () => 'bottom', onDismiss: () => dismissed.push('second') });
    first.open();
    second.open();
    key(document.body, 'Escape');
    expect(dismissed).toEqual(['second']);
    second.close();
    first.close();
  });
});

describe('sl-popover', () => {
  it('toggles from its anchor, reports open state and closes on Escape and outside clicks', async () => {
    const el = await fixture<SlPopover>('<sl-popover><button slot="anchor">Filters</button><p>Body</p></sl-popover>');
    const anchor = el.querySelector('button')!;
    const changes: boolean[] = [];
    el.addEventListener('sl-open-changed', (e) => changes.push((e as CustomEvent<{ open: boolean }>).detail.open));

    anchor.click();
    await settle();
    expect(el.open).toBe(true);
    expect($(el, '.sl-popover__panel').hidden).toBe(false);
    expect(anchor.getAttribute('aria-expanded')).toBe('true');
    expect(anchor.getAttribute('aria-haspopup')).toBe('dialog');

    key(document.body, 'Escape');
    await settle();
    expect(el.open).toBe(false);

    anchor.click();
    await settle();
    pointerDown(document.body);
    await settle();
    expect(el.open).toBe(false);
    expect(changes).toEqual([true, false, true, false]);
  });

  it('stays open on outside clicks when close-on-outside-click="false"', async () => {
    const el = await fixture<SlPopover>('<sl-popover close-on-outside-click="false" open><button slot="anchor">A</button>x</sl-popover>');
    pointerDown(document.body);
    await settle();
    expect(el.open).toBe(true);
    el.open = false;
  });

  it('modal popovers move focus inside and trap Tab', async () => {
    const el = await fixture<SlPopover>('<sl-popover modal><button slot="anchor">A</button><input id="one" /><button id="two">Two</button></sl-popover>');
    el.querySelector<HTMLButtonElement>('[slot="anchor"]')!.click();
    await settle();
    await frame();
    expect(document.activeElement?.id).toBe('one');
    expect($(el, '.sl-popover__panel').getAttribute('aria-modal')).toBe('true');

    el.querySelector<HTMLButtonElement>('#two')!.focus();
    const e = key(document.activeElement!, 'Tab');
    expect(e.defaultPrevented).toBe(true);
    expect(document.activeElement?.id).toBe('one');
    el.open = false;
  });

  it('renders placement for styling and the arrow part', async () => {
    const el = await fixture<SlPopover>('<sl-popover placement="right-end"><button slot="anchor">A</button></sl-popover>');
    expect($(el, '[part="panel"]').dataset.placement).toBe('right-end');
    expect(shadow(el).querySelector('[part="arrow"]')).not.toBeNull();
  });
});

describe('sl-tooltip', () => {
  it('shows after the delay on hover, hides on leave and describes the anchor', async () => {
    vi.useFakeTimers();
    const el = await fixture<SlTooltip>('<sl-tooltip text="Copy link" shortcut="⌘C" delay="300"><button>Copy</button></sl-tooltip>');
    const button = el.querySelector('button')!;
    expect(button.getAttribute('aria-description')).toBe('Copy link (⌘C)');

    el.dispatchEvent(new Event('pointerenter'));
    vi.advanceTimersByTime(299);
    await settle();
    expect(el.visible).toBe(false);
    vi.advanceTimersByTime(1);
    await settle();
    expect(el.visible).toBe(true);
    const bubble = $(el, '[role="tooltip"]');
    expect(bubble.hidden).toBe(false);
    expect(bubble.textContent).toContain('Copy link');
    expect(bubble.querySelector('.sl-kbd')?.textContent).toBe('⌘C');

    el.dispatchEvent(new Event('pointerleave'));
    await settle();
    expect(el.visible).toBe(false);
  });

  it('shows immediately when moving from another tooltip and hides on Escape', async () => {
    const el = await fixture<SlTooltip>('<div><sl-tooltip text="A"><button>A</button></sl-tooltip><sl-tooltip text="B"><button>B</button></sl-tooltip></div>');
    const [a, b] = [...el.querySelectorAll<SlTooltip>('sl-tooltip')];
    a.show();
    await settle();
    a.hide();
    b.dispatchEvent(new Event('pointerenter'));
    await settle(el);
    expect(b.visible).toBe(true);
    key(document.body, 'Escape');
    await settle(el);
    expect(b.visible).toBe(false);
  });

  it('does nothing when disabled', async () => {
    const el = await fixture<SlTooltip>('<sl-tooltip text="A" disabled delay="0"><button>A</button></sl-tooltip>');
    el.dispatchEvent(new Event('focusin'));
    await settle();
    expect(el.visible).toBe(false);
  });
});

describe('sl-menu', () => {
  const markup = `<sl-menu>
      <button slot="trigger">Actions</button>
      <sl-menu-item label="Rename" shortcut="F2"></sl-menu-item>
      <sl-menu-item label="Duplicate" disabled></sl-menu-item>
      <sl-menu-item label="Download"></sl-menu-item>
      <sl-menu-item separator></sl-menu-item>
      <sl-menu-item label="Delete" tone="danger" value="del"></sl-menu-item>
    </sl-menu>`;

  async function openMenu(): Promise<{ menu: SlMenu; items: SlMenuItem[]; trigger: HTMLButtonElement }> {
    const menu = await fixture<SlMenu>(markup);
    const trigger = menu.querySelector('button')!;
    trigger.focus();
    key(trigger, 'ArrowDown');
    await settle();
    await frame();
    return { menu, items: [...menu.querySelectorAll<SlMenuItem>('sl-menu-item')], trigger };
  }

  it('opens from the keyboard on the first item with menu semantics', async () => {
    const { menu, items, trigger } = await openMenu();
    expect(menu.open).toBe(true);
    expect(trigger.getAttribute('aria-haspopup')).toBe('menu');
    expect(trigger.getAttribute('aria-expanded')).toBe('true');
    expect($(menu, '[part="panel"]').getAttribute('role')).toBe('menu');
    expect(items[0].getAttribute('role')).toBe('menuitem');
    expect(items[3].getAttribute('role')).toBe('separator');
    expect(document.activeElement).toBe(items[0]);
    menu.close(false);
  });

  it('moves with arrows (skipping disabled and separators, wrapping) and typeahead', async () => {
    const { menu, items } = await openMenu();
    key(items[0], 'ArrowDown');
    expect(document.activeElement).toBe(items[2]);
    key(items[2], 'ArrowDown');
    expect(document.activeElement).toBe(items[4]);
    key(items[4], 'ArrowDown');
    expect(document.activeElement).toBe(items[0]);
    key(items[0], 'End');
    expect(document.activeElement).toBe(items[4]);
    key(items[4], 'd');
    expect(document.activeElement).toBe(items[2]); // "Download" (Duplicate is disabled)
    menu.close(false);
  });

  it('activating an item fires sl-select, closes and returns focus to the trigger', async () => {
    const { menu, items, trigger } = await openMenu();
    const selected: string[] = [];
    menu.addEventListener('sl-select', (e) => selected.push((e as CustomEvent<{ value: string }>).detail.value));
    items[4].focus();
    key(items[4], 'Enter');
    await settle();
    expect(selected).toEqual(['del']);
    expect(menu.open).toBe(false);
    expect(document.activeElement).toBe(trigger);
  });

  it('checkable items toggle and expose menuitemcheckbox', async () => {
    const menu = await fixture<SlMenu>('<sl-menu open><button slot="trigger">V</button><sl-menu-item label="Hidden files" checked="false"></sl-menu-item></sl-menu>');
    const item = menu.querySelector<SlMenuItem>('sl-menu-item')!;
    await settle();
    expect(item.getAttribute('role')).toBe('menuitemcheckbox');
    expect(item.getAttribute('aria-checked')).toBe('false');
    item.click();
    await settle();
    expect(item.checked).toBe(true);
    expect(item.getAttribute('aria-checked')).toBe('true');
  });

  it('submenus open with ArrowRight and close with ArrowLeft', async () => {
    const menu = await fixture<SlMenu>(`<sl-menu><button slot="trigger">M</button>
      <sl-menu-item label="Export"><sl-menu-item slot="submenu" label="CSV"></sl-menu-item><sl-menu-item slot="submenu" label="JSON"></sl-menu-item></sl-menu-item>
    </sl-menu>`);
    menu.show();
    await settle();
    await frame();
    const parent = menu.querySelector<SlMenuItem>(':scope > sl-menu-item')!;
    expect(parent.getAttribute('aria-haspopup')).toBe('menu');
    parent.focus();
    key(parent, 'ArrowRight');
    await settle();
    expect(parent.submenuOpen).toBe(true);
    const csv = parent.querySelector<SlMenuItem>('sl-menu-item')!;
    expect(document.activeElement).toBe(csv);
    key(csv, 'ArrowLeft');
    await settle();
    expect(parent.submenuOpen).toBe(false);
    expect(document.activeElement).toBe(parent);
    menu.close(false);
  });

  it('context menus open at the pointer', async () => {
    const menu = await fixture<SlMenu>('<sl-menu context-menu><div slot="trigger" style="height:100px">Area</div><sl-menu-item label="Copy"></sl-menu-item></sl-menu>');
    menu.querySelector('div')!.dispatchEvent(new MouseEvent('contextmenu', { bubbles: true, composed: true, cancelable: true, clientX: 40, clientY: 50 }));
    await settle();
    expect(menu.open).toBe(true);
    const panel = $(menu, '[part="panel"]');
    expect(panel.style.left).toBe('40px');
    expect(panel.style.top).toBe('50px');
    key(document.body, 'Escape');
    await settle();
    expect(menu.open).toBe(false);
  });
});

describe('sl-select', () => {
  const items = JSON.stringify([
    { value: 'eu-west', label: 'Europe West', group: 'Europe' },
    { value: 'eu-north', label: 'Europe North', group: 'Europe', disabled: true },
    { value: 'us-east', label: 'US East', group: 'Americas' },
    { value: 'sa-east', label: 'São Paulo', group: 'Americas' },
  ]);

  it('is an ARIA select-only combobox that opens with ArrowDown and selects with Enter', async () => {
    const el = await fixture<SlSelect>(`<sl-select label="Region" items='${items}'></sl-select>`);
    const trigger = $(el, '[role="combobox"]');
    const changes: unknown[] = [];
    el.addEventListener('sl-value-changed', (e) => changes.push((e as CustomEvent<{ value: unknown }>).detail.value));

    expect(trigger.getAttribute('aria-expanded')).toBe('false');
    expect(trigger.getAttribute('aria-haspopup')).toBe('listbox');
    key(trigger, 'ArrowDown');
    await settle();
    expect(el.open).toBe(true);
    expect(trigger.getAttribute('aria-expanded')).toBe('true');
    const active = trigger.getAttribute('aria-activedescendant')!;
    expect(shadow(el).getElementById(active)?.textContent).toContain('Europe West');

    key(trigger, 'ArrowDown'); // skips the disabled option
    await settle();
    expect(shadow(el).getElementById(trigger.getAttribute('aria-activedescendant')!)?.textContent).toContain('US East');
    key(trigger, 'Enter');
    await settle();
    expect(el.value).toBe('us-east');
    expect(el.open).toBe(false);
    expect(changes).toEqual(['us-east']);
    expect($(el, '[part="value"]').textContent).toContain('US East');
  });

  it('groups options and marks the selection', async () => {
    const el = await fixture<SlSelect>(`<sl-select value="sa-east" items='${items}'></sl-select>`);
    el.show();
    await settle();
    const groups = [...shadow(el).querySelectorAll('[role="group"]')];
    expect(groups.map((g) => g.querySelector('.sl-select__group-label')?.textContent)).toEqual(['Europe', 'Americas']);
    const selected = shadow(el).querySelector('[role="option"][aria-selected="true"]');
    expect(selected?.textContent).toContain('São Paulo');
    expect(shadow(el).querySelector('[aria-disabled="true"]')?.textContent).toContain('Europe North');
    el.hide();
  });

  it('typeahead jumps to matching options in select-only mode', async () => {
    const el = await fixture<SlSelect>(`<sl-select items='${items}'></sl-select>`);
    const trigger = $(el, '[role="combobox"]');
    key(trigger, 'u');
    await settle();
    expect(el.open).toBe(true);
    expect(shadow(el).getElementById(trigger.getAttribute('aria-activedescendant')!)?.textContent).toContain('US East');
    el.hide();
  });

  it('searchable filters case/diacritic-insensitively with highlights and announces search', async () => {
    const el = await fixture<SlSelect>(`<sl-select searchable items='${items}'></sl-select>`);
    const input = $(el, 'input') as HTMLInputElement;
    const searches: string[] = [];
    el.addEventListener('sl-search-changed', (e) => searches.push((e as CustomEvent<{ query: string }>).detail.query));
    expect(input.getAttribute('aria-autocomplete')).toBe('list');
    input.value = 'sao';
    input.dispatchEvent(new Event('input', { bubbles: true }));
    await settle();
    const options = [...shadow(el).querySelectorAll('[role="option"]')];
    expect(options.map((o) => o.textContent?.trim())).toEqual(['São Paulo']);
    expect(options[0].querySelector('mark')?.textContent).toBe('São');
    expect(searches).toEqual(['sao']);
    key(input, 'Enter');
    await settle();
    expect(el.value).toBe('sa-east');
  });

  it('creatable offers the typed text and empty results render the empty slot', async () => {
    const el = await fixture<SlSelect>(`<sl-select searchable creatable items='${items}'><span slot="empty-content">Nothing</span></sl-select>`);
    const input = $(el, 'input') as HTMLInputElement;
    input.value = 'ap-south';
    input.dispatchEvent(new Event('input', { bubbles: true }));
    await settle();
    const create = shadow(el).querySelector('.sl-select__option--create');
    expect(create?.textContent).toContain('Create “ap-south”');
    key(input, 'Enter');
    await settle();
    expect(el.value).toBe('ap-south');

    const plain = await fixture<SlSelect>(`<sl-select searchable items='${items}'><span slot="empty-content">Nothing</span></sl-select>`);
    const plainInput = $(plain, 'input') as HTMLInputElement;
    plainInput.value = 'zzz';
    plainInput.dispatchEvent(new Event('input', { bubbles: true }));
    await settle();
    expect(shadow(plain).querySelector('[part="empty"] slot[name="empty-content"]')).not.toBeNull();
  });

  it('multiple keeps the list open, renders removable chips and Backspace removes the last value', async () => {
    const el = await fixture<SlSelect>(`<sl-select multiple searchable max-visible-chips="1" items='${items}'></sl-select>`);
    const input = $(el, 'input') as HTMLInputElement;
    const changes: unknown[][] = [];
    el.addEventListener('sl-values-changed', (e) => changes.push([...(e as CustomEvent<{ values: unknown[] }>).detail.values]));
    key(input, 'ArrowDown');
    await settle();
    key(input, 'Enter');
    await settle();
    key(input, 'ArrowDown');
    await settle();
    key(input, 'Enter');
    await settle();
    expect(el.open).toBe(true);
    expect(el.values).toEqual(['eu-west', 'us-east']);
    expect($(el, '[role="listbox"]').getAttribute('aria-multiselectable')).toBe('true');
    const chips = [...shadow(el).querySelectorAll('[part="chip"]')].map((c) => c.textContent?.trim());
    expect(chips).toEqual([expect.stringContaining('Europe West'), '+1']);

    key(input, 'Backspace');
    await settle();
    expect(el.values).toEqual(['eu-west']);
    expect(changes.at(-1)).toEqual(['eu-west']);
  });

  it('clearable clears the value', async () => {
    const el = await fixture<SlSelect>(`<sl-select clearable value="us-east" items='${items}'></sl-select>`);
    ($(el, '.sl-field__clear') as HTMLButtonElement).click();
    await settle();
    expect(el.value).toBeUndefined();
  });

  it('item-template renders custom option content', async () => {
    const el = await fixture<SlSelect>(`<sl-select items='${items}'><template slot="item-template"><b class="custom" data-text></b></template></sl-select>`);
    el.show();
    await settle();
    expect(shadow(el).querySelector('.custom')?.textContent).toBe('Europe West');
    el.hide();
  });
});
