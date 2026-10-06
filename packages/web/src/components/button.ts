import { LitElement, css, html, nothing } from 'lit';
import { classMap } from 'lit/directives/class-map.js';
import { ifDefined } from 'lit/directives/if-defined.js';
import { hostReset, styles } from '../internal/styles';
import { renderIcon } from '../internal/icon';
import { attachFormInternals, owningForm } from '../internal/form';

/** Mirrors Slate.ButtonVariant. */
export type ButtonVariant = 'secondary' | 'primary' | 'ghost' | 'danger' | 'danger-solid' | 'link';
export type ControlSize = 'sm' | 'md' | 'lg';

/**
 * <sl-button variant="primary" size="md" icon-start="plus" shortcut="⌘↵" loading>Deploy</sl-button>
 * <sl-button icon="search" label="Search"></sl-button>  (icon-only: `label` is required)
 *
 * Form-associated: type="submit" / "reset" act on the surrounding <form>. With `href` it renders a link.
 */
export class SlButton extends LitElement {
  static formAssociated = true;
  static override shadowRootOptions = { ...LitElement.shadowRootOptions, delegatesFocus: true };

  static override properties = {
    variant: { reflect: true },
    size: { reflect: true },
    type: { reflect: true },
    disabled: { type: Boolean, reflect: true },
    loading: { type: Boolean, reflect: true },
    icon: {},
    iconStart: { attribute: 'icon-start' },
    iconEnd: { attribute: 'icon-end' },
    label: {},
    shortcut: {},
    fullWidth: { type: Boolean, attribute: 'full-width', reflect: true },
    href: {},
    target: {},
    name: {},
    value: {},
  };

  static override styles = [
    hostReset,
    styles.base,
    styles.typography,
    styles.feedback,
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

  declare variant: ButtonVariant;
  declare size: ControlSize;
  declare type: 'button' | 'submit' | 'reset';
  declare disabled: boolean;
  declare loading: boolean;
  /** Icon-only button. */
  declare icon: string | undefined;
  declare iconStart: string | undefined;
  declare iconEnd: string | undefined;
  /** Accessible name; required for icon-only buttons. */
  declare label: string | undefined;
  declare shortcut: string | undefined;
  declare fullWidth: boolean;
  declare href: string | undefined;
  declare target: string | undefined;
  declare name: string | undefined;
  declare value: string | undefined;

  private readonly internals: ElementInternals | undefined;

  constructor() {
    super();
    this.variant = 'secondary';
    this.size = 'md';
    this.type = 'button';
    this.disabled = false;
    this.loading = false;
    this.fullWidth = false;
    this.internals = attachFormInternals(this);
  }

  get form(): HTMLFormElement | null {
    return owningForm(this, this.internals);
  }

  override focus(options?: FocusOptions): void {
    this.renderRoot.querySelector<HTMLElement>('.sl-button')?.focus(options);
  }

  override click(): void {
    this.renderRoot.querySelector<HTMLElement>('.sl-button')?.click();
  }

  override updated(changed: Map<string, unknown>): void {
    if ((changed.has('icon') || changed.has('label')) && this.icon && !this.label && !this.textContent?.trim()) {
      console.warn('<sl-button>: icon-only buttons need a `label` for assistive technology.', this);
    }
  }

  override render() {
    const iconOnly = !!this.icon;
    const blocked = this.disabled || this.loading;
    const classes = {
      'sl-button': true,
      [`sl-button--${this.variant}`]: this.variant !== 'secondary',
      [`sl-button--${this.size}`]: this.size !== 'md',
      'sl-button--icon': iconOnly,
      'sl-button--full': this.fullWidth,
      'is-loading': this.loading,
    };

    const content = html`
      ${this.loading
        ? html`<span class="sl-spinner sl-button__icon" aria-hidden="true"></span>`
        : renderIcon(iconOnly ? this.icon : this.iconStart, 'sl-button__icon')}
      ${iconOnly ? nothing : html`<span class="sl-button__label" part="label"><slot></slot></span>`}
      ${!iconOnly && !this.loading ? renderIcon(this.iconEnd, 'sl-button__icon') : nothing}
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
