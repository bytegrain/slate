import { LitElement, css, html, nothing } from 'lit';
import { classMap } from 'lit/directives/class-map.js';
import { ifDefined } from 'lit/directives/if-defined.js';
import { live } from 'lit/directives/live.js';
import { hostReset, styles } from '../internal/styles';
import { renderIcon } from '../internal/icon';
import { uid } from '../internal/dom';
import { attachFormInternals, owningForm, watchFormReset } from '../internal/form';
import type { ControlSize } from './button';

export type TextFieldType = 'text' | 'password' | 'email' | 'number' | 'search' | 'tel' | 'url';

/**
 * <sl-text-field label="Email" type="email" helper-text="…" error="…" required prefix="https://"></sl-text-field>
 *
 * Form-associated. Fires `input` (as typed) and `change` (on commit); both bubble out of the shadow root.
 */
export class SlTextField extends LitElement {
  static formAssociated = true;
  static override shadowRootOptions = { ...LitElement.shadowRootOptions, delegatesFocus: true };

  static override properties = {
    label: {},
    value: {},
    placeholder: {},
    helperText: { attribute: 'helper-text' },
    error: {},
    required: { type: Boolean, reflect: true },
    disabled: { type: Boolean, reflect: true },
    readonly: { type: Boolean, reflect: true },
    type: {},
    multiline: { type: Boolean, reflect: true },
    rows: { type: Number },
    prefixText: { attribute: 'prefix' },
    suffixText: { attribute: 'suffix' },
    startIcon: { attribute: 'start-icon' },
    size: { reflect: true },
    name: { reflect: true },
    autocomplete: {},
    minlength: { type: Number },
    maxlength: { type: Number },
  };

  static override styles = [hostReset, styles.base, styles.typography, styles.field, css`:host { display: block; }`];

  declare label: string;
  declare value: string;
  declare placeholder: string | undefined;
  declare helperText: string | undefined;
  /** A non-empty error marks the field invalid and replaces the helper text. */
  declare error: string | undefined;
  declare required: boolean;
  declare disabled: boolean;
  declare readonly: boolean;
  declare type: TextFieldType;
  declare multiline: boolean;
  declare rows: number;
  /** Text segment before the input (attribute `prefix`; `Element.prefix` is reserved by the DOM). */
  declare prefixText: string | undefined;
  /** Text segment after the input (attribute `suffix`). */
  declare suffixText: string | undefined;
  declare startIcon: string | undefined;
  declare size: ControlSize;
  declare name: string | undefined;
  declare autocomplete: string | undefined;
  declare minlength: number | undefined;
  declare maxlength: number | undefined;

  private readonly internals: ElementInternals | undefined;
  private readonly inputId = uid('sl-field-input');
  private readonly descId = uid('sl-field-desc');
  private defaultValue = '';
  private stopWatchingReset: () => void = () => {};

  constructor() {
    super();
    this.label = '';
    this.value = '';
    this.required = false;
    this.disabled = false;
    this.readonly = false;
    this.type = 'text';
    this.multiline = false;
    this.rows = 3;
    this.size = 'md';
    this.internals = attachFormInternals(this);
  }

  override connectedCallback(): void {
    super.connectedCallback();
    this.defaultValue = this.getAttribute('value') ?? '';
    this.stopWatchingReset = watchFormReset(this, this.internals);
  }

  override disconnectedCallback(): void {
    super.disconnectedCallback();
    this.stopWatchingReset();
  }

  get form(): HTMLFormElement | null {
    return owningForm(this, this.internals);
  }

  get validity(): ValidityState | undefined {
    return this.internals?.validity;
  }

  get validationMessage(): string {
    return this.internals?.validationMessage ?? '';
  }

  checkValidity(): boolean {
    return this.internals?.checkValidity() ?? true;
  }

  reportValidity(): boolean {
    return this.internals?.reportValidity() ?? true;
  }

  get input(): HTMLInputElement | HTMLTextAreaElement | null {
    return this.renderRoot?.querySelector('.sl-field__input') ?? null;
  }

  override focus(options?: FocusOptions): void {
    this.input?.focus(options);
  }

  select(): void {
    this.input?.select();
  }

  formResetCallback(): void {
    this.value = this.defaultValue;
  }

  formDisabledCallback(disabled: boolean): void {
    this.disabled = disabled;
  }

  override updated(): void {
    this.internals?.setFormValue(this.value ?? '');
    this.updateValidity();
  }

  private updateValidity(): void {
    if (!this.internals) return;
    const anchor = this.input ?? undefined;
    if (this.error) {
      this.internals.setValidity({ customError: true }, this.error, anchor);
    } else if (this.required && !this.value) {
      this.internals.setValidity({ valueMissing: true }, 'Please fill in this field.', anchor);
    } else {
      this.internals.setValidity({});
    }
  }

  override render() {
    const invalid = !!this.error;
    const describedBy = invalid || this.helperText ? this.descId : undefined;
    const classes = {
      'sl-field': true,
      [`sl-field--${this.size}`]: this.size !== 'md',
      'sl-field--multiline': this.multiline,
      'sl-field--invalid': invalid,
      'sl-field--disabled': this.disabled,
    };

    const shared = {
      id: this.inputId,
      describedBy,
    };

    const control = this.multiline
      ? html`<textarea
          id=${shared.id}
          class="sl-field__input sl-field__input--multiline"
          part="input"
          rows=${this.rows}
          .value=${live(this.value ?? '')}
          placeholder=${ifDefined(this.placeholder)}
          name=${ifDefined(this.name)}
          ?required=${this.required}
          ?disabled=${this.disabled}
          ?readonly=${this.readonly}
          minlength=${ifDefined(this.minlength)}
          maxlength=${ifDefined(this.maxlength)}
          aria-invalid=${invalid ? 'true' : 'false'}
          aria-describedby=${ifDefined(shared.describedBy)}
          @input=${this.onInput}
          @change=${this.onChange}
        ></textarea>`
      : html`<input
          id=${shared.id}
          class="sl-field__input"
          part="input"
          type=${this.type}
          .value=${live(this.value ?? '')}
          placeholder=${ifDefined(this.placeholder)}
          name=${ifDefined(this.name)}
          autocomplete=${ifDefined(this.autocomplete as AutoFill | undefined)}
          ?required=${this.required}
          ?disabled=${this.disabled}
          ?readonly=${this.readonly}
          minlength=${ifDefined(this.minlength)}
          maxlength=${ifDefined(this.maxlength)}
          aria-invalid=${invalid ? 'true' : 'false'}
          aria-describedby=${ifDefined(shared.describedBy)}
          @input=${this.onInput}
          @change=${this.onChange}
        />`;

    return html`<div class=${classMap(classes)} part="base">
      ${this.label
        ? html`<label class="sl-field__label" part="label" for=${this.inputId}
            >${this.label}${this.required ? html`<span class="sl-field__required" aria-hidden="true">*</span>` : nothing}</label
          >`
        : nothing}
      <div class="sl-field__control" part="control">
        ${this.prefixText ? html`<span class="sl-field__affix sl-field__affix--prefix">${this.prefixText}</span>` : nothing}
        ${this.startIcon ? html`<span class="sl-field__icon">${renderIcon(this.startIcon)}</span>` : nothing}
        ${control}
        ${this.suffixText ? html`<span class="sl-field__affix sl-field__affix--suffix">${this.suffixText}</span>` : nothing}
      </div>
      ${invalid
        ? html`<p class="sl-field__error" id=${this.descId} part="error">${renderIcon('alert-circle')}${this.error}</p>`
        : this.helperText
          ? html`<p class="sl-field__helper" id=${this.descId} part="helper">${this.helperText}</p>`
          : nothing}
    </div>`;
  }

  private onInput(e: Event): void {
    this.value = (e.target as HTMLInputElement).value;
  }

  private onChange(e: Event): void {
    this.value = (e.target as HTMLInputElement).value;
    // Native `change` is not composed; re-dispatch from the host.
    this.dispatchEvent(new Event('change', { bubbles: true, composed: true }));
  }
}
