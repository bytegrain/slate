import { LitElement, css, html, nothing } from 'lit';
import { classMap } from 'lit/directives/class-map.js';
import { ifDefined } from 'lit/directives/if-defined.js';
import { styleMap } from 'lit/directives/style-map.js';
import { hostReset, styles } from '../internal/styles';
import { renderIcon } from '../internal/icon';
import { deepActiveElement, firstFocusable } from '../internal/dom';
import { breakpointMinWidth, breakpoints, resolveSpan, spaceVar, type GridSpan } from '../core/layout';

/**
 * <sl-app-shell viewport> with <sl-app-bar slot="app-bar">, <sl-drawer slot="drawer"> and main content.
 * The app bar's menu button toggles the drawer.
 */
export class SlAppShell extends LitElement {
  static override properties = {
    viewport: { type: Boolean, reflect: true },
    flush: { type: Boolean, reflect: true },
  };
  static override styles = [
    hostReset,
    styles.layout,
    css`
      :host { display: block; height: 100%; min-height: 0; }
      :host([viewport]) { height: 100vh; height: 100dvh; }
    `,
  ];

  /** Fill the viewport (use when the shell is the page root). */
  declare viewport: boolean;
  /** Remove the main area's padding. */
  declare flush: boolean;

  constructor() {
    super();
    this.viewport = false;
    this.flush = false;
    this.addEventListener('sl-menu-toggle', () => this.drawer?.toggle());
    this.addEventListener('sl-drawer-change', (e) => {
      const open = (e as CustomEvent<{ open: boolean }>).detail.open;
      const bar = this.querySelector<SlAppBar>(':scope > sl-app-bar');
      if (bar) bar.menuExpanded = open;
    });
  }

  get drawer(): SlDrawer | null {
    return this.querySelector<SlDrawer>(':scope > sl-drawer');
  }

  override render() {
    return html`<div class="sl-app-shell" part="base">
      <slot name="app-bar"></slot>
      <slot name="drawer"></slot>
      <main class=${classMap({ 'sl-main': true, 'sl-main--flush': this.flush })} part="main"><slot></slot></main>
    </div>`;
  }
}

/** <sl-app-bar menu-button> brand, actions… </sl-app-bar> */
export class SlAppBar extends LitElement {
  static override properties = {
    menuButton: { type: Boolean, attribute: 'menu-button', reflect: true },
    menuLabel: { attribute: 'menu-label' },
    menuExpanded: { type: Boolean, attribute: false },
  };
  static override styles = [hostReset, styles.base, styles.button, styles.typography, styles.layout, css`:host { display: contents; }`];

  declare menuButton: boolean;
  declare menuLabel: string;
  declare menuExpanded: boolean;

  constructor() {
    super();
    this.menuButton = false;
    this.menuLabel = 'Toggle navigation';
    this.menuExpanded = true;
  }

  override render() {
    return html`<header class="sl-app-bar" part="base">
      ${this.menuButton
        ? html`<button
            class="sl-button sl-button--ghost sl-button--icon"
            part="menu-button"
            type="button"
            aria-label=${this.menuLabel}
            aria-expanded=${this.menuExpanded ? 'true' : 'false'}
            @click=${this.onMenu}
          >${renderIcon('menu', 'sl-button__icon')}</button>`
        : nothing}
      <slot></slot>
    </header>`;
  }

  private onMenu(): void {
    this.dispatchEvent(new CustomEvent('sl-menu-toggle', { bubbles: true, composed: true }));
  }
}

export type DrawerVariant = 'responsive' | 'persistent' | 'temporary' | 'mini';

/**
 * <sl-drawer variant="responsive|persistent|temporary|mini" open label="Main">nav…</sl-drawer>
 * Responsive (default) is persistent from `md` up and temporary (overlay + scrim) below.
 * Fires `sl-drawer-change` with { open }.
 */
export class SlDrawer extends LitElement {
  static override properties = {
    variant: { reflect: true },
    open: { type: Boolean, reflect: true },
    label: {},
    narrow: { state: true },
  };
  static override styles = [hostReset, styles.layout, css`:host { display: contents; }`];

  declare variant: DrawerVariant;
  declare open: boolean;
  declare label: string;
  declare narrow: boolean;

  private media: MediaQueryList | undefined;
  private returnFocusTo: Element | null = null;
  private initialised = false;

  constructor() {
    super();
    this.variant = 'responsive';
    this.open = false;
    this.label = 'Navigation';
    this.narrow = false;
  }

  /** True when the drawer currently overlays content (temporary, or responsive below md). */
  get overlay(): boolean {
    return this.variant === 'temporary' || (this.variant === 'responsive' && this.narrow);
  }

  override connectedCallback(): void {
    super.connectedCallback();
    if (typeof matchMedia === 'function') {
      this.media = matchMedia(`(max-width: ${breakpointMinWidth('md') - 0.02}px)`);
      this.narrow = this.media.matches;
      this.media.addEventListener?.('change', this.onMediaChange);
    }
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
      'is-open': this.open,
    };
    return html`${this.open && this.overlay ? html`<div class="sl-drawer-scrim" part="scrim" @click=${this.hide}></div>` : nothing}
      <nav class=${classMap(classes)} part="base" aria-label=${this.label} tabindex="-1" ?inert=${!this.open && this.overlay}>
        <slot></slot>
      </nav>`;
  }
}

export type ContainerSize = 'sm' | 'md' | 'lg' | 'xl' | 'fluid';

/** <sl-container size="lg"> — centred, max-width content. */
export class SlContainer extends LitElement {
  static override properties = { size: { reflect: true } };
  static override styles = [hostReset, styles.layout, css`:host { display: block; }`];

  declare size: ContainerSize;

  constructor() {
    super();
    this.size = 'lg';
  }

  override render() {
    return html`<div class="sl-container sl-container--${this.size}" part="base"><slot></slot></div>`;
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
  declare align: 'start' | 'center' | 'end' | 'stretch' | 'baseline' | undefined;
  declare justify: 'start' | 'center' | 'end' | 'between' | undefined;
  declare wrap: boolean;

  constructor() {
    super();
    this.direction = 'column';
    this.spacing = '3';
    this.wrap = false;
  }

  override render() {
    const classes = {
      'sl-stack': true,
      'sl-stack--row': this.direction === 'row',
      'sl-stack--wrap': this.wrap,
      [`sl-stack--align-${this.align}`]: !!this.align,
      [`sl-stack--justify-${this.justify}`]: !!this.justify,
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

/** <sl-card heading="…" subheading="…" outlined interactive> body <div slot="actions"> <div slot="footer"> */
export class SlCard extends LitElement {
  static override properties = {
    heading: {},
    subheading: {},
    outlined: { type: Boolean, reflect: true },
    interactive: { type: Boolean, reflect: true },
    flush: { type: Boolean, reflect: true },
    hasActions: { state: true },
    hasFooter: { state: true },
  };
  static override styles = [hostReset, styles.layout, css`:host { display: block; min-width: 0; }`];

  declare heading: string | undefined;
  declare subheading: string | undefined;
  declare outlined: boolean;
  declare interactive: boolean;
  /** Remove body padding (tables, media). */
  declare flush: boolean;
  declare hasActions: boolean;
  declare hasFooter: boolean;

  constructor() {
    super();
    this.outlined = false;
    this.interactive = false;
    this.flush = false;
    this.hasActions = false;
    this.hasFooter = false;
  }

  override connectedCallback(): void {
    super.connectedCallback();
    this.hasActions = !!this.querySelector(':scope > [slot="actions"]');
    this.hasFooter = !!this.querySelector(':scope > [slot="footer"]');
  }

  override render() {
    const showHeader = !!(this.heading || this.subheading || this.hasActions);
    const classes = {
      'sl-card': true,
      'sl-card--outlined': this.outlined,
      'sl-card--interactive': this.interactive,
    };
    return html`<article class=${classMap(classes)} part="base" tabindex=${ifDefined(this.interactive ? '0' : undefined)}>
      <header class="sl-card__header" part="header" ?hidden=${!showHeader}>
        <div class="sl-card__titles">
          ${this.heading ? html`<h3 class="sl-card__title">${this.heading}</h3>` : nothing}
          ${this.subheading ? html`<p class="sl-card__subtitle">${this.subheading}</p>` : nothing}
        </div>
        <div class="sl-card__actions"><slot name="actions" @slotchange=${this.onSlotChange}></slot></div>
      </header>
      <div class=${classMap({ 'sl-card__body': true, 'sl-card__body--flush': this.flush })} part="body"><slot></slot></div>
      <footer class="sl-card__footer" part="footer" ?hidden=${!this.hasFooter}>
        <slot name="footer" @slotchange=${this.onSlotChange}></slot>
      </footer>
    </article>`;
  }

  private onSlotChange(e: Event): void {
    const slot = e.target as HTMLSlotElement;
    const filled = slot.assignedElements().length > 0;
    if (slot.name === 'actions') this.hasActions = filled;
    else this.hasFooter = filled;
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
