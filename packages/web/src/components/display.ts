import { LitElement, css, html, nothing } from 'lit';
import { classMap } from 'lit/directives/class-map.js';
import { ifDefined } from 'lit/directives/if-defined.js';
import { styleMap } from 'lit/directives/style-map.js';
import { html as staticHtml, unsafeStatic } from 'lit/static-html.js';
import { hostReset, styles } from '../internal/styles';
import { isIconName, renderIcon } from '../internal/icon';
import { icons, iconStrokeWidth, iconViewBox } from '../icons/generated/icons';
import type { Severity } from '../core/snackbar-queue';
import type { AlertVariant, BadgeVariant, ControlSize, Tone } from '../core/defaults';
import { TitledElement } from '../internal/title';
import { iconButtonClasses } from './button';

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

export type BadgeTone = Tone;

/** <sl-badge tone="success" variant="soft|solid|outlined" size="small" dot icon="check">Ready</sl-badge> */
export class SlBadge extends LitElement {
  static override properties = {
    tone: { reflect: true },
    variant: { reflect: true },
    size: { reflect: true },
    dot: { type: Boolean, reflect: true },
    icon: {},
  };
  static override styles = [hostReset, styles.typography, styles.tone, styles.feedback, css`:host { display: inline-flex; vertical-align: middle; }`];

  declare tone: Tone;
  declare variant: BadgeVariant;
  declare size: 'small' | 'medium';
  declare dot: boolean;
  declare icon: string | undefined;

  constructor() {
    super();
    this.tone = 'neutral';
    this.variant = 'soft';
    this.size = 'medium';
    this.dot = false;
  }

  override render() {
    const classes = {
      'sl-badge': true,
      [`sl-badge--${this.variant}`]: true,
      [`sl-tone-${this.tone}`]: true,
      'sl-badge--small': this.size === 'small',
    };
    return html`<span class=${classMap(classes)} part="base"
      >${this.dot ? html`<span class="sl-badge__dot" part="dot" aria-hidden="true"></span>` : nothing}${renderIcon(this.icon, 'sl-badge__icon')}<slot></slot
    ></span>`;
  }
}

/** <sl-spinner size="small" tone="accent" label="Loading"> — neutral follows the text colour. */
export class SlSpinner extends LitElement {
  static override properties = { size: { reflect: true }, tone: { reflect: true }, label: {} };
  static override styles = [hostReset, styles.base, styles.tone, styles.feedback, css`:host { display: inline-flex; vertical-align: middle; }`];

  declare size: ControlSize;
  declare tone: Tone;
  declare label: string;

  constructor() {
    super();
    this.size = 'medium';
    this.tone = 'neutral';
    this.label = 'Loading';
  }

  override render() {
    const classes = {
      'sl-spinner': true,
      [`sl-spinner--${this.size}`]: this.size !== 'medium',
      [`sl-tone-${this.tone}`]: true,
    };
    return html`<span class=${classMap(classes)} part="base" role="status" aria-label=${this.label}></span>`;
  }
}

/**
 * <sl-progress value="40" label="Uploading" tone="success" size="small" show-value>
 * Indeterminate when `indeterminate` is set or no value is given.
 */
export class SlProgress extends LitElement {
  static override properties = {
    value: { type: Number },
    max: { type: Number },
    indeterminate: { type: Boolean, reflect: true },
    tone: { reflect: true },
    size: { reflect: true },
    label: {},
    showValue: { type: Boolean, attribute: 'show-value', reflect: true },
  };
  static override styles = [hostReset, styles.base, styles.tone, styles.feedback, css`:host { display: block; }`];

  declare value: number | undefined;
  declare max: number;
  declare indeterminate: boolean;
  declare tone: Tone;
  declare size: ControlSize;
  declare label: string | undefined;
  declare showValue: boolean;

  constructor() {
    super();
    this.max = 100;
    this.indeterminate = false;
    this.tone = 'accent';
    this.size = 'medium';
    this.showValue = false;
  }

  get isIndeterminate(): boolean {
    return this.indeterminate || this.value === undefined || this.value === null || Number.isNaN(this.value);
  }

  override render() {
    const indeterminate = this.isIndeterminate;
    const clamped = indeterminate ? 0 : Math.min(this.max, Math.max(0, this.value!));
    const percent = this.max > 0 ? (clamped / this.max) * 100 : 0;
    const classes = {
      'sl-progress': true,
      [`sl-tone-${this.tone}`]: true,
      [`sl-progress--${this.size}`]: this.size !== 'medium',
      'sl-progress--indeterminate': indeterminate,
    };
    const bar = html`<div
      class=${classMap(classes)}
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
    if (!this.showValue) return bar;
    return html`<div class="sl-progress-row">
      ${bar}<span class="sl-progress__value" part="value" aria-hidden="true">${indeterminate ? '' : `${Math.round(percent)}%`}</span>
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

const severityTones: Record<Severity, Tone> = {
  normal: 'neutral',
  info: 'info',
  success: 'success',
  warning: 'warning',
  error: 'danger',
};

/** Mirrors Slate.SeverityExtensions.ToTone. */
export function severityTone(severity: Severity): Tone {
  return severityTones[severity] ?? 'neutral';
}

/**
 * <sl-alert severity="warning" variant="soft|outlined|solid" title="Usage at 80%" dismissible dense>
 *   Message <sl-button slot="actions">…</sl-button>
 * </sl-alert>
 * `icon` overrides the severity icon ("none" hides it). Errors announce assertively (role=alert); others
 * only when `live` is set (they appeared dynamically). Fires a cancelable `sl-dismissed` before hiding.
 */
export class SlAlert extends TitledElement {
  static override properties = {
    severity: { reflect: true },
    variant: { reflect: true },
    title: {},
    icon: {},
    dense: { type: Boolean, reflect: true },
    dismissible: { type: Boolean, reflect: true },
    live: { type: Boolean },
    hasActions: { state: true },
  };
  static override styles = [hostReset, styles.typography, styles.tone, styles.button, styles.feedback, css`:host { display: block; }`];

  declare severity: Severity;
  declare variant: AlertVariant;
  declare title: string;
  declare icon: string | undefined;
  declare dense: boolean;
  declare dismissible: boolean;
  declare live: boolean;
  declare hasActions: boolean;

  constructor() {
    super();
    this.severity = 'info';
    this.variant = 'soft';
    this.title = '';
    this.dense = false;
    this.dismissible = false;
    this.live = false;
    this.hasActions = false;
  }

  override connectedCallback(): void {
    super.connectedCallback();
    this.hasActions = !!this.querySelector(':scope > [slot="actions"]');
  }

  /** Hides the alert. Fires a cancelable `sl-dismissed` first. */
  dismiss(): void {
    const proceed = this.dispatchEvent(new CustomEvent('sl-dismissed', { bubbles: true, composed: true, cancelable: true }));
    if (proceed) this.hidden = true;
  }

  override render() {
    const role = this.severity === 'error' ? 'alert' : this.live ? 'status' : undefined;
    const icon = this.icon === 'none' ? undefined : (this.icon ?? severityIcon(this.severity));
    const classes = {
      'sl-alert': true,
      [`sl-alert--${this.variant}`]: true,
      [`sl-alert--${this.severity}`]: true,
      [`sl-tone-${severityTone(this.severity)}`]: true,
      'sl-alert--dense': this.dense,
    };
    return html`<div class=${classMap(classes)} part="base" role=${ifDefined(role)}>
      ${icon ? html`<span class="sl-alert__icon" part="icon">${renderIcon(icon)}</span>` : nothing}
      <div class="sl-alert__content">
        ${this.title ? html`<strong class="sl-alert__title" part="title">${this.title}</strong>` : nothing}
        <div class="sl-alert__message" part="message"><slot></slot></div>
      </div>
      <div class="sl-alert__actions" part="actions" ?hidden=${!this.hasActions && !this.dismissible}>
        <slot name="actions" @slotchange=${this.onActionsChange}></slot>
        ${this.dismissible
          ? html`<button
              class="${iconButtonClasses} sl-button--small sl-alert__close"
              part="close"
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
