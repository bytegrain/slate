import { LitElement, css, html, nothing } from 'lit';
import { classMap } from 'lit/directives/class-map.js';
import { ifDefined } from 'lit/directives/if-defined.js';
import { hostReset, styles } from '../internal/styles';
import { renderIcon } from '../internal/icon';
import { attachFormInternals, owningForm } from '../internal/form';
import { DefaultsController, getDefaults, type ButtonVariant, type ControlSize, type Radius, type Tone } from '../core/defaults';

export type { ButtonVariant, ControlSize };

/** `pressed` attribute: absent → not a toggle (undefined); "false" → false; anything else → true. */
const optionalBoolean = {
  fromAttribute: (value: string | null) => (value === null ? undefined : value !== 'false'),
  toAttribute: (value: boolean | undefined) => (value === undefined ? null : String(value)),
};

/** Classes shared by internal icon-only ghost buttons (alert close, dialog close, menu button). */
export const iconButtonClasses = 'sl-button sl-button--ghost sl-tone-neutral sl-button--icon-only';

/**
 * <sl-button variant="solid" tone="accent" start-icon="plus" shortcut="⌘↵">Deploy</sl-button>
 * <sl-button icon-only start-icon="search" label="Search"></sl-button>
 *
 * Variant × tone (docs/design/configurability.md): the primary action is solid + accent. Unset options use
 * configureDefaults({ button: … }). Form-associated: type="submit" / "reset" act on the surrounding <form>.
 * With `href` it renders a link. Slots: default (label), `start` and `end` (custom leading/trailing content).
 */
export class SlButton extends LitElement {
  static formAssociated = true;
  static override shadowRootOptions = { ...LitElement.shadowRootOptions, delegatesFocus: true };

  static override properties = {
    variant: { reflect: true },
    tone: { reflect: true },
    size: { reflect: true },
    radius: { reflect: true },
    startIcon: { attribute: 'start-icon' },
    endIcon: { attribute: 'end-icon' },
    iconOnly: { type: Boolean, attribute: 'icon-only', reflect: true },
    label: {},
    loading: { type: Boolean, reflect: true },
    disabled: { type: Boolean, reflect: true },
    fullWidth: { type: Boolean, attribute: 'full-width', reflect: true },
    pressed: { converter: optionalBoolean, reflect: true },
    shortcut: {},
    href: {},
    target: {},
    type: { reflect: true },
    name: {},
    value: {},
    slotted: { state: true },
  };

  static override styles = [
    hostReset,
    styles.base,
    styles.typography,
    styles.feedback,
    styles.tone,
    styles.button,
    css`
      :host {
        display: inline-flex;
        vertical-align: middle;
      }
      :host([full-width]) {
        display: flex;
      }
      .sl-button__label:empty {
        display: none;
      }
    `,
  ];

  /** Unset → defaults.button.variant ('outlined'). */
  declare variant: ButtonVariant | undefined;
  /** Unset → defaults.button.tone ('neutral'). */
  declare tone: Tone | undefined;
  declare size: ControlSize | undefined;
  declare radius: Radius | undefined;
  declare startIcon: string | undefined;
  declare endIcon: string | undefined;
  /** Square button showing only `startIcon`; `label` becomes its accessible name (required). */
  declare iconOnly: boolean;
  /** Accessible name; required for icon-only buttons. */
  declare label: string | undefined;
  declare loading: boolean;
  declare disabled: boolean;
  declare fullWidth: boolean;
  /** Toggle state (aria-pressed). Undefined = not a toggle button. */
  declare pressed: boolean | undefined;
  declare shortcut: string | undefined;
  declare href: string | undefined;
  declare target: string | undefined;
  declare type: 'button' | 'submit' | 'reset';
  declare name: string | undefined;
  declare value: string | undefined;
  /** Which of the start/end slots have light-DOM content. */
  declare slotted: { start: boolean; end: boolean };

  private readonly internals: ElementInternals | undefined;

  constructor() {
    super();
    new DefaultsController(this);
    this.iconOnly = false;
    this.loading = false;
    this.disabled = false;
    this.fullWidth = false;
    this.type = 'button';
    this.slotted = { start: false, end: false };
    this.internals = attachFormInternals(this);
  }

  /** Effective option values (parameter, else app default). */
  get resolved(): { variant: ButtonVariant; tone: Tone; size: ControlSize; radius: Radius } {
    const d = getDefaults().button;
    return { variant: this.variant ?? d.variant, tone: this.tone ?? d.tone, size: this.size ?? d.size, radius: this.radius ?? d.radius };
  }

  get form(): HTMLFormElement | null {
    return owningForm(this, this.internals);
  }

  override connectedCallback(): void {
    super.connectedCallback();
    this.slotted = {
      start: !!this.querySelector(':scope > [slot="start"]'),
      end: !!this.querySelector(':scope > [slot="end"]'),
    };
  }

  private onSlotChange(e: Event): void {
    const slot = e.target as HTMLSlotElement;
    this.slotted = { ...this.slotted, [slot.name]: slot.assignedNodes().length > 0 };
  }

  override focus(options?: FocusOptions): void {
    this.renderRoot.querySelector<HTMLElement>('.sl-button')?.focus(options);
  }

  override click(): void {
    this.renderRoot.querySelector<HTMLElement>('.sl-button')?.click();
  }

  override updated(changed: Map<string, unknown>): void {
    if ((changed.has('iconOnly') || changed.has('label')) && this.iconOnly && !this.label) {
      console.warn('<sl-button>: icon-only buttons need a `label` for assistive technology.', this);
    }
  }

  override render() {
    const { variant, tone, size, radius } = this.resolved;
    const blocked = this.disabled || this.loading;
    const classes = {
      'sl-button': true,
      [`sl-button--${variant}`]: true,
      [`sl-tone-${tone}`]: true,
      [`sl-button--${size}`]: size !== 'medium',
      [`sl-radius-${radius}`]: radius !== 'default',
      'sl-button--icon-only': this.iconOnly,
      'sl-button--full': this.fullWidth,
      'is-loading': this.loading,
    };

    const content = html`
      <span class="sl-button__start" part="start" ?hidden=${!(this.loading || this.startIcon || this.slotted.start)}
        ><slot name="start" @slotchange=${this.onSlotChange}
          >${this.loading
            ? html`<span class="sl-spinner sl-button__icon" part="spinner" aria-hidden="true"></span>`
            : renderIcon(this.startIcon, 'sl-button__icon')}</slot
        ></span
      >
      ${this.iconOnly ? nothing : html`<span class="sl-button__label" part="label"><slot></slot></span>`}
      ${this.iconOnly
        ? nothing
        : html`<span class="sl-button__end" part="end" ?hidden=${!(this.endIcon || this.slotted.end)}
            ><slot name="end" @slotchange=${this.onSlotChange}>${renderIcon(this.endIcon, 'sl-button__icon')}</slot></span
          >`}
      ${this.shortcut ? html`<kbd class="sl-kbd" aria-hidden="true">${this.shortcut}</kbd>` : nothing}
    `;

    if (this.href && !this.disabled) {
      return html`<a
        class=${classMap(classes)}
        part="base"
        href=${this.href}
        target=${ifDefined(this.target)}
        rel=${ifDefined(this.target === '_blank' ? 'noreferrer noopener' : undefined)}
        aria-label=${ifDefined(this.label)}
        aria-busy=${this.loading ? 'true' : 'false'}
        >${content}</a
      >`;
    }

    return html`<button
      class=${classMap(classes)}
      part="base"
      type="button"
      ?disabled=${this.disabled}
      aria-disabled=${blocked ? 'true' : 'false'}
      aria-busy=${this.loading ? 'true' : 'false'}
      aria-pressed=${ifDefined(this.pressed === undefined ? undefined : String(this.pressed))}
      aria-label=${ifDefined(this.label)}
      aria-keyshortcuts=${ifDefined(this.shortcut)}
      @click=${this.handleClick}
    >
      ${content}
    </button>`;
  }

  private handleClick(event: MouseEvent): void {
    if (this.disabled || this.loading) {
      event.preventDefault();
      event.stopImmediatePropagation();
      return;
    }
    const form = this.form;
    if (!form) return;
    if (this.type === 'submit') {
      // Submit as if this button were the submitter, so its name/value are included.
      const proxy = document.createElement('button');
      proxy.type = 'submit';
      proxy.hidden = true;
      if (this.name) proxy.name = this.name;
      if (this.value !== undefined) proxy.value = this.value;
      form.append(proxy);
      try {
        form.requestSubmit(proxy);
      } finally {
        proxy.remove();
      }
    } else if (this.type === 'reset') {
      form.reset();
    }
  }
}
