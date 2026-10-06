import { afterEach, describe, expect, it, vi } from 'vitest';
import { configureDefaults, getDefaults, resetDefaults, snackbar } from '../src/index';
import type { SlButton } from '../src/components/button';
import type { SlTextField } from '../src/components/text-field';
import type { SlCheckbox, SlRadioGroup } from '../src/components/selection';
import type { SlAppBar, SlAppShell, SlCard, SlDrawer, SlNavItem } from '../src/components/layout';
import type { SlDialog } from '../src/components/dialog';
import type { SlProvider } from '../src/components/provider';
import { $, cleanup, fixture, settle, shadow } from './helpers';

afterEach(() => {
  cleanup();
  resetDefaults();
});

describe('app-wide defaults', () => {
  it('unset button options follow the defaults and re-render when they change; explicit values win', async () => {
    const wrapper = await fixture<HTMLDivElement>('<div><sl-button>A</sl-button><sl-button variant="ghost">B</sl-button></div>');
    const [a, b] = [...wrapper.querySelectorAll('sl-button')] as SlButton[];
    configureDefaults({ button: { variant: 'solid', tone: 'accent', size: 'small' } });
    await settle(wrapper);
    expect([...$(a, 'button').classList]).toEqual(expect.arrayContaining(['sl-button--solid', 'sl-tone-accent', 'sl-button--small']));
    expect($(b, 'button').classList.contains('sl-button--ghost')).toBe(true);
    expect(b.resolved.tone).toBe('accent');

    resetDefaults();
    await settle(wrapper);
    expect($(a, 'button').classList.contains('sl-button--outlined')).toBe(true);
  });

  it('merges per group', () => {
    configureDefaults({ field: { variant: 'filled' } });
    configureDefaults({ field: { size: 'large' } });
    expect(getDefaults().field).toEqual({ variant: 'filled', size: 'large', radius: 'default' });
  });

  it('field, card, selection and dialog defaults apply', async () => {
    configureDefaults({ field: { variant: 'underlined' }, card: { variant: 'flat' }, selection: { labelPlacement: 'start' }, dialog: { maxWidth: 'lg' } });
    const field = await fixture<SlTextField>('<sl-text-field label="x"></sl-text-field>');
    expect($(field, '.sl-field').classList.contains('sl-field--underlined')).toBe(true);
    const card = await fixture<SlCard>('<sl-card>b</sl-card>');
    expect($(card, '.sl-card').classList.contains('sl-card--flat')).toBe(true);
    const box = await fixture<SlCheckbox>('<sl-checkbox label="x"></sl-checkbox>');
    expect($(box, 'label').classList.contains('sl-checkbox--label-start')).toBe(true);
    const dialog = await fixture<SlDialog>('<sl-dialog title="x"></sl-dialog>');
    expect(dialog.resolved.maxWidth).toBe('lg');
    expect($(dialog, 'dialog').classList.contains('sl-dialog--lg')).toBe(true);
  });

  it('snackbar defaults reconfigure the snackbar service', () => {
    configureDefaults({ snackbar: { position: 'top-center', maxVisible: 2 } });
    expect(snackbar.configuration.position).toBe('top-center');
    expect(snackbar.configuration.maxVisible).toBe(2);
    snackbar.configure({ position: 'bottom-right', maxVisible: 3 });
  });
});

describe('text field options', () => {
  it('counter shows length and flags over-limit values', async () => {
    const el = await fixture<SlTextField>('<sl-text-field label="Name" counter max-length="3" value="ab"></sl-text-field>');
    expect($(el, '[part="counter"]').textContent).toBe('2 / 3');
    el.value = 'abcd';
    await el.updateComplete;
    expect($(el, '[part="counter"]').classList.contains('is-over')).toBe(true);
  });

  it('clearable clears, focuses and notifies', async () => {
    const el = await fixture<SlTextField>('<sl-text-field label="Q" clearable value="hello"></sl-text-field>');
    const changed = vi.fn();
    el.addEventListener('sl-value-changed', changed);
    $(el, '.sl-field__clear').click();
    await el.updateComplete;
    expect(el.value).toBe('');
    expect(changed).toHaveBeenCalledWith(expect.objectContaining({ detail: { value: '' } }));
    expect(shadow(el).querySelector('.sl-field__clear')).toBeNull(); // hidden when empty
  });

  it('typing fires sl-value-changed', async () => {
    const el = await fixture<SlTextField>('<sl-text-field label="Q"></sl-text-field>');
    const changed = vi.fn();
    el.addEventListener('sl-value-changed', changed);
    const input = $(el, 'input') as HTMLInputElement;
    input.value = 'x';
    input.dispatchEvent(new Event('input'));
    expect(changed).toHaveBeenCalledTimes(1);
  });

  it('start/end content slots replace the icons', async () => {
    const el = await fixture<SlTextField>(`<sl-text-field label="Amount" start-icon="search" end-icon="x">
      <span slot="start-content">£</span><span slot="end-content">GBP</span></sl-text-field>`);
    await settle(el.parentElement!);
    expect(shadow(el).querySelector('.sl-field__icon')).toBeNull();
    expect($(el, '.sl-field__adornment--start').hasAttribute('hidden')).toBe(false);
    expect($(el, '.sl-field__adornment--end').hasAttribute('hidden')).toBe(false);
  });

  it('input-type, read-only and radius map to the input and classes', async () => {
    const el = await fixture<SlTextField>('<sl-text-field label="P" input-type="password" read-only radius="none"></sl-text-field>');
    const input = $(el, 'input') as HTMLInputElement;
    expect(input.type).toBe('password');
    expect(input.readOnly).toBe(true);
    expect($(el, '.sl-field').classList.contains('sl-radius-none')).toBe(true);
  });
});

describe('selection options', () => {
  it('checkbox tone, size, placement and sl-checked-changed', async () => {
    const el = await fixture<SlCheckbox>('<sl-checkbox label="x" tone="success" size="large" label-placement="start"></sl-checkbox>');
    expect([...$(el, 'label').classList]).toEqual(expect.arrayContaining(['sl-tone-success', 'sl-checkbox--large', 'sl-checkbox--label-start']));
    const changed = vi.fn();
    el.addEventListener('sl-checked-changed', changed);
    ($(el, 'input') as HTMLInputElement).click();
    expect(changed).toHaveBeenCalledWith(expect.objectContaining({ detail: { checked: true } }));
  });

  it('radio group direction and tone propagate; selection fires sl-value-changed', async () => {
    const group = await fixture<SlRadioGroup>(`<sl-radio-group direction="row" tone="danger" size="small">
      <sl-radio value="a" label="A"></sl-radio><sl-radio value="b" label="B"></sl-radio></sl-radio-group>`);
    await settle(group);
    expect($(group, '.sl-radio-group').classList.contains('sl-radio-group--row')).toBe(true);
    expect(group.getAttribute('aria-orientation')).toBe('horizontal');
    const radio = group.querySelector('sl-radio')!;
    expect([...$(radio, '.sl-radio').classList]).toEqual(expect.arrayContaining(['sl-tone-danger', 'sl-radio--small']));
    const changed = vi.fn();
    group.addEventListener('sl-value-changed', changed);
    (radio as HTMLElement).click();
    expect(changed).toHaveBeenCalledWith(expect.objectContaining({ detail: { value: 'a' } }));
  });
});

describe('display options', () => {
  it('badge variants, sizes and icons', async () => {
    const el = await fixture('<sl-badge variant="solid" tone="accent" size="small" icon="check">Done</sl-badge>');
    expect([...$(el, '.sl-badge').classList]).toEqual(expect.arrayContaining(['sl-badge--solid', 'sl-tone-accent', 'sl-badge--small']));
    expect($(el, '.sl-badge__icon')).toBeTruthy();
  });

  it('progress show-value, tone and explicit indeterminate', async () => {
    const el = await fixture('<sl-progress value="42" show-value tone="success" size="large"></sl-progress>');
    expect($(el, '[part="value"]').textContent).toBe('42%');
    expect([...$(el, '[role="progressbar"]').classList]).toEqual(expect.arrayContaining(['sl-tone-success', 'sl-progress--large']));
    const ind = await fixture('<sl-progress value="42" indeterminate></sl-progress>');
    expect($(ind, '[role="progressbar"]').classList.contains('sl-progress--indeterminate')).toBe(true);
  });

  it('alert variants, dense, custom and hidden icons', async () => {
    const el = await fixture('<sl-alert severity="success" variant="solid" dense icon="layers">x</sl-alert>');
    expect([...$(el, '.sl-alert').classList]).toEqual(expect.arrayContaining(['sl-alert--solid', 'sl-alert--dense', 'sl-tone-success']));
    expect($(el, '[part="icon"] path').getAttribute('d')).toContain('M3 8.5');
    const none = await fixture('<sl-alert icon="none">x</sl-alert>');
    expect(shadow(none).querySelector('[part="icon"]')).toBeNull();
  });

  it('spinner size and tone', async () => {
    const el = await fixture('<sl-spinner size="large" tone="accent"></sl-spinner>');
    expect([...$(el, '.sl-spinner').classList]).toEqual(expect.arrayContaining(['sl-spinner--large', 'sl-tone-accent']));
  });
});

describe('layout options', () => {
  it('the app shell drives its drawer and reports open changes', async () => {
    const shell = await fixture<SlAppShell>(`<sl-app-shell drawer-variant="persistent" responsive-breakpoint="lg">
      <sl-app-bar slot="app-bar" title="Slate"></sl-app-bar>
      <sl-drawer slot="drawer"><sl-nav-item label="Home" icon="home"></sl-nav-item></sl-drawer>
    </sl-app-shell>`);
    await settle(shell);
    const drawer = shell.querySelector('sl-drawer') as SlDrawer;
    expect(drawer.variant).toBe('persistent');
    expect(drawer.breakpoint).toBe('lg');

    const changes = vi.fn();
    shell.addEventListener('sl-drawer-open-changed', changes);
    const bar = shell.querySelector('sl-app-bar') as SlAppBar;
    $(bar, '[part="menu-button"]').click();
    await settle(shell);
    expect(drawer.open).toBe(false);
    expect(shell.drawerOpen).toBe(false);
    expect(changes).toHaveBeenCalledWith(expect.objectContaining({ detail: { open: false } }));

    shell.drawerOpen = true;
    await settle(shell);
    expect(drawer.open).toBe(true);
  });

  it('mini drawers collapse nav items to icons', async () => {
    const drawer = await fixture<SlDrawer>(`<sl-drawer variant="mini"><sl-nav-item label="Home" icon="home" trailing="3"></sl-nav-item></sl-drawer>`);
    await settle(drawer.parentElement!);
    const item = drawer.querySelector('sl-nav-item') as SlNavItem;
    expect(item.mini).toBe(true);
    expect($(item, 'button').getAttribute('aria-label')).toBe('Home');
  });

  it('nav items are links with aria-current when active', async () => {
    const item = await fixture<SlNavItem>('<sl-nav-item label="Docs" href="/docs" active trailing="12"></sl-nav-item>');
    const a = $(item, 'a');
    expect(a.getAttribute('aria-current')).toBe('page');
    expect($(item, '[part="trailing"]').textContent).toBe('12');
  });

  it('app bar title and menu button', async () => {
    const bar = await fixture<SlAppBar>('<sl-app-bar title="Slate" show-menu-button="false"></sl-app-bar>');
    expect($(bar, '[part="title"]').textContent).toBe('Slate');
    expect(shadow(bar).querySelector('[part="menu-button"]')).toBeNull();
    expect(bar.hasAttribute('title')).toBe(false);
  });

  it('card header slot replaces the title block; radius maps to a class', async () => {
    const card = await fixture<SlCard>('<sl-card radius="large" title="Ignored"><div slot="header">Custom</div>b</sl-card>');
    await settle(card.parentElement!);
    expect($(card, '.sl-card').classList.contains('sl-radius-large')).toBe(true);
    const slot = $(card, 'slot[name="header"]') as HTMLSlotElement;
    expect(slot.assignedElements()[0].textContent).toBe('Custom');
  });
});

describe('dialog options', () => {
  it('icon tile follows the tone (neutral by default)', async () => {
    const el = await fixture<SlDialog>('<sl-dialog title="x" icon="info"></sl-dialog>');
    expect($(el, '.sl-dialog__icon').classList.contains('sl-tone-neutral')).toBe(true);
    el.tone = 'danger';
    await el.updateComplete;
    expect($(el, '.sl-dialog__icon').classList.contains('sl-tone-danger')).toBe(true);
    expect(el.hasAttribute('title')).toBe(false);
  });
});

describe('provider themes', () => {
  it('themeOptions apply a custom theme and follow the base mode', async () => {
    const provider = await fixture<SlProvider>('<sl-provider theme="dark"><p>x</p></sl-provider>');
    provider.themeOptions = { accent: '#FF5A1F', radiusScale: 0 };
    await provider.updateComplete;
    expect(provider.getAttribute('data-sl-theme')).toBe('dark');
    expect(provider.style.getPropertyValue('--sl-radius-md')).toBe('0px');
    expect(provider.style.getPropertyValue('--sl-color-accent-default')).not.toBe('');

    provider.themeOptions = null;
    await provider.updateComplete;
    expect(provider.style.getPropertyValue('--sl-radius-md')).toBe('');
    expect(provider.getAttribute('data-sl-theme')).toBe('dark');
  });
});
