import { afterEach, describe, expect, it, vi } from 'vitest';
import type { SlAppShell, SlCard, SlDrawer, SlGridItem, SlStack } from '../src/components/layout';
import type { SlProvider } from '../src/components/provider';
import { $, cleanup, fixture, key, settle, shadow } from './helpers';

afterEach(cleanup);

describe('sl-provider', () => {
  it('maps theme and density to the token attributes', async () => {
    const el = await fixture<SlProvider>('<sl-provider theme="dark" density="comfortable"></sl-provider>');
    expect(el.getAttribute('data-sl-theme')).toBe('dark');
    expect(el.getAttribute('data-sl-density')).toBe('comfortable');
    el.theme = 'system';
    await el.updateComplete;
    expect(el.getAttribute('data-sl-theme')).toBe('auto');
  });

  it('defaults to light + compact', async () => {
    const el = await fixture<SlProvider>('<sl-provider></sl-provider>');
    expect(el.getAttribute('data-sl-theme')).toBe('light');
    expect(el.getAttribute('data-sl-density')).toBe('compact');
    expect(el.resolvedTheme).toBe('light');
  });

  it('only the outermost provider hosts snackbars', async () => {
    const outer = await fixture<SlProvider>('<sl-provider><sl-provider theme="dark" id="inner"></sl-provider></sl-provider>');
    await settle(outer);
    const inner = outer.querySelector('#inner')!;
    expect(shadow(outer).querySelector('sl-snackbar-host')).not.toBeNull();
    expect(shadow(inner).querySelector('sl-snackbar-host')).toBeNull();
  });
});

describe('sl-app-shell + drawer', () => {
  const shell = `<sl-app-shell>
      <sl-app-bar slot="app-bar" menu-button>Slate</sl-app-bar>
      <sl-drawer slot="drawer" variant="persistent"><a class="sl-nav__item" href="#">Home</a></sl-drawer>
      <p>content</p>
    </sl-app-shell>`;

  it('renders the shell grid with a main landmark', async () => {
    const el = await fixture<SlAppShell>(shell);
    expect($(el, '.sl-app-shell')).toBeTruthy();
    expect($(el, 'main.sl-main')).toBeTruthy();
  });

  it('menu button toggles the drawer and reflects aria-expanded', async () => {
    const el = await fixture<SlAppShell>(shell);
    await settle(el);
    const drawer = el.querySelector('sl-drawer') as SlDrawer;
    const bar = el.querySelector('sl-app-bar')!;
    expect(drawer.open).toBe(true); // persistent defaults open
    const changes: boolean[] = [];
    el.addEventListener('sl-drawer-change', (e) => changes.push((e as CustomEvent).detail.open));

    $(bar, 'button').click();
    await settle(el);
    expect(drawer.open).toBe(false);
    expect($(bar, 'button').getAttribute('aria-expanded')).toBe('false');
    expect($(drawer, 'nav').classList.contains('is-open')).toBe(false);

    $(bar, 'button').click();
    await settle(el);
    expect(drawer.open).toBe(true);
    expect(changes).toEqual([false, true]);
  });

  it('temporary drawers show a scrim, close on scrim click and Escape', async () => {
    const drawer = await fixture<SlDrawer>('<sl-drawer variant="temporary"><button>Item</button></sl-drawer>');
    expect(drawer.open).toBe(false);
    expect(drawer.overlay).toBe(true);
    drawer.show();
    await drawer.updateComplete;
    expect(shadow(drawer).querySelector('.sl-drawer-scrim')).not.toBeNull();

    $(drawer, '.sl-drawer-scrim').click();
    await drawer.updateComplete;
    expect(drawer.open).toBe(false);

    drawer.show();
    await drawer.updateComplete;
    key(document, 'Escape');
    await drawer.updateComplete;
    expect(drawer.open).toBe(false);
  });

  it('a closed overlay drawer is inert', async () => {
    const drawer = await fixture<SlDrawer>('<sl-drawer variant="temporary"></sl-drawer>');
    expect($(drawer, 'nav').hasAttribute('inert')).toBe(true);
  });

  it('Escape does not close a persistent drawer', async () => {
    const drawer = await fixture<SlDrawer>('<sl-drawer variant="persistent"></sl-drawer>');
    key(document, 'Escape');
    expect(drawer.open).toBe(true);
  });

  it('mini drawers start collapsed', async () => {
    const drawer = await fixture<SlDrawer>('<sl-drawer variant="mini"></sl-drawer>');
    expect(drawer.open).toBe(false);
    expect($(drawer, 'nav').className).toContain('sl-drawer--mini');
  });
});

describe('grid, stack and friends', () => {
  it('grid items set every breakpoint span explicitly', async () => {
    const grid = await fixture('<sl-grid spacing="6"><sl-grid-item xs="12" md="6"></sl-grid-item></sl-grid>');
    await settle(grid);
    const item = grid.querySelector('sl-grid-item') as SlGridItem;
    const spans = ['xs', 'sm', 'md', 'lg', 'xl'].map((bp) => item.style.getPropertyValue(`--_span-${bp}`));
    expect(spans).toEqual(['12', '12', '6', '6', '6']);
    expect($(grid, '.sl-grid').style.getPropertyValue('--_gap')).toBe('var(--sl-space-6)');
  });

  it('stack maps props to classes and gap', async () => {
    const el = await fixture<SlStack>('<sl-stack direction="row" spacing="2" align="center" justify="between" wrap></sl-stack>');
    const base = $(el, '.sl-stack');
    for (const c of ['sl-stack--row', 'sl-stack--wrap', 'sl-stack--align-center', 'sl-stack--justify-between']) {
      expect(base.classList.contains(c)).toBe(true);
    }
    expect(base.style.getPropertyValue('--_gap')).toBe('var(--sl-space-2)');
  });

  it('invalid spacing steps throw a helpful error', async () => {
    const el = await fixture<SlStack>('<sl-stack></sl-stack>');
    const errors = vi.spyOn(console, 'error').mockImplementation(() => {});
    el.spacing = '7';
    await expect(el.updateComplete).rejects.toThrow(/Unknown spacing step/);
    errors.mockRestore();
  });

  it('container sizes', async () => {
    const el = await fixture('<sl-container max-width="md" gutters="false"></sl-container>');
    expect($(el, '.sl-container').className).toContain('sl-container--md');
    expect($(el, '.sl-container').className).toContain('sl-container--no-gutters');
  });

  it('divider exposes separator semantics', async () => {
    const el = await fixture('<sl-divider vertical></sl-divider>');
    const d = $(el, '[role="separator"]');
    expect(d.getAttribute('aria-orientation')).toBe('vertical');
    expect(d.classList.contains('sl-divider--vertical')).toBe(true);
  });

  it('card shows header and footer only when used', async () => {
    const bare = await fixture<SlCard>('<sl-card>Body</sl-card>');
    expect($(bare, '.sl-card__header').hidden).toBe(true);
    expect($(bare, '.sl-card__footer').hidden).toBe(true);

    const full = await fixture<SlCard>(`<sl-card title="Deployments" subtitle="Last 7 days" variant="outlined" interactive>
        <button slot="header-actions">More</button>Body<div slot="footer">Footer</div></sl-card>`);
    expect($(full, '.sl-card__header').hidden).toBe(false);
    expect($(full, '.sl-card__title').textContent).toBe('Deployments');
    expect($(full, '.sl-card__footer').hidden).toBe(false);
    expect($(full, '.sl-card').getAttribute('tabindex')).toBe('0');
    expect($(full, '.sl-card').className).toContain('sl-card--outlined');
  });

  it('toolbar has the toolbar role and label', async () => {
    const el = await fixture('<sl-toolbar label="Formatting"></sl-toolbar>');
    const tb = $(el, '[role="toolbar"]');
    expect(tb.getAttribute('aria-label')).toBe('Formatting');
  });
});
