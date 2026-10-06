import { LitElement, css, html, nothing } from 'lit';
import { classMap } from 'lit/directives/class-map.js';
import { ifDefined } from 'lit/directives/if-defined.js';
import { styleMap } from 'lit/directives/style-map.js';
import { html as staticHtml, unsafeStatic } from 'lit/static-html.js';
import { hostReset, styles } from '../internal/styles';
import { isIconName, renderIcon } from '../internal/icon';
import { icons, iconStrokeWidth, iconViewBox } from '../icons/generated/icons';
import type { Severity } from '../core/snackbar-queue';

/** <sl-icon name="check" size="sm|md|lg" label="…"> — decorative unless labelled. */
export class SlIcon extends LitElement {
  static override properties = {
    name: { reflect: true },
    size: { reflect: true },
    label: {},
  };
  static override styles = [
    hostReset,
    styles.typography,
    css`
      :host {
        display: inline-flex;
        line-height: 0;
        vertical-align: middle;
      }
    `,
  ];

  declare name: string;
  declare size: 'sm' | 'md' | 'lg';
  declare label: string | undefined;

  constructor() {
    super();
    this.name = '';
    this.size = 'md';
  }

  override render() {
    if (!isIconName(this.name)) return nothing;
    const labelled = !!this.label;
    return html`<svg
      class="sl-icon sl-icon--${this.size}"
      part="svg"
      viewBox="0 0 ${iconViewBox} ${iconViewBox}"
      fill="none"
      stroke="currentColor"
      stroke-width=${iconStrokeWidth}
      stroke-linecap="round"
      stroke-linejoin="round"
      role=${labelled ? 'img' : 'presentation'}
      aria-hidden=${labelled ? 'false' : 'true'}
      aria-label=${ifDefined(this.label || undefined)}
      focusable="false"
    >
      <path d=${icons[this.name]}></path>
    </svg>`;
  }
}

/** <sl-kbd>⌘K</sl-kbd> */
export class SlKbd extends LitElement {
  static override styles = [hostReset, styles.feedback, css`:host { display: inline-block; }`];

  override render() {
    return html`<kbd class="sl-kbd" part="base"><slot></slot></kbd>`;
  }
}

export type TextVariant =
  | 'display' | 'h1' | 'h2' | 'h3' | 'title' | 'body' | 'body-strong' | 'label' | 'caption' | 'overline' | 'mono';
export type TextTone = 'primary' | 'secondary' | 'tertiary' | 'accent' | 'success' | 'warning' | 'danger' | 'info';

const textTags = new Set(['p', 'span', 'div', 'h1', 'h2', 'h3', 'h4', 'h5', 'h6', 'label', 'strong', 'em', 'code']);

/** <sl-text variant="h2" tone="secondary" as="h2"> — typography styles from the tokens. */
export class SlText extends LitElement {
  static override properties = {
    variant: { reflect: true },
    tone: { reflect: true },
    as: {},
    truncate: { type: Boolean, reflect: true },
  };
  static override styles = [hostReset, styles.typography, css`:host { display: block; } :host([as="span"]) { display: inline; }`];

  declare variant: TextVariant;
  declare tone: TextTone | undefined;
  declare as: string;
  declare truncate: boolean;

  constructor() {
    super();
    this.variant = 'body';
    this.as = 'p';
    this.truncate = false;
  }

  override render() {
    const tag = unsafeStatic(textTags.has(this.as) ? this.as : 'p');
    const classes = {
      'sl-text': true,
      [`sl-text--${this.variant}`]: true,
      [`sl-text--${this.tone}`]: !!this.tone,
      'sl-text--truncate': this.truncate,
    };
    return staticHtml`<${tag} class=${classMap(classes)} part="base"><slot></slot></${tag}>`;
  }
}

export type BadgeTone = 'neutral' | 'info' | 'success' | 'warning' | 'danger' | 'accent';

/** <sl-badge tone="success" dot>Ready</sl-badge> */
export class SlBadge extends LitElement {
  static override properties = {
    tone: { reflect: true },
    dot: { type: Boolean, reflect: true },
  };
  static override styles = [hostReset, styles.feedback, css`:host { display: inline-flex; vertical-align: middle; }`];

  declare tone: BadgeTone;
  declare dot: boolean;

  constructor() {
    super();
    this.tone = 'neutral';
    this.dot = false;
  }

  override render() {
    return html`<span class="sl-badge ${this.tone === 'neutral' ? '' : `sl-badge--${this.tone}`}" part="base"
      >${this.dot ? html`<span class="sl-badge__dot" aria-hidden="true"></span>` : nothing}<slot></slot
    ></span>`;
  }
}

/** <sl-spinner size="sm" label="Loading"> */
export class SlSpinner extends LitElement {
  static override properties = { size: { reflect: true }, label: {} };
  static override styles = [hostReset, styles.base, styles.feedback, css`:host { display: inline-flex; vertical-align: middle; }`];

  declare size: 'sm' | 'md' | 'lg';
  declare label: string;

  constructor() {
    super();
    this.size = 'md';
    this.label = 'Loading';
  }

  override render() {
    return html`<span class="sl-spinner sl-spinner--${this.size}" part="base" role="status" aria-label=${this.label}></span>`;
  }
}

/** <sl-progress value="40" label="Uploading"> — omit value for indeterminate. */
export class SlProgress extends LitElement {
  static override properties = {
    value: { type: Number },
    max: { type: Number },
    label: {},
  };
  static override styles = [hostReset, styles.base, styles.feedback, css`:host { display: block; }`];

  declare value: number | undefined;
  declare max: number;
  declare label: string | undefined;

  constructor() {
    super();
    this.max = 100;
  }

  get indeterminate(): boolean {
    return this.value === undefined || this.value === null || Number.isNaN(this.value);
  }

  override render() {
    const indeterminate = this.indeterminate;
    const clamped = indeterminate ? 0 : Math.min(this.max, Math.max(0, this.value!));
    const percent = this.max > 0 ? (clamped / this.max) * 100 : 0;
    return html`<div
      class="sl-progress ${indeterminate ? 'sl-progress--indeterminate' : ''}"
      part="base"
      role="progressbar"
      aria-label=${ifDefined(this.label)}
      aria-valuemin="0"
      aria-valuemax=${this.max}
      aria-valuenow=${ifDefined(indeterminate ? undefined : clamped)}
      aria-busy=${indeterminate ? 'true' : 'false'}
      style=${styleMap({ '--_value': `${percent}%` })}
    >
      <div class="sl-progress__bar" part="bar"></div>
    </div>`;
  }
}

const severityIcons: Record<Severity, string> = {
  normal: 'info',
  info: 'info',
  success: 'check-circle',
  warning: 'alert-triangle',
  error: 'alert-circle',
};

export function severityIcon(severity: Severity): string {
  return severityIcons[severity] ?? 'info';
}

/**
 * <sl-alert severity="warning" heading="Usage at 80%" dismissible>Message<sl-button slot="actions">…</sl-button></sl-alert>
 * Errors announce assertively (role=alert); others only when `live` is set (they appeared dynamically).
 */
export class SlAlert extends LitElement {
  static override properties = {
    severity: { reflect: true },
    heading: {},
    dismissible: { type: Boolean, reflect: true },
    live: { type: Boolean },
    hasActions: { state: true },
  };
  static override styles = [hostReset, styles.typography, styles.button, styles.feedback, css`:host { display: block; }`];

  declare severity: Severity;
  declare heading: string | undefined;
  declare dismissible: boolean;
  declare live: boolean;
  declare hasActions: boolean;

  constructor() {
    super();
    this.severity = 'info';
    this.dismissible = false;
    this.live = false;
    this.hasActions = false;
  }

  override connectedCallback(): void {
    super.connectedCallback();
    this.hasActions = !!this.querySelector(':scope > [slot="actions"]');
  }

  /** Hides the alert. Fires a cancelable `sl-dismiss` first. */
  dismiss(): void {
    const proceed = this.dispatchEvent(new CustomEvent('sl-dismiss', { bubbles: true, composed: true, cancelable: true }));
    if (proceed) this.hidden = true;
  }

  override render() {
    const role = this.severity === 'error' ? 'alert' : this.live ? 'status' : undefined;
    return html`<div class="sl-alert ${this.severity === 'normal' ? '' : `sl-alert--${this.severity}`}" part="base" role=${ifDefined(role)}>
      ${renderIcon(severityIcon(this.severity), 'sl-alert__icon')}
      <div class="sl-alert__content">
        ${this.heading ? html`<strong class="sl-alert__title">${this.heading}</strong>` : nothing}
        <div class="sl-alert__message"><slot></slot></div>
      </div>
      <div class="sl-alert__actions" ?hidden=${!this.hasActions && !this.dismissible}>
        <slot name="actions" @slotchange=${this.onActionsChange}></slot>
        ${this.dismissible
          ? html`<button
              class="sl-button sl-button--ghost sl-button--sm sl-button--icon sl-alert__close"
              type="button"
              aria-label="Dismiss"
              @click=${this.dismiss}
            >${renderIcon('x', 'sl-button__icon')}</button>`
          : nothing}
      </div>
    </div>`;
  }

  private onActionsChange(e: Event): void {
    this.hasActions = (e.target as HTMLSlotElement).assignedElements().length > 0;
  }
}
