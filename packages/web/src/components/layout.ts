import { LitElement, css, html, nothing } from 'lit';
import { classMap } from 'lit/directives/class-map.js';
import { ifDefined } from 'lit/directives/if-defined.js';
import { styleMap } from 'lit/directives/style-map.js';
import { hostReset, styles } from '../internal/styles';
import { renderIcon } from '../internal/icon';
import { deepActiveElement, defaultTrue, firstFocusable } from '../internal/dom';
import { TitledElement } from '../internal/title';
import { DefaultsController, getDefaults, type CardVariant, type Radius } from '../core/defaults';
import { iconButtonClasses } from './button';
import { breakpointMinWidth, breakpoints, resolveSpan, spaceVar, type GridSpan } from '../core/layout';

export type ResponsiveBreakpoint = 'sm' | 'md' | 'lg' | 'xl';

/**
 * <sl-app-shell fill-viewport drawer-variant="responsive" responsive-breakpoint="md" drawer-open>
 *   <sl-app-bar slot="app-bar" title="Slate"></sl-app-bar>
 *   <sl-drawer slot="drawer"> <sl-nav-item …> </sl-drawer>
 *   main content…
 * </sl-app-shell>
 * The shell drives its drawer: `drawer-variant`, `responsive-breakpoint` and the two-way `drawer-open`
 * (fires `sl-drawer-open-changed` with { open }). The app bar's menu button toggles it.
 */
export class SlAppShell extends LitElement {
  static override properties = {
    drawerVariant: { attribute: 'drawer-variant', reflect: true },
    drawerOpen: { type: Boolean, attribute: 'drawer-open', reflect: true },
    responsiveBreakpoint: { attribute: 'responsive-breakpoint', reflect: true },
    fillViewport: { type: Boolean, attribute: 'fill-viewport', reflect: true },
    flush: { type: Boolean, reflect: true },
  };
  static override styles = [
    hostReset,
    styles.layout,
    css`
      :host { display: block; height: 100%; min-height: 0; }
      :host([fill-viewport]) { height: 100vh; height: 100dvh; }
    `,
  ];

  declare drawerVariant: DrawerVariant;
  /** Unset until the drawer resolves its initial state (open when docked, closed when overlaying). */
  declare drawerOpen: boolean | undefined;
  declare responsiveBreakpoint: ResponsiveBreakpoint;
  /** Fill the viewport (use when the shell is the page root). */
  declare fillViewport: boolean;
  /** Remove the main area's padding. */
  declare flush: boolean;

  constructor() {
    super();
    this.drawerVariant = 'responsive';
    this.responsiveBreakpoint = 'md';
    this.fillViewport = false;
    this.flush = false;
    this.addEventListener('sl-menu-toggle', () => this.drawer?.toggle());
    this.addEventListener('sl-drawer-change', (e) => {
      if (e.target !== this.drawer) return;
      const open = (e as CustomEvent<{ open: boolean }>).detail.open;
      const bar = this.querySelector<SlAppBar>(':scope > sl-app-bar');
      if (bar) bar.menuExpanded = open;
      if (this.drawerOpen !== open) {
        this.drawerOpen = open;
        this.dispatchEvent(new CustomEvent('sl-drawer-open-changed', { bubbles: true, composed: true, detail: { open } }));
      }
    });
  }

  get drawer(): SlDrawer | null {
    return this.querySelector<SlDrawer>(':scope > sl-drawer');
  }

  override willUpdate(): void {
    this.syncDrawer();
  }

  private syncDrawer = (): void => {
    const drawer = this.drawer;
    if (!drawer) return;
    drawer.variant = this.drawerVariant;
    drawer.breakpoint = this.responsiveBreakpoint;
    if (this.drawerOpen !== undefined && drawer.open !== this.drawerOpen) {
      if (this.drawerOpen) drawer.show();
      else drawer.hide();
    }
  };

  override render() {
    return html`<div class="sl-app-shell" part="base">
      <slot name="app-bar"></slot>
      <slot name="drawer" @slotchange=${this.syncDrawer}></slot>
      <main class=${classMap({ 'sl-main': true, 'sl-main--flush': this.flush })} part="main"><slot></slot></main>
    </div>`;
  }
}

/**
 * <sl-app-bar title="Slate">
 *   <img slot="leading"> … <sl-text-field slot="center"> … <sl-button slot="actions">
 * </sl-app-bar>
 * Regions: [menu button] [leading] [title] [center + default slot] [actions]. `show-menu-button="false"` hides the menu.
 */
export class SlAppBar extends TitledElement {
  static override properties = {
    title: {},
    showMenuButton: { attribute: 'show-menu-button', converter: defaultTrue },
    menuLabel: { attribute: 'menu-label' },
    menuExpanded: { type: Boolean, attribute: false },
  };
  static override styles = [hostReset, styles.base, styles.tone, styles.button, styles.typography, styles.layout, css`:host { display: contents; }`];

  declare title: string;
  declare showMenuButton: boolean;
  declare menuLabel: string;
  declare menuExpanded: boolean;

  constructor() {
    super();
    this.title = '';
    this.showMenuButton = true;
    this.menuLabel = 'Toggle navigation';
    this.menuExpanded = true;
  }

  override render() {
    return html`<header class="sl-app-bar" part="base">
      ${this.showMenuButton
        ? html`<button
            class=${iconButtonClasses}
            part="menu-button"
            type="button"
            aria-label=${this.menuLabel}
            aria-expanded=${this.menuExpanded ? 'true' : 'false'}
            @click=${this.onMenu}
          >${renderIcon('menu', 'sl-button__icon')}</button>`
        : nothing}
      <div class="sl-app-bar__leading" part="leading"><slot name="leading"></slot></div>
      ${this.title ? html`<span class="sl-app-bar__title" part="title">${this.title}</span>` : nothing}
      <div class="sl-app-bar__center" part="center"><slot name="center"></slot><slot></slot></div>
      <div class="sl-app-bar__actions" part="actions"><slot name="actions"></slot></div>
    </header>`;
  }

  private onMenu(): void {
    this.dispatchEvent(new CustomEvent('sl-menu-toggle', { bubbles: true, composed: true }));
  }
}

export type DrawerVariant = 'responsive' | 'persistent' | 'temporary' | 'mini';

/**
 * <sl-drawer variant="responsive|persistent|temporary|mini" breakpoint="md" open label="Main">nav…</sl-drawer>
 * Usually driven by <sl-app-shell>. Responsive (default) is persistent from `breakpoint` up and temporary
 * (overlay + scrim) below. Fires `sl-drawer-change` with { open }.
 */
export class SlDrawer extends LitElement {
  static override properties = {
    variant: { reflect: true },
    breakpoint: { reflect: true },
    open: { type: Boolean, reflect: true },
    label: {},
    narrow: { state: true },
  };
  static override styles = [hostReset, styles.layout, css`:host { display: contents; }`];

  declare variant: DrawerVariant;
  declare breakpoint: ResponsiveBreakpoint;
  declare open: boolean;
  declare label: string;
  declare narrow: boolean;

  private media: MediaQueryList | undefined;
  private returnFocusTo: Element | null = null;
  private initialised = false;

  constructor() {
    super();
    this.variant = 'responsive';
    this.breakpoint = 'md';
    this.open = false;
    this.label = 'Navigation';
    this.narrow = false;
  }

  /** True when the drawer currently overlays content (temporary, or responsive below its breakpoint). */
  get overlay(): boolean {
    return this.variant === 'temporary' || (this.variant === 'responsive' && this.narrow);
  }

  /** True when the drawer shows icons only. */
  get mini(): boolean {
    return this.variant === 'mini' && !this.open;
  }

  override connectedCallback(): void {
    super.connectedCallback();
    this.watchMedia();
    if (!this.initialised) {
      this.initialised = true;
      if (!this.hasAttribute('open')) {
        this.open = this.variant === 'persistent' || (this.variant === 'responsive' && !this.narrow);
      }
    }
    document.addEventListener('keydown', this.onDocumentKeyDown);
  }

  override disconnectedCallback(): void {
    super.disconnectedCallback();
    this.media?.removeEventListener?.('change', this.onMediaChange);
    document.removeEventListener('keydown', this.onDocumentKeyDown);
  }

  override willUpdate(changed: Map<string, unknown>): void {
    if (changed.has('breakpoint') && changed.get('breakpoint') !== undefined && this.isConnected) {
      const wasNarrow = this.narrow;
      this.watchMedia();
      if (this.variant === 'responsive' && wasNarrow !== this.narrow) this.setOpen(!this.narrow);
    }
  }

  override updated(): void {
    // Nav items render icon-only in a collapsed mini drawer.
    for (const item of this.querySelectorAll<SlNavItem>('sl-nav-item')) item.mini = this.mini;
  }

  private watchMedia(): void {
    this.media?.removeEventListener?.('change', this.onMediaChange);
    if (typeof matchMedia !== 'function') return;
    this.media = matchMedia(`(max-width: ${breakpointMinWidth(this.breakpoint) - 0.02}px)`);
    this.narrow = this.media.matches;
    this.media.addEventListener?.('change', this.onMediaChange);
  }

  show(): void {
    this.setOpen(true);
  }

  hide(): void {
    this.setOpen(false);
  }

  toggle(): void {
    this.setOpen(!this.open);
  }

  private setOpen(open: boolean): void {
    if (open === this.open) return;
    if (open && this.overlay) this.returnFocusTo = deepActiveElement();
    this.open = open;
    this.dispatchEvent(new CustomEvent('sl-drawer-change', { bubbles: true, composed: true, detail: { open } }));
    if (this.overlay) {
      void this.updateComplete.then(() => {
        if (open) (firstFocusable(this) ?? this.renderRoot.querySelector<HTMLElement>('.sl-drawer'))?.focus();
        else if (this.returnFocusTo instanceof HTMLElement) this.returnFocusTo.focus();
      });
    }
  }

  private readonly onMediaChange = (e: MediaQueryListEvent): void => {
    this.narrow = e.matches;
    if (this.variant === 'responsive') this.setOpen(!e.matches);
  };

  private readonly onDocumentKeyDown = (e: KeyboardEvent): void => {
    if (e.key === 'Escape' && this.open && this.overlay) {
      e.preventDefault();
      this.hide();
    }
  };

  override render() {
    const classes = {
      'sl-drawer': true,
      [`sl-drawer--${this.variant}`]: true,
      [`sl-drawer--breakpoint-${this.breakpoint}`]: this.breakpoint !== 'md',
      'is-overlay': this.overlay,
      'is-open': this.open,
    };
    return html`${this.open && this.overlay ? html`<div class="sl-drawer-scrim" part="scrim" @click=${this.hide}></div>` : nothing}
      <nav class=${classMap(classes)} part="base" aria-label=${this.label} tabindex="-1" ?inert=${!this.open && this.overlay}>
        <slot @slotchange=${() => this.requestUpdate()}></slot>
      </nav>`;
  }
}

/**
 * <sl-nav-item label="Overview" icon="home" href="/" trailing="12" active></sl-nav-item>
 * A drawer navigation entry: a link with `href`, otherwise a button. In a collapsed mini drawer it shows
 * only its icon and exposes the label as a tooltip/accessible name.
 */
export class SlNavItem extends LitElement {
  static override shadowRootOptions = { ...LitElement.shadowRootOptions, delegatesFocus: true };
  static override properties = {
    label: {},
    icon: {},
    trailing: {},
    active: { type: Boolean, reflect: true },
    href: {},
    target: {},
    disabled: { type: Boolean, reflect: true },
    mini: { type: Boolean, reflect: true },
  };
  static override styles = [
    hostReset,
    styles.typography,
    styles.layout,
    css`
      :host { display: block; }
      :host([mini]) .sl-nav__label,
      :host([mini]) .sl-nav__meta { display: none; }
      :host([mini]) .sl-nav__item { justify-content: center; padding: 0; }
      .sl-nav__item { width: 100%; box-sizing: border-box; }
    `,
  ];

  declare label: string;
  declare icon: string | undefined;
  /** Count or short status shown at the end. */
  declare trailing: string | undefined;
  declare active: boolean;
  declare href: string | undefined;
  declare target: string | undefined;
  declare disabled: boolean;
  /** Set by the drawer when collapsed to an icon rail. */
  declare mini: boolean;

  constructor() {
    super();
    this.label = '';
    this.active = false;
    this.disabled = false;
    this.mini = false;
  }

  override render() {
    const classes = { 'sl-nav__item': true, 'is-active': this.active, 'is-disabled': this.disabled };
    const inner = html`${renderIcon(this.icon)}
      <span class="sl-nav__label" part="label"><slot>${this.label}</slot></span>
      ${this.trailing ? html`<span class="sl-nav__meta" part="trailing">${this.trailing}</span>` : nothing}`;
    const name = this.mini ? this.label || undefined : undefined;
    if (this.href && !this.disabled) {
      return html`<a
        class=${classMap(classes)}
        part="base"
        href=${this.href}
        target=${ifDefined(this.target)}
        aria-current=${ifDefined(this.active ? 'page' : undefined)}
        aria-label=${ifDefined(name)}
        title=${ifDefined(name)}
        >${inner}</a
      >`;
    }
    return html`<button
      class=${classMap(classes)}
      part="base"
      type="button"
      ?disabled=${this.disabled}
      aria-current=${ifDefined(this.active ? 'page' : undefined)}
      aria-label=${ifDefined(name)}
      title=${ifDefined(name)}
    >
      ${inner}
    </button>`;
  }
}

export type ContainerSize = 'sm' | 'md' | 'lg' | 'xl' | 'fluid';

/** <sl-container max-width="lg" gutters="false"> — centred, max-width content. */
export class SlContainer extends LitElement {
  static override properties = {
    maxWidth: { attribute: 'max-width', reflect: true },
    gutters: { converter: defaultTrue },
  };
  static override styles = [hostReset, styles.layout, css`:host { display: block; }`];

  declare maxWidth: ContainerSize;
  /** Horizontal padding (default true). */
  declare gutters: boolean;

  constructor() {
    super();
    this.maxWidth = 'lg';
    this.gutters = true;
  }

  override render() {
    return html`<div
      class=${classMap({ 'sl-container': true, [`sl-container--${this.maxWidth}`]: true, 'sl-container--no-gutters': !this.gutters })}
      part="base"
    ><slot></slot></div>`;
  }
}

/** <sl-grid spacing="4"> <sl-grid-item xs="12" md="6">…</sl-grid-item> </sl-grid> */
export class SlGrid extends LitElement {
  static override properties = { spacing: {} };
  static override styles = [hostReset, styles.grid, css`:host { display: block; }`];

  declare spacing: string;

  constructor() {
    super();
    this.spacing = '4';
  }

  override render() {
    return html`<div class="sl-grid" part="base" style=${styleMap({ '--_gap': spaceVar(this.spacing) })}><slot></slot></div>`;
  }
}

/** Grid item with mobile-first spans per breakpoint (xs defaults to 12). */
export class SlGridItem extends LitElement {
  static override properties = {
    xs: { type: Number },
    sm: { type: Number },
    md: { type: Number },
    lg: { type: Number },
    xl: { type: Number },
  };
  static override styles = [
    hostReset,
    css`
      :host {
        display: block;
        min-width: 0;
        grid-column: span var(--_span-xs, 12);
      }
      @media (min-width: 600px) { :host { grid-column: span var(--_span-sm, 12); } }
      @media (min-width: 900px) { :host { grid-column: span var(--_span-md, 12); } }
      @media (min-width: 1200px) { :host { grid-column: span var(--_span-lg, 12); } }
      @media (min-width: 1536px) { :host { grid-column: span var(--_span-xl, 12); } }
    `,
  ];

  declare xs: number | undefined;
  declare sm: number | undefined;
  declare md: number | undefined;
  declare lg: number | undefined;
  declare xl: number | undefined;

  get span(): GridSpan {
    return { xs: this.xs, sm: this.sm, md: this.md, lg: this.lg, xl: this.xl };
  }

  override updated(): void {
    // Every breakpoint is set explicitly so nested items never inherit an ancestor's span.
    for (const bp of breakpoints) this.style.setProperty(`--_span-${bp}`, String(resolveSpan(this.span, bp)));
  }

  override render() {
    return html`<slot></slot>`;
  }
}

/** <sl-stack direction="row" spacing="2" align="center" justify="between" wrap> */
export class SlStack extends LitElement {
  static override properties = {
    direction: { reflect: true },
    spacing: {},
    align: {},
    justify: {},
    wrap: { type: Boolean, reflect: true },
  };
  static override styles = [hostReset, styles.layout, css`:host { display: block; min-width: 0; }`];

  declare direction: 'row' | 'column';
  declare spacing: string;
  declare align: 'start' | 'center' | 'end' | 'stretch' | 'baseline';
  declare justify: 'start' | 'center' | 'end' | 'between';
  declare wrap: boolean;

  constructor() {
    super();
    this.direction = 'column';
    this.spacing = '3';
    this.align = 'stretch';
    this.justify = 'start';
    this.wrap = false;
  }

  override render() {
    const classes = {
      'sl-stack': true,
      'sl-stack--row': this.direction === 'row',
      'sl-stack--wrap': this.wrap,
      [`sl-stack--align-${this.align}`]: this.align !== 'stretch',
      [`sl-stack--justify-${this.justify}`]: this.justify !== 'start',
    };
    return html`<div class=${classMap(classes)} part="base" style=${styleMap({ '--_gap': spaceVar(this.spacing) })}><slot></slot></div>`;
  }
}

/** <sl-spacer> — pushes siblings apart inside a row. */
export class SlSpacer extends LitElement {
  static override styles = css`:host { display: block; flex: 1 1 auto; }`;
  override render() {
    return nothing;
  }
}

/** <sl-divider vertical> */
export class SlDivider extends LitElement {
  static override properties = { vertical: { type: Boolean, reflect: true } };
  static override styles = [hostReset, styles.layout, css`:host { display: contents; }`];

  declare vertical: boolean;

  constructor() {
    super();
    this.vertical = false;
  }

  override render() {
    return html`<div
      class=${classMap({ 'sl-divider': true, 'sl-divider--vertical': this.vertical })}
      part="base"
      role="separator"
      aria-orientation=${this.vertical ? 'vertical' : 'horizontal'}
    ></div>`;
  }
}

/**
 * <sl-card variant="elevated|outlined|flat" radius="large" title="…" subtitle="…" interactive flush>
 *   body… <div slot="header-actions"> <div slot="footer"> (or <div slot="header"> to replace the title block)
 * </sl-card>
 */
export class SlCard extends TitledElement {
  static override properties = {
    variant: { reflect: true },
    radius: { reflect: true },
    title: {},
    subtitle: {},
    interactive: { type: Boolean, reflect: true },
    flush: { type: Boolean, reflect: true },
    slotted: { state: true },
  };
  static override styles = [hostReset, styles.tone, styles.layout, css`:host { display: block; min-width: 0; }`];

  declare variant: CardVariant | undefined;
  declare radius: Radius | undefined;
  declare title: string;
  declare subtitle: string | undefined;
  declare interactive: boolean;
  /** Remove body padding (tables, media). */
  declare flush: boolean;
  declare slotted: { header: boolean; 'header-actions': boolean; footer: boolean };

  constructor() {
    super();
    new DefaultsController(this);
    this.title = '';
    this.interactive = false;
    this.flush = false;
    this.slotted = { header: false, 'header-actions': false, footer: false };
  }

  get resolved(): { variant: CardVariant; radius: Radius } {
    const d = getDefaults().card;
    return { variant: this.variant ?? d.variant, radius: this.radius ?? d.radius };
  }

  override connectedCallback(): void {
    super.connectedCallback();
    const has = (name: string) => !!this.querySelector(`:scope > [slot="${name}"]`);
    this.slotted = { header: has('header'), 'header-actions': has('header-actions'), footer: has('footer') };
  }

  override render() {
    const { variant, radius } = this.resolved;
    const showHeader = !!(this.title || this.subtitle || this.slotted.header || this.slotted['header-actions']);
    const classes = {
      'sl-card': true,
      [`sl-card--${variant}`]: true,
      [`sl-radius-${radius}`]: radius !== 'default',
      'sl-card--interactive': this.interactive,
    };
    return html`<article class=${classMap(classes)} part="base" tabindex=${ifDefined(this.interactive ? '0' : undefined)}>
      <header class="sl-card__header" part="header" ?hidden=${!showHeader}>
        <slot name="header" @slotchange=${this.onSlotChange}>
          <div class="sl-card__titles">
            ${this.title ? html`<h3 class="sl-card__title" part="title">${this.title}</h3>` : nothing}
            ${this.subtitle ? html`<p class="sl-card__subtitle" part="subtitle">${this.subtitle}</p>` : nothing}
          </div>
        </slot>
        <div class="sl-card__actions" part="actions"><slot name="header-actions" @slotchange=${this.onSlotChange}></slot></div>
      </header>
      <div class=${classMap({ 'sl-card__body': true, 'sl-card__body--flush': this.flush })} part="body"><slot></slot></div>
      <footer class="sl-card__footer" part="footer" ?hidden=${!this.slotted.footer}>
        <slot name="footer" @slotchange=${this.onSlotChange}></slot>
      </footer>
    </article>`;
  }

  private onSlotChange(e: Event): void {
    const slot = e.target as HTMLSlotElement;
    this.slotted = { ...this.slotted, [slot.name]: slot.assignedElements().length > 0 };
  }
}

/** <sl-toolbar label="Formatting" flat> buttons, <sl-divider vertical>, … </sl-toolbar> */
export class SlToolbar extends LitElement {
  static override properties = { label: {}, flat: { type: Boolean, reflect: true } };
  static override styles = [hostReset, styles.layout, css`:host { display: inline-flex; } :host([flat]) { display: flex; }`];

  declare label: string | undefined;
  declare flat: boolean;

  constructor() {
    super();
    this.flat = false;
  }

  override render() {
    return html`<div
      class=${classMap({ 'sl-toolbar': true, 'sl-toolbar--flat': this.flat })}
      part="base"
      role="toolbar"
      aria-label=${ifDefined(this.label)}
    >
      <slot></slot>
    </div>`;
  }
}
