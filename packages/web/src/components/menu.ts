import { LitElement, css, html, nothing, type PropertyValues } from 'lit';
import { classMap } from 'lit/directives/class-map.js';
import { hostReset, styles } from '../internal/styles';
import { renderIcon } from '../internal/icon';
import { uid } from '../internal/dom';
import { Overlay } from '../internal/overlay';
import { moveIndex, Typeahead, type ListKey } from '../core/collections/list';
import type { PopoverPlacement } from '../core/overlay/positioning';
import type { Tone } from '../core/defaults';

/** `checked` attribute: absent → not checkable (undefined), "false" → unchecked, anything else → checked. */
const optionalBoolean = {
  fromAttribute: (value: string | null) => (value === null ? undefined : value !== 'false'),
  toAttribute: (value: boolean | undefined) => (value === undefined ? null : value ? '' : 'false'),
};

const listKeys: Record<string, ListKey> = { ArrowDown: 'next', ArrowUp: 'previous', Home: 'first', End: 'last' };

/** Roving focus + typeahead over a list of menu items (shared by menus and submenus). */
class MenuKeyboard {
  private readonly typeahead = new Typeahead();

  constructor(private readonly items: () => SlMenuItem[]) {}

  focusAt(which: 'first' | 'last' | number): void {
    const items = this.items();
    const disabled = items.map((i) => i.disabled || i.separator);
    const index = which === 'first' ? moveIndex(-1, 'first', disabled) : which === 'last' ? moveIndex(-1, 'last', disabled) : which;
    items[index]?.focus();
  }

  /** Handles navigation keys; returns true when the key was used. */
  handle(e: KeyboardEvent): boolean {
    const items = this.items();
    const disabled = items.map((i) => i.disabled || i.separator);
    const current = items.findIndex((i) => i === document.activeElement || i.matches(':focus'));
    const key = listKeys[e.key];
    if (key) {
      const next = moveIndex(current, key, disabled, { wrap: true });
      items[next]?.focus();
      return true;
    }
    if (e.key.length === 1 && !e.ctrlKey && !e.metaKey && !e.altKey && e.key !== ' ') {
      const labels = items.map((i) => i.text);
      const found = this.typeahead.search(e.key, Date.now(), labels, current, disabled);
      if (found >= 0) items[found].focus();
      return true;
    }
    return false;
  }
}

/**
 * <sl-menu-item label="Rename" icon="pencil" shortcut="F2"></sl-menu-item>
 * <sl-menu-item label="Show hidden" checked="false"></sl-menu-item>      (checkable: menuitemcheckbox)
 * <sl-menu-item separator></sl-menu-item>
 * <sl-menu-item label="Export">  <sl-menu-item slot="submenu" label="CSV"></sl-menu-item> … </sl-menu-item>
 *
 * Activating an item (click, Enter, Space) toggles `checked` when checkable, follows `href`, and dispatches
 * `sl-select` (detail: { item, checked }) which closes the menu.
 */
export class SlMenuItem extends LitElement {
  static override properties = {
    label: {},
    icon: {},
    shortcut: {},
    tone: { reflect: true },
    disabled: { type: Boolean, reflect: true },
    checked: { converter: optionalBoolean, reflect: true },
    href: {},
    separator: { type: Boolean, reflect: true },
    value: {},
    hasSubmenu: { state: true },
    submenuOpen: { state: true },
  };

  static override styles = [
    hostReset,
    styles.base,
    styles.tone,
    styles.overlay,
    css`
      :host {
        display: block;
        outline: none;
      }
      :host(:focus) .sl-menu-item {
        background: var(--sl-component-menu-item-hover);
      }
      :host(:focus-visible) .sl-menu-item {
        box-shadow: inset 0 0 0 var(--sl-focus-ring-width) var(--sl-color-focus-ring);
      }
    `,
  ];

  declare label: string | undefined;
  declare icon: string | undefined;
  declare shortcut: string | undefined;
  declare tone: Tone;
  declare disabled: boolean;
  /** Undefined = not checkable. */
  declare checked: boolean | undefined;
  declare href: string | undefined;
  declare separator: boolean;
  /** Free-form value passed along in `sl-select`. */
  declare value: string | undefined;
  declare hasSubmenu: boolean;
  declare submenuOpen: boolean;

  private readonly keyboard = new MenuKeyboard(() => this.submenuItems);
  private readonly overlay = new Overlay({
    anchor: () => this,
    panel: () => this.renderRoot?.querySelector<HTMLElement>('.sl-menu__panel') ?? null,
    placement: () => 'right-start',
    offset: () => 2,
    onDismiss: (reason) => this.closeSubmenu(reason === 'escape'),
  });

  constructor() {
    super();
    this.tone = 'neutral';
    this.disabled = false;
    this.separator = false;
    this.hasSubmenu = false;
    this.submenuOpen = false;
    this.addEventListener('click', this.onClick);
    this.addEventListener('keydown', this.onKeyDown);
    this.addEventListener('pointerenter', this.onPointerEnter);
  }

  /** Text used for typeahead and accessible names. */
  get text(): string {
    return (this.label ?? this.textContent ?? '').trim();
  }

  get submenuItems(): SlMenuItem[] {
    return [...this.querySelectorAll<SlMenuItem>(':scope > sl-menu-item[slot="submenu"]')];
  }

  override connectedCallback(): void {
    super.connectedCallback();
    if (!this.hasAttribute('tabindex')) this.tabIndex = -1;
    this.hasSubmenu = this.submenuItems.length > 0;
  }

  override disconnectedCallback(): void {
    super.disconnectedCallback();
    this.overlay.close();
  }

  protected override updated(changed: PropertyValues<this>): void {
    if (this.separator) {
      this.setAttribute('role', 'separator');
      this.removeAttribute('tabindex');
    } else {
      this.setAttribute('role', this.checked === undefined ? 'menuitem' : 'menuitemcheckbox');
      if (this.checked === undefined) this.removeAttribute('aria-checked');
      else this.setAttribute('aria-checked', String(this.checked));
      this.setAttribute('aria-disabled', String(this.disabled));
      if (this.hasSubmenu) {
        this.setAttribute('aria-haspopup', 'menu');
        this.setAttribute('aria-expanded', String(this.submenuOpen));
      }
    }
    if (changed.has('submenuOpen')) {
      if (this.submenuOpen) this.overlay.open();
      else this.overlay.close();
    }
  }

  openSubmenu(focusFirst = false): void {
    if (!this.hasSubmenu || this.disabled) return;
    for (const sibling of this.parentElement?.querySelectorAll<SlMenuItem>(':scope > sl-menu-item') ?? []) {
      if (sibling !== this) sibling.closeSubmenu(false);
    }
    this.submenuOpen = true;
    if (focusFirst) this.updateComplete.then(() => this.keyboard.focusAt('first'));
  }

  closeSubmenu(refocus: boolean): void {
    for (const child of this.submenuItems) child.closeSubmenu(false);
    if (!this.submenuOpen) return;
    this.submenuOpen = false;
    if (refocus) this.focus();
  }

  private readonly onPointerEnter = (): void => {
    if (this.separator) return;
    if (this.hasSubmenu) this.openSubmenu();
    else for (const sibling of this.parentElement?.querySelectorAll<SlMenuItem>(':scope > sl-menu-item') ?? []) sibling.closeSubmenu(false);
    if (!this.disabled) this.focus({ preventScroll: true });
  };

  private readonly onClick = (e: MouseEvent): void => {
    if (e.composedPath()[0] !== this && this.submenuItems.some((i) => e.composedPath().includes(i))) return; // from a submenu item
    if (this.separator || this.disabled) {
      e.preventDefault();
      e.stopImmediatePropagation();
      return;
    }
    if (this.hasSubmenu) {
      this.openSubmenu(true);
      return;
    }
    if (this.checked !== undefined) this.checked = !this.checked;
    if (this.href) {
      const link = this.renderRoot.querySelector<HTMLAnchorElement>('a');
      if (link && e.composedPath()[0] !== link && !e.composedPath().includes(link)) link.click();
    }
    this.dispatchEvent(new CustomEvent('sl-select', { bubbles: true, composed: true, detail: { item: this, value: this.value ?? this.text, checked: this.checked } }));
  };

  private readonly onKeyDown = (e: KeyboardEvent): void => {
    if (e.target !== this) {
      // Navigation inside the open submenu.
      if (this.keyboard.handle(e)) {
        e.preventDefault();
        e.stopPropagation();
      } else if (e.key === 'ArrowLeft') {
        e.preventDefault();
        e.stopPropagation();
        this.closeSubmenu(true);
      }
      return;
    }
    if (e.key === 'Enter' || e.key === ' ') {
      e.preventDefault();
      e.stopPropagation();
      this.click();
    } else if (e.key === 'ArrowRight' && this.hasSubmenu) {
      e.preventDefault();
      e.stopPropagation();
      this.openSubmenu(true);
    }
  };

  private onSubmenuSlotChange(): void {
    this.hasSubmenu = this.submenuItems.length > 0;
  }

  override render() {
    if (this.separator) return html`<div class="sl-menu-separator" part="separator"></div>`;
    const classes = {
      'sl-menu-item': true,
      [`sl-tone-${this.tone}`]: true,
      'is-checked': this.checked === true,
      'is-disabled': this.disabled,
      'is-expanded': this.submenuOpen,
    };
    const content = html`${this.checked !== undefined ? html`<span class="sl-menu-item__check">${renderIcon('check')}</span>` : nothing}
      ${this.icon ? html`<span class="sl-menu-item__icon">${renderIcon(this.icon)}</span>` : nothing}
      <span class="sl-menu-item__label"><slot>${this.label ?? ''}</slot></span>
      ${this.shortcut ? html`<kbd class="sl-menu-item__shortcut" part="shortcut">${this.shortcut}</kbd>` : nothing}
      ${this.hasSubmenu ? html`<span class="sl-menu-item__chevron">${renderIcon('chevron-right')}</span>` : nothing}`;

    return html`${this.href
        ? html`<a class=${classMap(classes)} part="item" href=${this.href} tabindex="-1">${content}</a>`
        : html`<div class=${classMap(classes)} part="item">${content}</div>`}
      <div class="sl-popover-panel sl-menu__panel" role="menu" part="submenu" aria-label=${this.text} hidden>
        <slot name="submenu" @slotchange=${this.onSubmenuSlotChange}></slot>
      </div>`;
  }
}

/**
 * <sl-menu placement="bottom-start">
 *   <sl-button slot="trigger" end-icon="chevron-down">Actions</sl-button>
 *   <sl-menu-item label="Rename" icon="pencil" shortcut="F2"></sl-menu-item>
 *   <sl-menu-item separator></sl-menu-item>
 *   <sl-menu-item label="Delete" icon="trash" tone="danger"></sl-menu-item>
 * </sl-menu>
 *
 * Items may also use `slot="items"`. With `context-menu`, the trigger slot is the area that opens the menu at the
 * pointer on right-click (or Shift+F10 / the Menu key). Keyboard: Enter/Space/ArrowDown open on the first item,
 * ArrowUp on the last; arrows, Home/End and typeahead move; Escape closes and returns focus; Tab closes.
 * `open` is two-way via `sl-open-changed`; `sl-select` bubbles from the chosen item.
 */
export class SlMenu extends LitElement {
  static override properties = {
    placement: { reflect: true },
    open: { type: Boolean, reflect: true },
    contextMenu: { type: Boolean, attribute: 'context-menu', reflect: true },
  };

  static override styles = [hostReset, styles.base, styles.overlay, css`:host { display: inline-block; }`];

  declare placement: PopoverPlacement;
  declare open: boolean;
  declare contextMenu: boolean;

  private readonly panelId = uid('sl-menu');
  private point: { x: number; y: number } | undefined;
  private pendingFocus: 'first' | 'last' | 'none' = 'first';
  private readonly keyboard = new MenuKeyboard(() => this.items);
  private readonly overlay = new Overlay({
    anchor: () => this.trigger,
    panel: () => this.renderRoot?.querySelector<HTMLElement>('.sl-menu__panel') ?? null,
    placement: () => this.placement,
    onDismiss: (reason) => this.close(reason === 'escape'),
  });

  constructor() {
    super();
    this.placement = 'bottom-start';
    this.open = false;
    this.contextMenu = false;
    this.addEventListener('sl-select', this.onSelect as EventListener);
    // Host listeners so slotted triggers/items are handled in every DOM implementation.
    this.addEventListener('click', (e) => {
      if (this.fromTrigger(e)) this.onTriggerClick(e);
    });
    this.addEventListener('contextmenu', (e) => {
      if (this.fromTrigger(e)) this.onContextMenu(e);
    });
    this.addEventListener('keydown', (e) => {
      if (this.fromTrigger(e)) this.onTriggerKeyDown(e);
      else if (this.open) this.onPanelKeyDown(e);
    });
  }

  private fromTrigger(e: Event): boolean {
    const trigger = this.trigger;
    return trigger !== this && e.composedPath().includes(trigger);
  }

  /** Top-level items (default slot or slot="items"). */
  get items(): SlMenuItem[] {
    return [...this.querySelectorAll<SlMenuItem>(':scope > sl-menu-item')].filter((i) => !i.slot || i.slot === 'items');
  }

  get trigger(): HTMLElement {
    const slot = this.renderRoot?.querySelector<HTMLSlotElement>('slot[name="trigger"]');
    return (slot?.assignedElements()[0] as HTMLElement | undefined) ?? this;
  }

  /** Opens the menu; `at` opens it at a viewport point (context menus). */
  show(focus: 'first' | 'last' | 'none' = 'first', at?: { x: number; y: number }): void {
    this.pendingFocus = focus;
    this.point = at;
    if (this.open) {
      this.overlay.open(this.point);
      this.focusPending();
      return;
    }
    this.open = true;
    this.dispatchEvent(new CustomEvent('sl-open-changed', { bubbles: true, composed: true, detail: { open: true } }));
  }

  /** Closes the menu (and every submenu); optionally returns focus to the trigger. */
  close(returnFocus = true): void {
    for (const item of this.querySelectorAll<SlMenuItem>('sl-menu-item')) item.closeSubmenu(false);
    if (!this.open) return;
    this.open = false;
    this.dispatchEvent(new CustomEvent('sl-open-changed', { bubbles: true, composed: true, detail: { open: false } }));
    if (returnFocus && this.trigger !== this) this.trigger.focus();
  }

  override disconnectedCallback(): void {
    super.disconnectedCallback();
    this.overlay.close();
  }

  protected override updated(changed: PropertyValues<this>): void {
    if (changed.has('open')) {
      if (this.open) {
        this.overlay.open(this.point);
        this.focusPending();
      } else {
        this.overlay.close();
        this.point = undefined;
      }
      this.syncTrigger();
    } else if (this.open && changed.has('placement')) {
      this.overlay.position();
    }
  }

  private focusPending(): void {
    const which = this.pendingFocus;
    this.pendingFocus = 'first';
    if (which !== 'none') requestAnimationFrame(() => this.keyboard.focusAt(which));
  }

  private syncTrigger(): void {
    const trigger = this.trigger;
    if (trigger === this || this.contextMenu) return;
    trigger.setAttribute('aria-haspopup', 'menu');
    trigger.setAttribute('aria-expanded', String(this.open));
  }

  private readonly onSelect = (e: CustomEvent<{ item: SlMenuItem }>): void => {
    if (e.detail.item.hasSubmenu) return;
    this.close(true);
  };

  private onTriggerClick(e: MouseEvent): void {
    if (this.contextMenu) return;
    if ((e.target as HTMLElement | null)?.closest?.('[disabled]')) return;
    if (this.open) this.close(false);
    else this.show(e.detail === 0 ? 'first' : 'none');
  }

  private onTriggerKeyDown(e: KeyboardEvent): void {
    if (this.contextMenu) {
      if (e.key === 'ContextMenu' || (e.key === 'F10' && e.shiftKey)) {
        e.preventDefault();
        const r = (e.target as HTMLElement).getBoundingClientRect();
        this.show('first', { x: r.left + r.width / 2, y: r.top + r.height / 2 });
      }
      return;
    }
    if (e.key === 'ArrowDown' || e.key === 'Enter' || e.key === ' ') {
      e.preventDefault();
      this.show('first');
    } else if (e.key === 'ArrowUp') {
      e.preventDefault();
      this.show('last');
    }
  }

  private onContextMenu(e: MouseEvent): void {
    if (!this.contextMenu) return;
    e.preventDefault();
    this.show('first', { x: e.clientX, y: e.clientY });
  }

  private onPanelKeyDown(e: KeyboardEvent): void {
    if (e.key === 'Tab') {
      this.close(false);
      return;
    }
    if (this.keyboard.handle(e)) e.preventDefault();
  }

  override render() {
    return html`<span class="sl-menu__trigger"
        ><slot name="trigger" @slotchange=${this.syncTrigger}></slot></span
      ><div id=${this.panelId} class="sl-popover-panel sl-menu__panel" role="menu" part="panel" data-placement=${this.placement} hidden>
        <slot name="items"></slot><slot></slot>
      </div>`;
  }
}
