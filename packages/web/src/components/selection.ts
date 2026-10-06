import { LitElement, css, html, nothing } from 'lit';
import { classMap } from 'lit/directives/class-map.js';
import { ifDefined } from 'lit/directives/if-defined.js';
import { live } from 'lit/directives/live.js';
import { hostReset, styles } from '../internal/styles';
import { uid } from '../internal/dom';
import { attachFormInternals, owningForm, watchFormReset } from '../internal/form';
import { DefaultsController, getDefaults, type ControlSize, type Direction, type Placement, type Tone } from '../core/defaults';

/** Shared form plumbing and options for boolean controls. */
abstract class ToggleBase extends LitElement {
  static formAssociated = true;
  static override shadowRootOptions = { ...LitElement.shadowRootOptions, delegatesFocus: true };

  static override properties = {
    checked: { type: Boolean, reflect: true },
    disabled: { type: Boolean, reflect: true },
    required: { type: Boolean, reflect: true },
    name: { reflect: true },
    value: {},
    label: {},
    description: {},
    tone: { reflect: true },
    size: { reflect: true },
    labelPlacement: { attribute: 'label-placement', reflect: true },
  };

  declare checked: boolean;
  declare disabled: boolean;
  declare required: boolean;
  declare name: string | undefined;
  declare value: string;
  declare label: string | undefined;
  declare description: string | undefined;
  /** Checked colour. Default 'accent'. */
  declare tone: Tone | undefined;
  declare size: ControlSize | undefined;
  /** Label side. Unset → defaults.selection.labelPlacement ('end'). */
  declare labelPlacement: Placement | undefined;

  protected readonly internals: ElementInternals | undefined;
  protected readonly inputId = uid('sl-toggle');
  protected readonly descId = uid('sl-toggle-desc');
  private defaultChecked = false;
  private stopWatchingReset: () => void = () => {};

  constructor() {
    super();
    new DefaultsController(this);
    this.checked = false;
    this.disabled = false;
    this.required = false;
    this.value = 'on';
    this.internals = attachFormInternals(this);
  }

  protected get placement(): Placement {
    return this.labelPlacement ?? getDefaults().selection.labelPlacement;
  }

  /** Effective option values (parameter, else default). */
  get resolved(): { tone: Tone; size: ControlSize; labelPlacement: Placement } {
    return { tone: this.tone ?? 'accent', size: this.size ?? 'medium', labelPlacement: this.placement };
  }

  /** Root classes shared by the checkbox and switch. */
  protected rootClasses(block: string): Record<string, boolean> {
    const size = this.size ?? 'medium';
    return {
      [block]: true,
      [`sl-tone-${this.tone ?? 'accent'}`]: true,
      [`${block}--${size}`]: size !== 'medium',
      [`${block}--label-${this.placement}`]: true,
      'is-disabled': this.disabled,
    };
  }

  override connectedCallback(): void {
    super.connectedCallback();
    this.defaultChecked = this.hasAttribute('checked');
    this.stopWatchingReset = watchFormReset(this, this.internals);
  }

  override disconnectedCallback(): void {
    super.disconnectedCallback();
    this.stopWatchingReset();
  }

  get form(): HTMLFormElement | null {
    return owningForm(this, this.internals);
  }

  get input(): HTMLInputElement | null {
    return this.renderRoot?.querySelector('input') ?? null;
  }

  override focus(options?: FocusOptions): void {
    this.input?.focus(options);
  }

  override click(): void {
    this.input?.click();
  }

  formResetCallback(): void {
    this.checked = this.defaultChecked;
  }

  formDisabledCallback(disabled: boolean): void {
    this.disabled = disabled;
  }

  checkValidity(): boolean {
    return this.internals?.checkValidity() ?? true;
  }

  override updated(): void {
    this.internals?.setFormValue(this.checked ? this.value : null);
    if (this.internals) {
      if (this.required && !this.checked) {
        this.internals.setValidity({ valueMissing: true }, 'Please tick this box to continue.', this.input ?? undefined);
      } else {
        this.internals.setValidity({});
      }
    }
  }

  protected onChange(e: Event): void {
    this.applyInput(e.target as HTMLInputElement);
    this.dispatchEvent(new Event('change', { bubbles: true, composed: true }));
    this.dispatchEvent(new CustomEvent('sl-checked-changed', { bubbles: true, composed: true, detail: { checked: this.checked } }));
  }

  protected applyInput(input: HTMLInputElement): void {
    this.checked = input.checked;
  }

  protected renderText(prefix: string) {
    return html`<span class="${prefix}__text">
      <span class="${prefix}__label" part="label"><slot>${this.label ?? ''}</slot></span>
      ${this.description
        ? html`<span class="${prefix}__description" id=${this.descId} part="description">${this.description}</span>`
        : nothing}
    </span>`;
  }
}

/**
 * <sl-checkbox label="Run tests" description="…" checked indeterminate tone="success" size="small"
 *              label-placement="start"></sl-checkbox>
 * `indeterminate` is the web spelling of Checked = null. Fires `change` and `sl-checked-changed`.
 */
export class SlCheckbox extends ToggleBase {
  static override properties = {
    ...ToggleBase.properties,
    indeterminate: { type: Boolean, reflect: true },
  };
  static override styles = [hostReset, styles.base, styles.tone, styles.selection, css`:host { display: inline-flex; }`];

  declare indeterminate: boolean;

  constructor() {
    super();
    this.indeterminate = false;
  }

  protected override applyInput(input: HTMLInputElement): void {
    this.checked = input.checked;
    this.indeterminate = false;
  }

  override render() {
    return html`<label class=${classMap(this.rootClasses('sl-checkbox'))} part="base">
      <input
        id=${this.inputId}
        class="sl-checkbox__input"
        part="box"
        type="checkbox"
        .checked=${live(this.checked)}
        .indeterminate=${this.indeterminate}
        ?disabled=${this.disabled}
        ?required=${this.required}
        aria-checked=${this.indeterminate ? 'mixed' : this.checked ? 'true' : 'false'}
        aria-describedby=${ifDefined(this.description ? this.descId : undefined)}
        @change=${this.onChange}
      />
      ${this.renderText('sl-checkbox')}
    </label>`;
  }
}

/** <sl-switch label="Preview deployments" checked label-placement="start" spread tone="accent"></sl-switch> */
export class SlSwitch extends ToggleBase {
  static override properties = {
    ...ToggleBase.properties,
    spread: { type: Boolean, reflect: true },
  };
  static override styles = [
    hostReset,
    styles.base,
    styles.tone,
    styles.selection,
    css`
      :host { display: inline-flex; }
      :host([spread]) { display: block; }
    `,
  ];

  /** Label and switch at opposite ends of the row (settings lists). */
  declare spread: boolean;

  constructor() {
    super();
    this.spread = false;
  }

  override render() {
    const classes = { ...this.rootClasses('sl-switch'), 'sl-switch--spread': this.spread };
    return html`<label class=${classMap(classes)} part="base">
      <span class="sl-switch__control">
        <input
          id=${this.inputId}
          class="sl-switch__input"
          part="track"
          type="checkbox"
          role="switch"
          .checked=${live(this.checked)}
          ?disabled=${this.disabled}
          aria-checked=${this.checked ? 'true' : 'false'}
          aria-describedby=${ifDefined(this.description ? this.descId : undefined)}
          @change=${this.onChange}
          @keydown=${this.onKeyDown}
        /><span class="sl-switch__thumb" part="thumb" aria-hidden="true"></span>
      </span>
      ${this.renderText('sl-switch')}
    </label>`;
  }

  /** Enter must not submit the form from a switch. */
  private onKeyDown(e: KeyboardEvent): void {
    if (e.key === 'Enter') e.preventDefault();
  }
}

/** <sl-radio value="team" label="Team" description="…"></sl-radio> — only inside <sl-radio-group>. */
export class SlRadio extends LitElement {
  static override properties = {
    value: { reflect: true },
    label: {},
    description: {},
    disabled: { type: Boolean, reflect: true },
    checked: { type: Boolean, reflect: true },
    tone: { attribute: false },
    size: { attribute: false },
  };
  static override styles = [
    hostReset,
    styles.base,
    styles.tone,
    styles.selection,
    css`
      :host {
        display: flex;
        outline: none;
      }
      :host(:focus-visible) .sl-radio__input {
        outline: var(--sl-focus-ring-width) solid var(--sl-color-focus-ring);
        outline-offset: var(--sl-focus-ring-offset);
      }
    `,
  ];

  declare value: string;
  declare label: string | undefined;
  declare description: string | undefined;
  declare disabled: boolean;
  /** Managed by the group. */
  declare checked: boolean;
  /** Managed by the group. */
  declare tone: Tone;
  /** Managed by the group. */
  declare size: ControlSize;

  constructor() {
    super();
    this.value = '';
    this.disabled = false;
    this.checked = false;
    this.tone = 'accent';
    this.size = 'medium';
  }

  override connectedCallback(): void {
    super.connectedCallback();
    this.setAttribute('role', 'radio');
    this.addEventListener('click', this.select);
    this.addEventListener('keydown', this.onKeyDown);
  }

  override disconnectedCallback(): void {
    super.disconnectedCallback();
    this.removeEventListener('click', this.select);
    this.removeEventListener('keydown', this.onKeyDown);
  }

  override updated(): void {
    this.setAttribute('aria-checked', String(this.checked));
    this.setAttribute('aria-disabled', String(this.disabled));
  }

  private readonly select = (): void => {
    if (this.disabled) return;
    this.dispatchEvent(new CustomEvent('sl-radio-select', { bubbles: true, composed: true, detail: { value: this.value } }));
  };

  private readonly onKeyDown = (e: KeyboardEvent): void => {
    if (e.key === ' ') {
      e.preventDefault();
      this.select();
    }
  };

  override render() {
    const classes = {
      'sl-radio': true,
      [`sl-tone-${this.tone}`]: true,
      [`sl-radio--${this.size}`]: this.size !== 'medium',
      'is-disabled': this.disabled,
    };
    return html`<span class=${classMap(classes)} part="base">
      <span class=${classMap({ 'sl-radio__input': true, 'is-checked': this.checked })} part="control" aria-hidden="true"></span>
      <span class="sl-radio__text">
        <span class="sl-radio__label" part="label"><slot>${this.label ?? ''}</slot></span>
        ${this.description ? html`<span class="sl-radio__description" part="description">${this.description}</span>` : nothing}
      </span>
    </span>`;
  }
}

/**
 * <sl-radio-group label="Plan" name="plan" value="team" direction="row" tone="accent">
 *   <sl-radio value="team" label="Team"></sl-radio> …
 * </sl-radio-group>
 * One tab stop; arrow keys move and select (roving tabindex). Form-associated.
 * Fires `input`, `change` and `sl-value-changed` (detail: { value }) when the user picks a radio.
 */
export class SlRadioGroup extends LitElement {
  static formAssociated = true;

  static override properties = {
    label: {},
    name: { reflect: true },
    value: { reflect: true },
    disabled: { type: Boolean, reflect: true },
    required: { type: Boolean, reflect: true },
    direction: { reflect: true },
    tone: { reflect: true },
    size: { reflect: true },
  };
  static override styles = [hostReset, styles.base, styles.selection, css`:host { display: block; }`];

  declare label: string | undefined;
  declare name: string | undefined;
  declare value: string;
  declare disabled: boolean;
  declare required: boolean;
  declare direction: Direction;
  declare tone: Tone;
  declare size: ControlSize;

  private readonly internals: ElementInternals | undefined;
  private readonly labelId = uid('sl-radio-group-label');
  private defaultValue = '';
  private stopWatchingReset: () => void = () => {};

  constructor() {
    super();
    this.value = '';
    this.disabled = false;
    this.required = false;
    this.direction = 'column';
    this.tone = 'accent';
    this.size = 'medium';
    this.internals = attachFormInternals(this);
    this.addEventListener('sl-radio-select', (e) => this.choose((e as CustomEvent<{ value: string }>).detail.value, true));
    this.addEventListener('keydown', this.onKeyDown);
  }

  override connectedCallback(): void {
    super.connectedCallback();
    this.setAttribute('role', 'radiogroup');
    this.defaultValue = this.getAttribute('value') ?? '';
    this.stopWatchingReset = watchFormReset(this, this.internals);
  }

  override disconnectedCallback(): void {
    super.disconnectedCallback();
    this.stopWatchingReset();
  }

  get radios(): SlRadio[] {
    return [...this.querySelectorAll<SlRadio>('sl-radio')];
  }

  get form(): HTMLFormElement | null {
    return owningForm(this, this.internals);
  }

  formResetCallback(): void {
    this.value = this.defaultValue;
  }

  formDisabledCallback(disabled: boolean): void {
    this.disabled = disabled;
  }

  checkValidity(): boolean {
    return this.internals?.checkValidity() ?? true;
  }

  override focus(options?: FocusOptions): void {
    (this.radios.find((r) => r.tabIndex === 0) ?? this.radios[0])?.focus(options);
  }

  override updated(): void {
    if (this.label) this.setAttribute('aria-label', this.label);
    this.setAttribute('aria-disabled', String(this.disabled));
    this.setAttribute('aria-required', String(this.required));
    this.setAttribute('aria-orientation', this.direction === 'row' ? 'horizontal' : 'vertical');
    this.syncRadios();
    this.internals?.setFormValue(this.value || null);
    if (this.internals) {
      if (this.required && !this.value) this.internals.setValidity({ valueMissing: true }, 'Please select an option.', this.radios[0]);
      else this.internals.setValidity({});
    }
  }

  private syncRadios = (): void => {
    const radios = this.radios;
    const enabled = radios.filter((r) => !r.disabled && !this.disabled);
    const current = radios.find((r) => r.value === this.value && !r.disabled);
    for (const r of radios) {
      r.checked = r.value === this.value;
      r.tone = this.tone;
      r.size = this.size;
      const focusable = current ? r === current : r === enabled[0];
      r.tabIndex = focusable && !this.disabled ? 0 : -1;
    }
  };

  private choose(value: string, fromUser: boolean): void {
    if (this.disabled || value === this.value) return;
    this.value = value;
    this.syncRadios();
    if (fromUser) {
      this.dispatchEvent(new Event('input', { bubbles: true, composed: true }));
      this.dispatchEvent(new Event('change', { bubbles: true, composed: true }));
      this.dispatchEvent(new CustomEvent('sl-value-changed', { bubbles: true, composed: true, detail: { value } }));
    }
  }

  private readonly onKeyDown = (e: KeyboardEvent): void => {
    const forward = ['ArrowDown', 'ArrowRight'].includes(e.key);
    const backward = ['ArrowUp', 'ArrowLeft'].includes(e.key);
    if (!forward && !backward) return;
    const enabled = this.radios.filter((r) => !r.disabled);
    if (enabled.length === 0 || this.disabled) return;
    e.preventDefault();
    const from = enabled.findIndex((r) => r === e.target || r.contains(e.target as Node));
    const index = (from + (forward ? 1 : -1) + enabled.length) % enabled.length;
    const next = enabled[index];
    this.choose(next.value, true);
    next.focus();
  };

  override render() {
    return html`<div class=${classMap({ 'sl-radio-group': true, 'sl-radio-group--row': this.direction === 'row' })} part="base">
      ${this.label ? html`<span class="sl-radio-group__label" id=${this.labelId} part="label">${this.label}</span>` : nothing}
      <div class="sl-radio-group__options" part="options"><slot @slotchange=${this.syncRadios}></slot></div>
    </div>`;
  }
}
