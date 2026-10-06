import { LitElement, css, html, nothing } from 'lit';
import { classMap } from 'lit/directives/class-map.js';
import { ifDefined } from 'lit/directives/if-defined.js';
import { hostReset, styles } from '../internal/styles';
import { renderIcon } from '../internal/icon';
import { deepActiveElement, defaultTrue, firstFocusable, prefersReducedMotion, uid } from '../internal/dom';
import { tokenNumber } from '../core/tokens';
import {
  DialogResult,
  dialogStack,
  type DialogPlacement,
  type DialogReference,
  type DialogStack,
  type DialogWidth,
} from '../core/dialog-stack';

export type DialogTone = 'accent' | 'danger' | 'warning' | 'success' | 'info';

/**
 * <sl-dialog heading="Rename" description="…" width="xs" icon="pencil" open>
 *   body…
 *   <sl-button slot="footer" variant="primary">Save</sl-button>
 * </sl-dialog>
 *
 * Modal (native <dialog> + showModal: background is inert, focus stays inside). Only the top dialog of
 * the page-wide DialogStack reacts to Escape and scrim clicks. `show()` resolves with { canceled, data }.
 * Fires `sl-close` (detail: DialogResult) after closing.
 */
export class SlDialog extends LitElement {
  static override properties = {
    open: { type: Boolean, reflect: true },
    heading: {},
    description: {},
    width: { reflect: true },
    placement: { reflect: true },
    fullWidth: { type: Boolean, attribute: 'full-width', reflect: true },
    fullscreen: { type: Boolean, reflect: true },
    closeOnEscape: { attribute: 'close-on-escape', converter: defaultTrue },
    closeOnBackdropClick: { attribute: 'close-on-backdrop-click', converter: defaultTrue },
    showCloseButton: { attribute: 'show-close-button', converter: defaultTrue },
    icon: {},
    tone: { reflect: true },
    hasFooter: { state: true },
    closing: { state: true },
  };

  static override styles = [
    hostReset,
    styles.base,
    styles.typography,
    styles.button,
    styles.dialog,
    css`
      :host { display: contents; }
    `,
  ];

  declare open: boolean;
  declare heading: string;
  declare description: string | undefined;
  declare width: DialogWidth;
  declare placement: DialogPlacement;
  declare fullWidth: boolean;
  declare fullscreen: boolean;
  declare closeOnEscape: boolean;
  declare closeOnBackdropClick: boolean;
  declare showCloseButton: boolean;
  declare icon: string | undefined;
  declare tone: DialogTone;
  declare hasFooter: boolean;
  declare closing: boolean;

  /** The stack this dialog registers with. Defaults to the page-wide stack. */
  stack: DialogStack = dialogStack;

  private reference: DialogReference | null = null;
  private returnFocusTo: Element | null = null;
  private finished: Promise<DialogResult> | null = null;
  private readonly titleId = uid('sl-dialog-title');
  private readonly descId = uid('sl-dialog-desc');

  constructor() {
    super();
    this.open = false;
    this.heading = '';
    this.width = 'sm';
    this.placement = 'center';
    this.fullWidth = false;
    this.fullscreen = false;
    this.closeOnEscape = true;
    this.closeOnBackdropClick = true;
    this.showCloseButton = true;
    this.tone = 'accent';
    this.hasFooter = false;
    this.closing = false;
  }

  /** The live stack entry while open. */
  get dialogReference(): DialogReference | null {
    return this.reference;
  }

  override connectedCallback(): void {
    super.connectedCallback();
    this.hasFooter = !!this.querySelector(':scope > [slot="footer"]');
  }

  override disconnectedCallback(): void {
    super.disconnectedCallback();
    if (this.reference?.isOpen) this.stack.close(this.reference, DialogResult.cancel());
  }

  /** Opens the dialog. Resolves once it has closed (after the exit animation). */
  show(): Promise<DialogResult> {
    if (this.finished && this.reference?.isOpen) return this.finished;

    this.returnFocusTo = deepActiveElement();
    const reference = this.stack.push(this, {
      title: this.heading,
      maxWidth: this.width,
      fullWidth: this.fullWidth,
      fullScreen: this.fullscreen,
      placement: this.placement,
      closeOnEscape: this.closeOnEscape,
      closeOnBackdropClick: this.closeOnBackdropClick,
      showCloseButton: this.showCloseButton,
    });
    this.reference = reference;
    this.closing = false;
    this.open = true;

    this.finished = reference.result.then((result) => this.finish(reference, result));
    void this.updateComplete.then(() => this.present());
    return this.finished;
  }

  /** Closes with a result (default: cancel). */
  close(result: DialogResult = DialogResult.cancel()): void {
    if (this.reference?.isOpen) this.stack.close(this.reference, result);
  }

  /** Closes with Ok(data). */
  accept(data?: unknown): void {
    this.close(DialogResult.ok(data));
  }

  override willUpdate(changed: Map<string, unknown>): void {
    // Declarative control: toggling the `open` attribute opens/closes.
    if (changed.has('open')) {
      if (this.open && !this.reference?.isOpen) void this.show();
      else if (!this.open && this.reference?.isOpen) this.close();
    }
  }

  private get dialogElement(): HTMLDialogElement | null {
    return this.renderRoot.querySelector('dialog');
  }

  private present(): void {
    const el = this.dialogElement;
    if (!el || !this.reference?.isOpen) return;
    if (!el.open) {
      try {
        if (typeof el.showModal === 'function') el.showModal();
        else el.setAttribute('open', '');
      } catch {
        el.setAttribute('open', '');
      }
    }
    const target = firstFocusable(this) ?? this.renderRoot.querySelector<HTMLElement>('.sl-dialog__panel');
    target?.focus();
  }

  private async finish(reference: DialogReference, result: DialogResult): Promise<DialogResult> {
    if (this.reference !== reference) return result;
    const duration = prefersReducedMotion() ? 0 : tokenNumber('--sl-motion-duration-base');
    if (duration > 0 && this.isConnected) {
      this.closing = true;
      await new Promise((resolve) => setTimeout(resolve, duration));
    }
    this.closing = false;
    const el = this.dialogElement;
    if (el?.open) {
      if (typeof el.close === 'function') el.close();
      else el.removeAttribute('open');
    }
    this.reference = null;
    this.open = false;
    if (this.returnFocusTo instanceof HTMLElement && this.returnFocusTo.isConnected) this.returnFocusTo.focus();
    this.returnFocusTo = null;
    this.dispatchEvent(new CustomEvent<DialogResult>('sl-close', { bubbles: true, composed: true, detail: result }));
    return result;
  }

  private onKeyDown(e: KeyboardEvent): void {
    if (e.key !== 'Escape') return;
    e.preventDefault(); // we decide, not the browser
    e.stopPropagation();
    if (this.reference && this.stack.top === this.reference) this.stack.handleEscape();
  }

  /** Native close requests (e.g. Android back) arrive as `cancel`. */
  private onCancel(e: Event): void {
    e.preventDefault();
    if (this.reference?.isOpen && this.stack.top === this.reference) this.stack.handleEscape();
  }

  private onBackdrop(): void {
    if (this.reference) this.stack.handleBackdropClick(this.reference);
  }

  private onFooterChange(e: Event): void {
    this.hasFooter = (e.target as HTMLSlotElement).assignedElements().length > 0;
  }

  override render() {
    const classes = {
      'sl-dialog': true,
      [`sl-dialog--${this.width}`]: true,
      'sl-dialog--top': this.placement === 'top',
      'sl-dialog--full-width': this.fullWidth,
      'sl-dialog--fullscreen': this.fullscreen,
      'is-closing': this.closing,
    };
    return html`<dialog
      class=${classMap(classes)}
      part="base"
      aria-labelledby=${this.titleId}
      aria-describedby=${ifDefined(this.description ? this.descId : undefined)}
      @keydown=${this.onKeyDown}
      @cancel=${this.onCancel}
    >
      <div class="sl-dialog__scrim" part="scrim" @click=${this.onBackdrop}></div>
      <div class="sl-dialog__panel" part="panel" tabindex="-1">
        <header class="sl-dialog__header" part="header">
          ${this.icon
            ? html`<div class="sl-dialog__icon ${this.tone === 'accent' ? '' : `sl-dialog__icon--${this.tone}`}" aria-hidden="true">
                ${renderIcon(this.icon, 'sl-icon--lg')}
              </div>`
            : nothing}
          <div class="sl-dialog__titles">
            <h2 class="sl-dialog__title" id=${this.titleId}>${this.heading}</h2>
            ${this.description ? html`<p class="sl-dialog__description" id=${this.descId}>${this.description}</p>` : nothing}
          </div>
          ${this.showCloseButton
            ? html`<button
                class="sl-button sl-button--ghost sl-button--sm sl-button--icon sl-dialog__close"
                part="close-button"
                type="button"
                aria-label="Close"
                @click=${() => this.close()}
              >${renderIcon('x', 'sl-button__icon')}</button>`
            : nothing}
        </header>
        <div class="sl-dialog__body" part="body"><slot></slot></div>
        <footer class="sl-dialog__footer" part="footer" ?hidden=${!this.hasFooter}>
          <slot name="footer" @slotchange=${this.onFooterChange}></slot>
        </footer>
      </div>
    </dialog>`;
  }
}
