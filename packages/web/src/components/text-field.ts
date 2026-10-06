import { LitElement, css, html, nothing } from 'lit';
import { classMap } from 'lit/directives/class-map.js';
import { ifDefined } from 'lit/directives/if-defined.js';
import { live } from 'lit/directives/live.js';
import { hostReset, styles } from '../internal/styles';
import { renderIcon } from '../internal/icon';
import { uid } from '../internal/dom';
import { attachFormInternals, owningForm, watchFormReset } from '../internal/form';
import { DefaultsController, getDefaults, type ControlSize, type FieldVariant, type Radius } from '../core/defaults';

export type TextFieldType = 'text' | 'password' | 'email' | 'number' | 'search' | 'tel' | 'url';
export type { FieldVariant };

/**
 * <sl-text-field label="Email" input-type="email" variant="filled" helper-text="…" error="…" required
 *                prefix="https://" max-length="40" counter clearable></sl-text-field>
 *
 * Slots `start-content` / `end-content` replace the start/end icons with custom adornments.
 * Form-associated. Fires `input` (as typed), `change` (on commit) and `sl-value-changed` (detail: { value })
 * whenever the value changes through the UI (typing or the clear button).
 */
export class SlTextField extends LitElement {
  static formAssociated = true;
  static override shadowRootOptions = { ...LitElement.shadowRootOptions, delegatesFocus: true };

  static override properties = {
    variant: { reflect: true },
    size: { reflect: true },
    radius: { reflect: true },
    label: {},
    value: {},
    placeholder: {},
    helperText: { attribute: 'helper-text' },
    error: {},
    required: { type: Boolean, reflect: true },
    disabled: { type: Boolean, reflect: true },
    readOnly: { type: Boolean, attribute: 'read-only', reflect: true },
    inputType: { attribute: 'input-type' },
    multiline: { type: Boolean, reflect: true },
    rows: { type: Number },
    maxLength: { type: Number, attribute: 'max-length' },
    minLength: { type: Number, attribute: 'min-length' },
    counter: { type: Boolean, reflect: true },
    clearable: { type: Boolean, reflect: true },
    prefixText: { attribute: 'prefix' },
    suffixText: { attribute: 'suffix' },
    startIcon: { attribute: 'start-icon' },
    endIcon: { attribute: 'end-icon' },
    name: { reflect: true },
    autocomplete: {},
    slotted: { state: true },
  };

  static override styles = [hostReset, styles.base, styles.typography, styles.field, css`:host { display: block; }`];

  declare variant: FieldVariant | undefined;
  declare size: ControlSize | undefined;
  declare radius: Radius | undefined;
  declare label: string;
  declare value: string;
  declare placeholder: string | undefined;
  declare helperText: string | undefined;
  /** A non-empty error marks the field invalid and replaces the helper text. */
  declare error: string | undefined;
  declare required: boolean;
  declare disabled: boolean;
  declare readOnly: boolean;
  declare inputType: TextFieldType;
  declare multiline: boolean;
  declare rows: number;
  declare maxLength: number | undefined;
  declare minLength: number | undefined;
  /** Shows the value length (and max-length). */
  declare counter: boolean;
  /** Shows a clear button while the field has a value. */
  declare clearable: boolean;
  /** Text segment before the input (attribute `prefix`; the `prefix` property is reserved by the DOM). */
  declare prefixText: string | undefined;
  /** Text segment after the input (attribute `suffix`). */
  declare suffixText: string | undefined;
  declare startIcon: string | undefined;
  declare endIcon: string | undefined;
  declare name: string | undefined;
  declare autocomplete: string | undefined;
  declare slotted: { 'start-content': boolean; 'end-content': boolean };

  private readonly internals: ElementInternals | undefined;
  private readonly inputId = uid('sl-field-input');
  private readonly descId = uid('sl-field-desc');
  private defaultValue = '';
  private stopWatchingReset: () => void = () => {};

  constructor() {
    super();
    new DefaultsController(this);
    this.label = '';
    this.value = '';
    this.required = false;
    this.disabled = false;
    this.readOnly = false;
    this.inputType = 'text';
    this.multiline = false;
    this.rows = 3;
    this.counter = false;
    this.clearable = false;
    this.slotted = { 'start-content': false, 'end-content': false };
    this.internals = attachFormInternals(this);
  }

  get resolved(): { variant: FieldVariant; size: ControlSize; radius: Radius } {
    const d = getDefaults().field;
    return { variant: this.variant ?? d.variant, size: this.size ?? d.size, radius: this.radius ?? d.radius };
  }

  override connectedCallback(): void {
    super.connectedCallback();
    this.defaultValue = this.getAttribute('value') ?? '';
    this.slotted = {
      'start-content': !!this.querySelector(':scope > [slot="start-content"]'),
      'end-content': !!this.querySelector(':scope > [slot="end-content"]'),
    };
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

  /** Empties the field (as the clear button does) and notifies listeners. */
  clear(): void {
    if (!this.value) return;
    this.setValueFromUser('');
    this.dispatchEvent(new Event('input', { bubbles: true, composed: true }));
    this.dispatchEvent(new Event('change', { bubbles: true, composed: true }));
    this.input?.focus();
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
    const { variant, size, radius } = this.resolved;
    const invalid = !!this.error;
    const describedBy = invalid || this.helperText ? this.descId : undefined;
    const classes = {
      'sl-field': true,
      [`sl-field--${variant}`]: true,
      [`sl-field--${size}`]: size !== 'medium',
      [`sl-radius-${radius}`]: radius !== 'default',
      'sl-field--multiline': this.multiline,
      'sl-field--invalid': invalid,
      'sl-field--disabled': this.disabled,
    };

    const common = {
      describedBy,
    };

    const control = this.multiline
      ? html`<textarea
          id=${this.inputId}
          class="sl-field__input sl-field__input--multiline"
          part="input"
          rows=${this.rows}
          .value=${live(this.value ?? '')}
          placeholder=${ifDefined(this.placeholder)}
          name=${ifDefined(this.name)}
          ?required=${this.required}
          ?disabled=${this.disabled}
          ?readonly=${this.readOnly}
          minlength=${ifDefined(this.minLength)}
          maxlength=${ifDefined(this.maxLength)}
          aria-invalid=${invalid ? 'true' : 'false'}
          aria-describedby=${ifDefined(common.describedBy)}
          @input=${this.onInput}
          @change=${this.onChange}
        ></textarea>`
      : html`<input
          id=${this.inputId}
          class="sl-field__input"
          part="input"
          type=${this.inputType}
          .value=${live(this.value ?? '')}
          placeholder=${ifDefined(this.placeholder)}
          name=${ifDefined(this.name)}
          autocomplete=${ifDefined(this.autocomplete as AutoFill | undefined)}
          ?required=${this.required}
          ?disabled=${this.disabled}
          ?readonly=${this.readOnly}
          minlength=${ifDefined(this.minLength)}
          maxlength=${ifDefined(this.maxLength)}
          aria-invalid=${invalid ? 'true' : 'false'}
          aria-describedby=${ifDefined(common.describedBy)}
          @input=${this.onInput}
          @change=${this.onChange}
        />`;

    const hasStartContent = this.slotted['start-content'];
    const hasEndContent = this.slotted['end-content'];
    const showClear = this.clearable && !!this.value && !this.disabled && !this.readOnly;
    const length = (this.value ?? '').length;

    return html`<div class=${classMap(classes)} part="base">
      ${this.label
        ? html`<label class="sl-field__label" part="label" for=${this.inputId}
            >${this.label}${this.required ? html`<span class="sl-field__required" aria-hidden="true">*</span>` : nothing}</label
          >`
        : nothing}
      <div class="sl-field__control" part="control">
        ${this.prefixText ? html`<span class="sl-field__affix sl-field__affix--prefix" part="prefix">${this.prefixText}</span>` : nothing}
        <span class="sl-field__adornment sl-field__adornment--start" ?hidden=${!hasStartContent}
          ><slot name="start-content" @slotchange=${this.onSlotChange}></slot
        ></span>
        ${this.startIcon && !hasStartContent ? html`<span class="sl-field__icon sl-field__icon--start">${renderIcon(this.startIcon)}</span>` : nothing}
        ${control}
        ${showClear
          ? html`<button class="sl-field__clear" type="button" aria-label="Clear" @click=${this.clear}>${renderIcon('x')}</button>`
          : nothing}
        ${this.endIcon && !hasEndContent ? html`<span class="sl-field__icon sl-field__icon--end">${renderIcon(this.endIcon)}</span>` : nothing}
        <span class="sl-field__adornment sl-field__adornment--end" ?hidden=${!hasEndContent}
          ><slot name="end-content" @slotchange=${this.onSlotChange}></slot
        ></span>
        ${this.suffixText ? html`<span class="sl-field__affix sl-field__affix--suffix" part="suffix">${this.suffixText}</span>` : nothing}
      </div>
      ${invalid || this.helperText || this.counter
        ? html`<div class="sl-field__footer">
            ${invalid
              ? html`<p class="sl-field__error" id=${this.descId} part="error">${renderIcon('alert-circle')}${this.error}</p>`
              : this.helperText
                ? html`<p class="sl-field__helper" id=${this.descId} part="helper">${this.helperText}</p>`
                : nothing}
            ${this.counter
              ? html`<span
                  class=${classMap({ 'sl-field__counter': true, 'is-over': this.maxLength !== undefined && length > this.maxLength })}
                  part="counter"
                  aria-live="polite"
                  >${this.maxLength !== undefined ? `${length} / ${this.maxLength}` : length}</span
                >`
              : nothing}
          </div>`
        : nothing}
    </div>`;
  }

  private onSlotChange(e: Event): void {
    const slot = e.target as HTMLSlotElement;
    this.slotted = { ...this.slotted, [slot.name]: slot.assignedNodes().length > 0 };
  }

  private setValueFromUser(value: string): void {
    if (value === this.value) return;
    this.value = value;
    this.dispatchEvent(new CustomEvent('sl-value-changed', { bubbles: true, composed: true, detail: { value } }));
  }

  private onInput(e: Event): void {
    this.setValueFromUser((e.target as HTMLInputElement).value);
  }

  private onChange(e: Event): void {
    this.setValueFromUser((e.target as HTMLInputElement).value);
    // Native `change` is not composed; re-dispatch from the host.
    this.dispatchEvent(new Event('change', { bubbles: true, composed: true }));
  }
}
