import { LitElement, css, html, nothing, type PropertyValues } from 'lit';
import { classMap } from 'lit/directives/class-map.js';
import { hostReset, styles } from '../internal/styles';
import { renderIcon } from '../internal/icon';
import { uid } from '../internal/dom';
import { moveIndex, type ListKey } from '../core/collections/list';
import type { ControlSize, Direction } from '../core/defaults';

export type TabsVariant = 'line' | 'pills' | 'enclosed';

/**
 * <sl-tab key="logs" label="Logs" icon="file" badge="12" closable></sl-tab> — only inside <sl-tabs>.
 * Fires `sl-closed` (detail: { key }) when its close button (or Delete) is used; removing it is up to the app.
 */
export class SlTab extends LitElement {
  static override properties = {
    key: { reflect: true },
    label: {},
    icon: {},
    badge: {},
    closable: { type: Boolean, reflect: true },
    disabled: { type: Boolean, reflect: true },
    selected: { type: Boolean, reflect: true },
    variant: { attribute: false },
    size: { attribute: false },
    direction: { attribute: false },
  };

  static override styles = [
    hostReset,
    styles.base,
    styles.navigation,
    css`
      :host {
        display: inline-flex;
        outline: none;
      }
      :host(:focus-visible) .sl-tab {
        outline: var(--sl-focus-ring-width) solid var(--sl-color-focus-ring);
        outline-offset: calc(var(--sl-focus-ring-width) * -1);
        border-radius: var(--sl-radius-sm);
      }
    `,
  ];

  declare key: string;
  declare label: string | undefined;
  declare icon: string | undefined;
  declare badge: string | undefined;
  declare closable: boolean;
  declare disabled: boolean;
  /** Managed by <sl-tabs>. */
  declare selected: boolean;
  /** Managed by <sl-tabs>. */
  declare variant: TabsVariant;
  /** Managed by <sl-tabs>. */
  declare size: ControlSize;
  /** Managed by <sl-tabs>. */
  declare direction: Direction;

  constructor() {
    super();
    this.key = '';
    this.closable = false;
    this.disabled = false;
    this.selected = false;
    this.variant = 'line';
    this.size = 'medium';
    this.direction = 'row';
  }

  override connectedCallback(): void {
    super.connectedCallback();
    if (!this.id) this.id = uid('sl-tab');
    this.setAttribute('role', 'tab');
    if (this.parentElement?.localName === 'sl-tabs' && !this.slot) this.slot = 'tab';
  }

  protected override updated(): void {
    this.setAttribute('aria-selected', String(this.selected));
    if (this.disabled) this.setAttribute('aria-disabled', 'true');
    else this.removeAttribute('aria-disabled');
  }

  close(): void {
    this.dispatchEvent(new CustomEvent('sl-closed', { bubbles: true, composed: true, detail: { key: this.key } }));
  }

  override render() {
    const classes = {
      'sl-tab': true,
      [`sl-tab--${this.variant}`]: true,
      [`sl-tab--${this.size}`]: this.size !== 'medium',
      'sl-tab--column': this.direction === 'column',
      'is-selected': this.selected,
      'is-disabled': this.disabled,
    };
    return html`<span class=${classMap(classes)} part="tab">
      ${this.icon ? html`<span class="sl-tab__icon">${renderIcon(this.icon)}</span>` : nothing}
      <span class="sl-tab__label"><slot>${this.label ?? ''}</slot></span>
      ${this.badge ? html`<span class="sl-tab__badge">${this.badge}</span>` : nothing}
      ${this.closable
        ? html`<button
            class="sl-tab__close"
            type="button"
            tabindex="-1"
            aria-label=${`Close ${this.label ?? this.key}`}
            @click=${(e: Event) => {
              e.stopPropagation();
              this.close();
            }}
          >
            ${renderIcon('x')}
          </button>`
        : nothing}
    </span>`;
  }
}

/** <sl-tab-panel key="logs">…</sl-tab-panel> — shown when its key is the selected tab. */
export class SlTabPanel extends LitElement {
  static override properties = {
    key: { reflect: true },
    direction: { attribute: false },
  };

  static override styles = [hostReset, styles.base, styles.navigation, css`:host { display: block; }`];

  declare key: string;
  /** Managed by <sl-tabs>. */
  declare direction: Direction;

  constructor() {
    super();
    this.key = '';
    this.direction = 'row';
  }

  override connectedCallback(): void {
    super.connectedCallback();
    if (!this.id) this.id = uid('sl-tab-panel');
    this.setAttribute('role', 'tabpanel');
    if (!this.hasAttribute('tabindex')) this.tabIndex = 0;
  }

  override render() {
    return html`<div class=${classMap({ 'sl-tab-panel': true, 'sl-tab-panel--column': this.direction === 'column' })} part="panel"><slot></slot></div>`;
  }
}

const rowKeys: Record<string, ListKey> = { ArrowRight: 'next', ArrowLeft: 'previous', Home: 'first', End: 'last' };
const columnKeys: Record<string, ListKey> = { ArrowDown: 'next', ArrowUp: 'previous', Home: 'first', End: 'last' };

/**
 * <sl-tabs value="overview" variant="line|pills|enclosed" size="small" direction="row|column">
 *   <sl-tab key="overview" label="Overview"></sl-tab>
 *   <sl-tab key="logs" label="Logs" badge="12"></sl-tab>
 *   <sl-tab-panel key="overview">…</sl-tab-panel>
 *   <sl-tab-panel key="logs">…</sl-tab-panel>
 * </sl-tabs>
 *
 * WAI-ARIA tabs with automatic activation: arrows (Left/Right, or Up/Down in a column) move and select, Home/End,
 * Delete closes a closable tab. `value` is two-way via `sl-value-changed` (detail: { value }). Panels are light-DOM
 * children, so they always stay mounted on the web (`keep-alive` is accepted for API parity).
 * Overflowing tab lists scroll, with previous/next buttons.
 */
export class SlTabs extends LitElement {
  static override properties = {
    value: { reflect: true },
    variant: { reflect: true },
    size: { reflect: true },
    direction: { reflect: true },
    keepAlive: { type: Boolean, attribute: 'keep-alive', reflect: true },
    overflowing: { state: true },
    indicator: { state: true },
  };

  static override styles = [hostReset, styles.base, styles.button, styles.tone, styles.navigation, css`:host { display: block; }`];

  declare value: string;
  declare variant: TabsVariant;
  declare size: ControlSize;
  declare direction: Direction;
  declare keepAlive: boolean;
  declare overflowing: boolean;
  declare indicator: { x: number; y: number; w: number; h: number };

  private observer: ResizeObserver | undefined;

  constructor() {
    super();
    this.value = '';
    this.variant = 'line';
    this.size = 'medium';
    this.direction = 'row';
    this.keepAlive = false;
    this.overflowing = false;
    this.indicator = { x: 0, y: 0, w: 0, h: 0 };
    this.addEventListener('sl-closed', () => this.updateComplete.then(() => this.sync()));
    // Host listeners so slotted tabs are handled in every DOM implementation.
    this.addEventListener('click', (e) => this.onClick(e));
    this.addEventListener('keydown', (e) => this.onKeyDown(e));
  }

  get tabs(): SlTab[] {
    return [...this.querySelectorAll<SlTab>(':scope > sl-tab')];
  }

  get panels(): SlTabPanel[] {
    return [...this.querySelectorAll<SlTabPanel>(':scope > sl-tab-panel')];
  }

  private get list(): HTMLElement | null {
    return this.renderRoot?.querySelector('.sl-tabs__list') ?? null;
  }

  override connectedCallback(): void {
    super.connectedCallback();
    if (typeof ResizeObserver !== 'undefined') {
      this.observer = new ResizeObserver(() => this.measure());
    }
  }

  override disconnectedCallback(): void {
    super.disconnectedCallback();
    this.observer?.disconnect();
  }

  protected override firstUpdated(): void {
    const list = this.list;
    if (list) this.observer?.observe(list);
    this.sync();
  }

  protected override updated(changed: PropertyValues<this>): void {
    if (changed.has('value') || changed.has('variant') || changed.has('size') || changed.has('direction')) this.sync();
  }

  /** Selects a tab by key (as a user would) and notifies. */
  select(key: string): void {
    const tab = this.tabs.find((t) => t.key === key);
    if (!tab || tab.disabled || key === this.value) return;
    this.value = key;
    this.dispatchEvent(new CustomEvent('sl-value-changed', { bubbles: true, composed: true, detail: { value: key } }));
  }

  private sync(): void {
    const tabs = this.tabs;
    if (!this.value || !tabs.some((t) => t.key === this.value)) {
      const first = tabs.find((t) => !t.disabled);
      if (first && this.value !== first.key) this.value = first.key;
    }
    const panels = this.panels;
    for (const tab of tabs) {
      if (!tab.slot) tab.slot = 'tab';
      const selected = tab.key === this.value;
      tab.selected = selected;
      tab.variant = this.variant;
      tab.size = this.size;
      tab.direction = this.direction;
      tab.tabIndex = selected ? 0 : -1;
      const panel = panels.find((p) => p.key === tab.key);
      if (panel) {
        tab.setAttribute('aria-controls', panel.id);
        panel.setAttribute('aria-labelledby', tab.id);
      }
    }
    for (const panel of panels) {
      panel.direction = this.direction;
      panel.hidden = panel.key !== this.value;
    }
    requestAnimationFrame(() => this.measure());
  }

  private measure(): void {
    const list = this.list;
    const tab = this.tabs.find((t) => t.selected);
    if (!list) return;
    this.overflowing = this.direction === 'row' && list.scrollWidth > list.clientWidth + 1;
    if (!tab) return;
    const l = list.getBoundingClientRect();
    const t = tab.getBoundingClientRect();
    this.indicator = { x: t.left - l.left + list.scrollLeft, y: t.top - l.top + list.scrollTop, w: t.width, h: t.height };
  }

  private onClick(e: Event): void {
    const tab = e.composedPath().find((n): n is SlTab => n instanceof SlTab);
    if (tab) this.select(tab.key);
  }

  private onKeyDown(e: KeyboardEvent): void {
    const tabs = this.tabs;
    const tab = e.composedPath().find((n): n is SlTab => n instanceof SlTab);
    const current = tab ? tabs.indexOf(tab) : -1;
    if (current < 0) return;
    if (e.key === 'Delete' && tabs[current].closable) {
      e.preventDefault();
      tabs[current].close();
      return;
    }
    const map = this.direction === 'column' ? columnKeys : rowKeys;
    const key = map[e.key];
    if (!key) return;
    e.preventDefault();
    const next = moveIndex(current, key, tabs.map((t) => t.disabled), { wrap: true });
    const target = tabs[next];
    if (!target) return;
    target.focus();
    target.scrollIntoView?.({ block: 'nearest', inline: 'nearest' });
    this.select(target.key);
  }

  private scrollTabs(direction: -1 | 1): void {
    const list = this.list;
    list?.scrollBy?.({ left: direction * list.clientWidth * 0.8, behavior: 'smooth' });
  }

  override render() {
    const classes = {
      'sl-tabs': true,
      [`sl-tabs--${this.variant}`]: true,
      [`sl-tabs--${this.size}`]: this.size !== 'medium',
      'sl-tabs--column': this.direction === 'column',
    };
    const i = this.indicator;
    return html`<div class=${classMap(classes)} part="base">
      <div class="sl-tabs__bar">
        <div
          class="sl-tabs__list"
          part="list"
          role="tablist"
          aria-orientation=${this.direction === 'column' ? 'vertical' : 'horizontal'}
          @scroll=${() => this.measure()}
        >
          <slot name="tab" @slotchange=${() => this.sync()}></slot>
          <span
            class="sl-tabs__indicator"
            part="indicator"
            aria-hidden="true"
            style=${`--_x:${i.x}px;--_y:${i.y}px;--_w:${i.w}px;--_h:${i.h}px`}
          ></span>
        </div>
        <div class="sl-tabs__overflow" part="overflow" ?hidden=${!this.overflowing}>
          <button class="sl-button sl-button--ghost sl-tone-neutral sl-button--small sl-button--icon-only" type="button" aria-label="Scroll tabs left" @click=${() => this.scrollTabs(-1)}>
            ${renderIcon('chevron-left')}
          </button>
          <button class="sl-button sl-button--ghost sl-tone-neutral sl-button--small sl-button--icon-only" type="button" aria-label="Scroll tabs right" @click=${() => this.scrollTabs(1)}>
            ${renderIcon('chevron-right')}
          </button>
        </div>
      </div>
      <div class="sl-tabs__panels"><slot @slotchange=${() => this.sync()}></slot></div>
    </div>`;
  }
}
