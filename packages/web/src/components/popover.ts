import { LitElement, css, html, nothing, type PropertyValues } from 'lit';
import { hostReset, styles } from '../internal/styles';
import { defaultTrue, uid } from '../internal/dom';
import { Overlay } from '../internal/overlay';
import type { PopoverPlacement } from '../core/overlay/positioning';

export type { PopoverPlacement };

const focusable =
  'button:not([disabled]), [href], input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"]), sl-button:not([disabled]), sl-text-field:not([disabled]), sl-select:not([disabled]), sl-checkbox:not([disabled]), sl-switch:not([disabled])';

/**
 * <sl-popover placement="bottom-start" match-anchor-width modal>
 *   <sl-button slot="anchor">Filters</sl-button>
 *   …content…
 * </sl-popover>
 *
 * Clicking the anchor toggles the popover. `open` is two-way: it fires `sl-open-changed` (detail: { open }) when the
 * user opens or dismisses it. Escape and outside clicks close it (outside clicks only with close-on-outside-click,
 * the default). Modal popovers move focus inside and trap Tab; focus returns to the anchor on close.
 */
export class SlPopover extends LitElement {
  static override properties = {
    open: { type: Boolean, reflect: true },
    placement: { reflect: true },
    offset: { type: Number },
    modal: { type: Boolean, reflect: true },
    closeOnOutsideClick: { attribute: 'close-on-outside-click', converter: defaultTrue },
    matchAnchorWidth: { type: Boolean, attribute: 'match-anchor-width', reflect: true },
  };

  static override styles = [hostReset, styles.base, styles.overlay, css`:host { display: inline-block; }`];

  declare open: boolean;
  declare placement: PopoverPlacement;
  declare offset: number;
  declare modal: boolean;
  declare closeOnOutsideClick: boolean;
  declare matchAnchorWidth: boolean;

  private readonly panelId = uid('sl-popover');
  private returnFocus: HTMLElement | null = null;

  private readonly overlay = new Overlay({
    anchor: () => this.anchor,
    panel: () => this.panel,
    placement: () => this.placement,
    offset: () => this.offset,
    matchWidth: () => this.matchAnchorWidth,
    dismissOnOutside: () => this.closeOnOutsideClick,
    onDismiss: (reason) => this.setOpen(false, reason === 'escape'),
  });

  constructor() {
    super();
    this.open = false;
    this.placement = 'bottom';
    this.offset = 6;
    this.modal = false;
    this.closeOnOutsideClick = true;
    this.matchAnchorWidth = false;
    // Host listeners (not on shadow wrappers) so slotted content is handled in every DOM implementation.
    this.addEventListener('click', (e) => {
      const anchor = this.anchor;
      if (anchor !== this && e.composedPath().includes(anchor)) this.onAnchorClick(e);
    });
    this.addEventListener('keydown', (e) => this.onPanelKeyDown(e));
  }

  /** The slotted anchor element (falls back to the host). */
  get anchor(): HTMLElement {
    const slot = this.renderRoot?.querySelector<HTMLSlotElement>('slot[name="anchor"]');
    return (slot?.assignedElements()[0] as HTMLElement | undefined) ?? this;
  }

  get panel(): HTMLElement | null {
    return this.renderRoot?.querySelector<HTMLElement>('.sl-popover__panel') ?? null;
  }

  show(): void {
    this.setOpen(true, false);
  }

  hide(): void {
    this.setOpen(false, true);
  }

  toggle(): void {
    this.setOpen(!this.open, true);
  }

  /** Recompute the position (e.g. after the content changed size). */
  reposition(): void {
    this.overlay.position();
  }

  override disconnectedCallback(): void {
    super.disconnectedCallback();
    this.overlay.close();
  }

  protected override updated(changed: PropertyValues<this>): void {
    if (changed.has('open')) {
      if (this.open) {
        this.returnFocus = this.anchor;
        this.overlay.open();
        if (this.modal) requestAnimationFrame(() => this.focusInside());
      } else {
        this.overlay.close();
      }
      this.syncAnchor();
    } else if (this.open && (changed.has('placement') || changed.has('offset'))) {
      this.overlay.position();
    }
  }

  private setOpen(open: boolean, restoreFocus: boolean): void {
    if (open === this.open) return;
    const hadFocus = this.matches(':focus-within');
    this.open = open;
    this.dispatchEvent(new CustomEvent('sl-open-changed', { bubbles: true, composed: true, detail: { open } }));
    if (!open && restoreFocus && hadFocus) this.returnFocus?.focus();
  }

  private syncAnchor(): void {
    const anchor = this.anchor;
    if (anchor === this) return;
    anchor.setAttribute('aria-haspopup', 'dialog');
    anchor.setAttribute('aria-expanded', String(this.open));
  }

  private contentFocusables(): HTMLElement[] {
    const anchor = this.anchor;
    return [...this.querySelectorAll<HTMLElement>(focusable)].filter((el) => el.slot !== 'anchor' && !anchor.contains(el));
  }

  private focusInside(): void {
    const first = this.contentFocusables()[0];
    (first ?? this.panel)?.focus();
  }

  private onAnchorClick(e: Event): void {
    if ((e.target as HTMLElement | null)?.closest?.('[disabled]')) return;
    this.setOpen(!this.open, true);
  }

  private onPanelKeyDown(e: KeyboardEvent): void {
    if (!this.modal || !this.open || e.key !== 'Tab') return;
    const items = this.contentFocusables();
    if (items.length === 0) {
      e.preventDefault();
      return;
    }
    const active = document.activeElement as HTMLElement | null;
    const index = active ? items.findIndex((el) => el === active || el.contains(active)) : -1;
    if (e.shiftKey && index <= 0) {
      e.preventDefault();
      items[items.length - 1].focus();
    } else if (!e.shiftKey && index === items.length - 1) {
      e.preventDefault();
      items[0].focus();
    }
  }

  override render() {
    return html`<span class="sl-popover__anchor"><slot name="anchor" @slotchange=${this.syncAnchor}></slot></span
      ><div
        id=${this.panelId}
        class="sl-popover-panel sl-popover__panel"
        part="panel"
        role="dialog"
        aria-modal=${this.modal ? 'true' : nothing}
        data-placement=${this.placement}
        tabindex="-1"
        hidden
      >
        <span class="sl-popover__arrow" part="arrow" aria-hidden="true"></span>
        <slot></slot>
      </div>`;
  }
}

/** Shared "warm-up": once a tooltip has been shown, moving to another within this window shows it immediately. */
let lastTooltipHidden = Number.NEGATIVE_INFINITY;
const warmWindowMs = 400;

/**
 * <sl-tooltip text="Copy link" shortcut="⌘C" placement="top" delay="500">
 *   <sl-button icon-only start-icon="copy" label="Copy link"></sl-button>
 * </sl-tooltip>
 *
 * Shows on hover and keyboard focus after `delay` ms (0 when moving between tooltips), hides on leave, blur,
 * pointer-down and Escape (WCAG 1.4.13). Rich content goes in `slot="content"`. The text is also set as the
 * anchor's `aria-description`, because ARIA references can't cross shadow roots.
 */
export class SlTooltip extends LitElement {
  static override properties = {
    text: {},
    shortcut: {},
    placement: { reflect: true },
    delay: { type: Number },
    disabled: { type: Boolean, reflect: true },
    visible: { state: true },
  };

  static override styles = [hostReset, styles.base, styles.feedback, styles.overlay, css`:host { display: inline-block; }`];

  declare text: string | undefined;
  declare shortcut: string | undefined;
  declare placement: PopoverPlacement;
  declare delay: number;
  declare disabled: boolean;
  declare visible: boolean;

  private timer: ReturnType<typeof setTimeout> | undefined;
  private readonly bubbleId = uid('sl-tooltip');

  private readonly overlay = new Overlay({
    anchor: () => this.anchor,
    panel: () => this.renderRoot?.querySelector<HTMLElement>('.sl-tooltip') ?? null,
    placement: () => this.placement,
    offset: () => 6,
    onDismiss: () => this.hide(),
  });

  constructor() {
    super();
    this.placement = 'top';
    this.delay = 500;
    this.disabled = false;
    this.visible = false;
    this.addEventListener('pointerenter', () => this.scheduleShow());
    this.addEventListener('pointerleave', () => this.hide());
    this.addEventListener('focusin', () => this.scheduleShow());
    this.addEventListener('focusout', () => this.hide());
    this.addEventListener('pointerdown', () => this.hide());
  }

  get anchor(): HTMLElement {
    const slot = this.renderRoot?.querySelector<HTMLSlotElement>('slot:not([name])');
    return (slot?.assignedElements()[0] as HTMLElement | undefined) ?? this;
  }

  override disconnectedCallback(): void {
    super.disconnectedCallback();
    clearTimeout(this.timer);
    this.overlay.close();
  }

  /** Shows immediately (ignores the delay). */
  show(): void {
    if (this.disabled || !this.hasContent()) return;
    clearTimeout(this.timer);
    this.visible = true;
  }

  hide(): void {
    clearTimeout(this.timer);
    if (this.visible) lastTooltipHidden = Date.now();
    this.visible = false;
  }

  protected override updated(changed: PropertyValues<this>): void {
    if (changed.has('visible')) {
      if (this.visible) this.overlay.open();
      else this.overlay.close();
    }
    if (changed.has('text')) this.describeAnchor();
    if (changed.has('disabled') && this.disabled) this.hide();
  }

  private hasContent(): boolean {
    return !!this.text || !!this.querySelector(':scope > [slot="content"]');
  }

  private scheduleShow(): void {
    if (this.disabled || !this.hasContent() || this.visible) return;
    clearTimeout(this.timer);
    const warm = Date.now() - lastTooltipHidden < warmWindowMs;
    const wait = warm ? 0 : Math.max(0, this.delay);
    if (wait === 0) this.show();
    else this.timer = setTimeout(() => this.show(), wait);
  }

  private describeAnchor(): void {
    const anchor = this.anchor;
    if (anchor === this) return;
    if (this.text) anchor.setAttribute('aria-description', this.shortcut ? `${this.text} (${this.shortcut})` : this.text);
    else anchor.removeAttribute('aria-description');
  }

  override render() {
    return html`<span class="sl-tooltip__anchor"><slot @slotchange=${this.describeAnchor}></slot></span
      ><div id=${this.bubbleId} class="sl-tooltip" part="base" role="tooltip" data-placement=${this.placement} hidden>
        ${this.text ?? nothing}<slot name="content"></slot>${this.shortcut ? html`<kbd class="sl-kbd">${this.shortcut}</kbd>` : nothing}
      </div>`;
  }
}
