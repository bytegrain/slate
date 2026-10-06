import { LitElement, css, html, nothing } from 'lit';
import { classMap } from 'lit/directives/class-map.js';
import { repeat } from 'lit/directives/repeat.js';
import { styleMap } from 'lit/directives/style-map.js';
import { hostReset, styles } from '../internal/styles';
import { renderIcon } from '../internal/icon';
import { prefersReducedMotion } from '../internal/dom';
import { tokenNumber } from '../core/tokens';
import type { Snackbar, SnackbarPosition, SnackbarQueue } from '../core/snackbar-queue';
import { snackbar as defaultService, type SnackbarService } from '../services/snackbar';

interface Leaving {
  snackbar: Snackbar;
}

/**
 * Renders a snackbar service's queue. <sl-provider> includes one; add your own to change position:
 *   <sl-snackbar-host position="top-center"></sl-snackbar-host>
 * Hover or focus pauses a snackbar's countdown; Escape dismisses the focused one.
 */
export class SlSnackbarHost extends LitElement {
  static override properties = {
    position: { reflect: true },
    label: {},
    service: { attribute: false },
    items: { state: true },
    leaving: { state: true },
  };
  static override styles = [hostReset, styles.base, styles.typography, styles.snackbar, css`:host { display: contents; }`];

  /** Defaults to the service configuration's position. */
  declare position: SnackbarPosition | undefined;
  declare label: string;
  declare service: SnackbarService;
  declare items: readonly Snackbar[];
  declare leaving: readonly Leaving[];

  private unsubscribers: Array<() => void> = [];

  constructor() {
    super();
    this.label = 'Notifications';
    this.service = defaultService;
    this.items = [];
    this.leaving = [];
  }

  get resolved(): { position: SnackbarPosition } {
    return { position: this.position ?? this.service.configuration.position };
  }

  private get queue(): SnackbarQueue {
    return this.service.queue;
  }

  override connectedCallback(): void {
    super.connectedCallback();
    this.subscribe();
  }

  override disconnectedCallback(): void {
    super.disconnectedCallback();
    this.unsubscribe();
  }

  override willUpdate(changed: Map<string, unknown>): void {
    if (changed.has('service') && this.isConnected && changed.get('service') !== undefined) {
      this.unsubscribe();
      this.subscribe();
    }
  }

  private subscribe(): void {
    const bind = () => {
      this.items = this.queue.visible;
      return [
        this.queue.onChanged(() => (this.items = this.queue.visible)),
        this.queue.onClosed(({ snackbar }) => this.animateOut(snackbar)),
      ];
    };
    let queueSubs = bind();
    this.unsubscribers = [
      this.service.registerHost(this),
      this.service.onQueueReplaced(() => {
        queueSubs.forEach((u) => u());
        queueSubs = bind();
      }),
      () => queueSubs.forEach((u) => u()),
    ];
  }

  private unsubscribe(): void {
    this.unsubscribers.forEach((u) => u());
    this.unsubscribers = [];
  }

  private animateOut(s: Snackbar): void {
    if (prefersReducedMotion() || !this.items.some((i) => i.id === s.id)) return;
    const entry = { snackbar: s };
    this.leaving = [...this.leaving, entry];
    setTimeout(() => (this.leaving = this.leaving.filter((l) => l !== entry)), tokenNumber('--sl-motion-duration-fast'));
  }

  override render() {
    const position = this.position ?? this.service.configuration.position;
    const visibleIds = new Set(this.items.map((s) => s.id));
    const rows = [
      ...this.items.map((snackbar) => ({ snackbar, leaving: false })),
      ...this.leaving.filter((l) => !visibleIds.has(l.snackbar.id)).map((l) => ({ snackbar: l.snackbar, leaving: true })),
    ];
    return html`<section class="sl-snackbar-host sl-snackbar-host--${position}" part="base" aria-label=${this.label}>
      ${repeat(rows, (r) => r.snackbar.id, (r) => this.renderSnackbar(r.snackbar, r.leaving))}
    </section>`;
  }

  private renderSnackbar(s: Snackbar, leaving: boolean) {
    const o = s.options;
    const assertive = s.severity === 'error' || s.severity === 'warning';
    const classes = {
      'sl-snackbar': true,
      [`sl-snackbar--${s.severity}`]: s.severity !== 'normal',
      'is-paused': s.isPaused,
      'is-leaving': leaving,
    };
    return html`<div
      class=${classMap(classes)}
      part="snackbar"
      role=${assertive ? 'alert' : 'status'}
      aria-live=${assertive ? 'assertive' : 'polite'}
      data-id=${s.id}
      style=${styleMap({ '--_duration': s.duration === null ? null : `${s.duration}ms` })}
      @mouseenter=${() => this.queue.pause(s)}
      @mouseleave=${() => this.queue.resume(s)}
      @focusin=${() => this.queue.pause(s)}
      @focusout=${(e: FocusEvent) => this.onFocusOut(e, s)}
      @keydown=${(e: KeyboardEvent) => this.onKeyDown(e, s)}
    >
      ${s.severity === 'normal'
        ? nothing
        : html`<span class="sl-snackbar__icon" aria-hidden="true">${renderIcon(iconGlyph(s.severity))}</span>`}
      <div class="sl-snackbar__content">
        ${o.title ? html`<strong class="sl-snackbar__title">${o.title}</strong>` : nothing}
        <p class="sl-snackbar__message">${o.message}</p>
      </div>
      ${o.action || o.showCloseButton !== false
        ? html`<div class="sl-snackbar__actions">
            ${o.action
              ? html`<button class="sl-snackbar__action" type="button" @click=${() => this.queue.invokeAction(s)}>${o.action.label}</button>`
              : nothing}
            ${o.showCloseButton !== false
              ? html`<button class="sl-snackbar__close" type="button" aria-label="Dismiss" @click=${() => this.queue.dismiss(s, 'user')}>
                  ${renderIcon('x')}
                </button>`
              : nothing}
          </div>`
        : nothing}
      ${s.duration !== null && !leaving ? html`<span class="sl-snackbar__timer" aria-hidden="true"></span>` : nothing}
    </div>`;
  }

  private onFocusOut(e: FocusEvent, s: Snackbar): void {
    const target = e.currentTarget as HTMLElement;
    if (target.contains(e.relatedTarget as Node | null)) return;
    let hovered = false;
    try {
      hovered = target.matches(':hover');
    } catch {
      /* :hover unsupported (non-browser DOM) */
    }
    if (!hovered) this.queue.resume(s);
  }

  private onKeyDown(e: KeyboardEvent, s: Snackbar): void {
    if (e.key === 'Escape') {
      e.stopPropagation();
      this.queue.dismiss(s, 'user');
    }
  }
}

/** Compact glyphs for the 18px severity tile (the circled icons are too busy at that size). */
function iconGlyph(severity: Snackbar['severity']): string {
  switch (severity) {
    case 'success':
      return 'check';
    case 'error':
      return 'x';
    case 'warning':
      return 'alert-triangle';
    default:
      return 'info';
  }
}
