import { LitElement, css, html, nothing, type PropertyValues, type TemplateResult } from 'lit';
import { classMap } from 'lit/directives/class-map.js';
import { ifDefined } from 'lit/directives/if-defined.js';
import { live } from 'lit/directives/live.js';
import { hostReset, styles } from '../internal/styles';
import { renderIcon } from '../internal/icon';
import { uid } from '../internal/dom';
import { attachFormInternals, owningForm, watchFormReset } from '../internal/form';
import { Overlay } from '../internal/overlay';
import { filterOptions, foldText, groupIndices, moveIndex, Typeahead, type ListKey, type TextRange } from '../core/collections/list';
import { DefaultsController, getDefaults, type ControlSize, type FieldVariant, type Radius } from '../core/defaults';
import { fromTemplate, highlight, normalizeItems, valueKey, type NormalizedItem } from '../internal/items';

export type { ItemLike as SelectItem } from '../internal/items';

interface ViewOption {
  option: NormalizedItem | null; // null = the "create" row
  ranges: TextRange[];
  id: string;
}


/**
 * <sl-select label="Region" placeholder="Choose…" .items=${['eu-west', 'us-east']} value="eu-west"></sl-select>
 * <sl-select multiple searchable clearable label="Labels" items='[{"value":"bug","label":"Bug","group":"Type"}]'></sl-select>
 *
 * Items: strings, numbers or objects ({ value, label, group, icon, description, disabled }); `itemText` /
 * `groupBy` functions override. Single selection binds `value`, multiple binds `values`. `searchable` turns it
 * into a combobox (type to filter, case/diacritic-insensitive with highlights); `creatable` offers the typed text.
 * Custom rows: `<template slot="item-template">` with `[data-text]` / `[data-description]` placeholders.
 *
 * Form-associated (multiple submits one entry per value). Events: `sl-value-changed` (detail: { value }),
 * `sl-values-changed` (detail: { values }), `sl-search-changed` (detail: { query }), plus `input`/`change`.
 */
export class SlSelect extends LitElement {
  static formAssociated = true;
  static override shadowRootOptions = { ...LitElement.shadowRootOptions, delegatesFocus: true };

  static override properties = {
    items: { type: Array },
    value: {},
    values: { type: Array },
    multiple: { type: Boolean, reflect: true },
    searchable: { type: Boolean, reflect: true },
    creatable: { type: Boolean, reflect: true },
    itemText: { attribute: false },
    groupBy: { attribute: false },
    label: {},
    placeholder: {},
    helperText: { attribute: 'helper-text' },
    error: {},
    variant: { reflect: true },
    size: { reflect: true },
    radius: { reflect: true },
    clearable: { type: Boolean, reflect: true },
    disabled: { type: Boolean, reflect: true },
    required: { type: Boolean, reflect: true },
    loading: { type: Boolean, reflect: true },
    maxVisibleChips: { type: Number, attribute: 'max-visible-chips' },
    name: { reflect: true },
    open: { type: Boolean, reflect: true },
    query: { state: true },
    activeIndex: { state: true },
  };

  static override styles = [
    hostReset,
    styles.base,
    styles.feedback,
    styles.field,
    styles.overlay,
    styles.select,
    css`
      :host { display: block; }
    `,
  ];

  declare items: unknown[];
  declare value: unknown;
  declare values: unknown[];
  declare multiple: boolean;
  declare searchable: boolean;
  declare creatable: boolean;
  declare itemText: ((item: unknown) => string) | undefined;
  declare groupBy: ((item: unknown) => string) | undefined;
  declare label: string | undefined;
  declare placeholder: string | undefined;
  declare helperText: string | undefined;
  declare error: string | undefined;
  declare variant: FieldVariant | undefined;
  declare size: ControlSize | undefined;
  declare radius: Radius | undefined;
  declare clearable: boolean;
  declare disabled: boolean;
  declare required: boolean;
  declare loading: boolean;
  declare maxVisibleChips: number | undefined;
  declare name: string | undefined;
  /** Whether the listbox is open (reflected for styling; prefer the UI to drive it). */
  declare open: boolean;
  declare query: string;
  declare activeIndex: number;

  private readonly internals: ElementInternals | undefined;
  private readonly ids = { control: uid('sl-select'), label: uid('sl-select-label'), listbox: uid('sl-select-listbox'), desc: uid('sl-select-desc'), option: uid('sl-option') };
  private readonly typeahead = new Typeahead();
  private defaultValue: unknown = undefined;
  private defaultValues: unknown[] = [];
  private stopWatchingReset: () => void = () => {};
  private view: ViewOption[] = [];

  private readonly overlay = new Overlay({
    anchor: () => this.renderRoot?.querySelector('.sl-field__control') ?? null,
    panel: () => this.renderRoot?.querySelector<HTMLElement>('.sl-select__panel') ?? null,
    placement: () => 'bottom-start',
    offset: () => 4,
    matchWidth: () => true,
    onDismiss: () => this.closeList(),
  });

  constructor() {
    super();
    new DefaultsController(this);
    this.items = [];
    this.value = undefined;
    this.values = [];
    this.multiple = false;
    this.searchable = false;
    this.creatable = false;
    this.clearable = false;
    this.disabled = false;
    this.required = false;
    this.loading = false;
    this.open = false;
    this.query = '';
    this.activeIndex = -1;
    this.internals = attachFormInternals(this);
  }

  get resolved(): { variant: FieldVariant; size: ControlSize; radius: Radius } {
    const d = getDefaults().field;
    return { variant: this.variant ?? d.variant, size: this.size ?? d.size, radius: this.radius ?? d.radius };
  }

  get form(): HTMLFormElement | null {
    return owningForm(this, this.internals);
  }

  get options(): NormalizedItem[] {
    return normalizeItems(this.items, { itemText: this.itemText, groupBy: this.groupBy });
  }

  /** The control that holds focus (button for select-only, input when searchable). */
  get control(): HTMLElement | null {
    return this.renderRoot?.querySelector<HTMLElement>('.sl-select__trigger, .sl-select__input') ?? null;
  }

  override focus(options?: FocusOptions): void {
    this.control?.focus(options);
  }

  checkValidity(): boolean {
    return this.internals?.checkValidity() ?? true;
  }

  override connectedCallback(): void {
    super.connectedCallback();
    this.defaultValue = this.value;
    this.defaultValues = [...(this.values ?? [])];
    this.stopWatchingReset = watchFormReset(this, this.internals);
  }

  override disconnectedCallback(): void {
    super.disconnectedCallback();
    this.stopWatchingReset();
    this.overlay.close();
  }

  formResetCallback(): void {
    this.value = this.defaultValue;
    this.values = [...this.defaultValues];
  }

  formDisabledCallback(disabled: boolean): void {
    this.disabled = disabled;
  }

  /** Opens the listbox. */
  show(): void {
    if (this.disabled || this.open) return;
    this.open = true;
    const selected = this.view.findIndex((v) => v.option && this.isSelected(v.option));
    this.activeIndex = selected >= 0 ? selected : this.firstEnabled();
  }

  /** Closes the listbox. */
  hide(): void {
    this.closeList();
  }

  protected override willUpdate(): void {
    this.view = this.buildView();
    if (this.activeIndex >= this.view.length) this.activeIndex = this.view.length - 1;
  }

  protected override updated(changed: PropertyValues<this>): void {
    if (changed.has('open')) {
      if (this.open) this.overlay.open();
      else this.overlay.close();
    } else if (this.open && (changed.has('values') || changed.has('query') || changed.has('items'))) {
      this.overlay.position();
    }
    if (this.open && changed.has('activeIndex')) {
      this.renderRoot.querySelector('.sl-select__option.is-active')?.scrollIntoView?.({ block: 'nearest' });
    }
    this.syncForm();
  }

  // ---- selection model -----------------------------------------------------------------------------------

  private selectedKeys(): Set<string> {
    return this.multiple ? new Set((this.values ?? []).map(valueKey)) : new Set(this.value === undefined || this.value === null || this.value === '' ? [] : [valueKey(this.value)]);
  }

  private isSelected(option: NormalizedItem): boolean {
    return this.selectedKeys().has(option.key);
  }

  private textFor(value: unknown): string {
    const key = valueKey(value);
    return this.options.find((o) => o.key === key)?.text ?? String(value ?? '');
  }

  private hasValue(): boolean {
    return this.multiple ? (this.values ?? []).length > 0 : this.selectedKeys().size > 0;
  }

  private buildView(): ViewOption[] {
    const options = this.options;
    const query = this.searchable ? this.query.trim() : '';
    const matches = filterOptions(
      options.map((o) => o.text),
      query,
    );
    // Keep groups together (in first-appearance order) while preserving match order inside them.
    const ordered = matches.map((m) => ({ option: options[m.index], ranges: m.ranges }));
    const grouped = groupIndices(ordered.map((o) => o.option.group)).flatMap((g) => g.indices.map((i) => ordered[i]));
    const view: ViewOption[] = grouped.map((o, i) => ({ ...o, id: `${this.ids.option}-${i}` }));
    if (this.creatable && query && !options.some((o) => foldText(o.text) === foldText(query))) {
      view.push({ option: null, ranges: [], id: `${this.ids.option}-create` });
    }
    return view;
  }

  private firstEnabled(): number {
    return moveIndex(-1, 'first', this.view.map((v) => !!v.option?.disabled));
  }

  private choose(index: number): void {
    const entry = this.view[index];
    if (!entry) return;
    const value = entry.option ? entry.option.value : this.query.trim();
    if (entry.option?.disabled) return;

    if (this.multiple) {
      const key = entry.option?.key ?? valueKey(value);
      const current = this.values ?? [];
      const exists = current.some((v) => valueKey(v) === key);
      this.values = exists ? current.filter((v) => valueKey(v) !== key) : [...current, value];
      this.emit('sl-values-changed', { values: this.values });
      if (this.searchable && this.query) this.setQuery('');
      this.activeIndex = Math.min(index, Math.max(0, this.buildView().length - 1));
    } else {
      this.value = value;
      this.emit('sl-value-changed', { value });
      this.closeList();
    }
  }

  private removeValue(value: unknown): void {
    const key = valueKey(value);
    this.values = (this.values ?? []).filter((v) => valueKey(v) !== key);
    this.emit('sl-values-changed', { values: this.values });
    this.control?.focus();
  }

  /** Clears the selection (as the clear button does). */
  clear(): void {
    if (this.multiple) {
      this.values = [];
      this.emit('sl-values-changed', { values: [] });
    } else {
      this.value = undefined;
      this.emit('sl-value-changed', { value: undefined });
    }
    this.setQuery('');
    this.control?.focus();
  }

  private emit(type: string, detail: Record<string, unknown>): void {
    this.dispatchEvent(new CustomEvent(type, { bubbles: true, composed: true, detail }));
    this.dispatchEvent(new Event('input', { bubbles: true, composed: true }));
    this.dispatchEvent(new Event('change', { bubbles: true, composed: true }));
  }

  private setQuery(query: string): void {
    if (query === this.query) return;
    this.query = query;
    this.dispatchEvent(new CustomEvent('sl-search-changed', { bubbles: true, composed: true, detail: { query } }));
  }

  private closeList(): void {
    if (!this.open) return;
    this.open = false;
    this.activeIndex = -1;
    if (this.searchable && !this.multiple) this.setQuery('');
  }

  private syncForm(): void {
    if (!this.internals) return;
    if (this.multiple) {
      const data = new FormData();
      if (this.name) for (const v of this.values ?? []) data.append(this.name, valueKey(v));
      this.internals.setFormValue(data);
    } else {
      this.internals.setFormValue(this.hasValue() ? valueKey(this.value) : null);
    }
    const anchor = this.control ?? undefined;
    if (this.error) this.internals.setValidity({ customError: true }, this.error, anchor);
    else if (this.required && !this.hasValue()) this.internals.setValidity({ valueMissing: true }, 'Please choose an option.', anchor);
    else this.internals.setValidity({});
  }

  // ---- input -----------------------------------------------------------------------------------------------

  private onKeyDown(e: KeyboardEvent): void {
    if (this.disabled) return;
    const disabled = this.view.map((v) => !!v.option?.disabled);
    const keys: Record<string, ListKey> = { ArrowDown: 'next', ArrowUp: 'previous', PageDown: 'page-down', PageUp: 'page-up' };
    if (!this.searchable) Object.assign(keys, { Home: 'first', End: 'last' });

    if (keys[e.key]) {
      e.preventDefault();
      if (!this.open) {
        this.show();
        if (e.key === 'ArrowUp') this.activeIndex = moveIndex(-1, 'last', disabled);
        return;
      }
      this.activeIndex = moveIndex(this.activeIndex, keys[e.key], disabled, { pageSize: 8 });
      return;
    }

    switch (e.key) {
      case 'Enter':
        if (this.open && this.activeIndex >= 0) {
          e.preventDefault();
          this.choose(this.activeIndex);
        } else if (!this.open) {
          e.preventDefault();
          this.show();
        }
        return;
      case ' ':
        if (this.searchable) return;
        e.preventDefault();
        if (this.open && this.activeIndex >= 0) this.choose(this.activeIndex);
        else this.show();
        return;
      case 'Tab':
        this.closeList();
        return;
      case 'Backspace':
        if (this.searchable && this.multiple && !this.query && (this.values ?? []).length > 0) {
          this.removeValue(this.values[this.values.length - 1]);
        }
        return;
      default:
        break;
    }

    // Select-only typeahead: jump to the first matching option.
    if (!this.searchable && e.key.length === 1 && !e.ctrlKey && !e.metaKey && !e.altKey) {
      const found = this.typeahead.search(e.key, Date.now(), this.view.map((v) => v.option?.text ?? ''), this.activeIndex, disabled);
      if (found >= 0) {
        if (!this.open) this.show();
        this.activeIndex = found;
      }
    }
  }

  private onInput(e: Event): void {
    this.setQuery((e.target as HTMLInputElement).value);
    if (!this.open) this.open = true;
    this.activeIndex = this.firstEnabled();
  }

  private onControlClick(e: MouseEvent): void {
    if (this.disabled) return;
    if ((e.composedPath() as Element[]).some((el) => el instanceof HTMLElement && (el.classList.contains('sl-select__chip-remove') || el.classList.contains('sl-field__clear')))) return;
    if (this.searchable) {
      this.control?.focus();
      this.show();
    } else if (this.open) {
      this.closeList();
    } else {
      this.show();
    }
  }

  private onOptionPointerDown(e: PointerEvent): void {
    e.preventDefault(); // keep focus on the control
  }

  // ---- rendering ---------------------------------------------------------------------------------------------

  private renderChips(): TemplateResult[] {
    const values = this.values ?? [];
    const max = this.maxVisibleChips ?? values.length;
    const chips = values.slice(0, max).map((v) => {
      const text = this.textFor(v);
      return html`<span class="sl-select__chip" part="chip"
        ><span class="sl-select__chip-text">${text}</span
        ><button
          class="sl-select__chip-remove"
          type="button"
          tabindex="-1"
          aria-label=${`Remove ${text}`}
          ?disabled=${this.disabled}
          @click=${(e: Event) => {
            e.stopPropagation();
            this.removeValue(v);
          }}
        >
          ${renderIcon('x')}
        </button></span
      >`;
    });
    if (values.length > max) chips.push(html`<span class="sl-select__chip sl-select__chip--more" part="chip">+${values.length - max}</span>`);
    return chips;
  }

  private renderOption(entry: ViewOption, index: number, template: HTMLTemplateElement | null) {
    const option = entry.option;
    const active = index === this.activeIndex;
    if (!option) {
      return html`<div
        id=${entry.id}
        class=${classMap({ 'sl-select__option': true, 'sl-select__option--create': true, 'is-active': active })}
        part="option"
        role="option"
        aria-selected="false"
        @pointerdown=${this.onOptionPointerDown}
        @pointermove=${() => (this.activeIndex = index)}
        @click=${() => this.choose(index)}
      >
        <span class="sl-select__option-icon">${renderIcon('plus')}</span>
        <span class="sl-select__option-text"><span class="sl-select__option-label">Create “${this.query.trim()}”</span></span>
      </div>`;
    }
    const selected = this.isSelected(option);
    const custom = fromTemplate(template, option);
    return html`<div
      id=${entry.id}
      class=${classMap({ 'sl-select__option': true, 'is-active': active, 'is-selected': selected, 'is-disabled': option.disabled })}
      part="option"
      role="option"
      aria-selected=${selected ? 'true' : 'false'}
      aria-disabled=${option.disabled ? 'true' : nothing}
      @pointerdown=${this.onOptionPointerDown}
      @pointermove=${() => {
        if (!option.disabled) this.activeIndex = index;
      }}
      @click=${() => this.choose(index)}
    >
      ${this.multiple
        ? html`<span class="sl-select__option-box" aria-hidden="true">${renderIcon('check')}</span>`
        : html`<span class="sl-select__option-check" aria-hidden="true">${renderIcon('check')}</span>`}
      ${option.icon ? html`<span class="sl-select__option-icon">${renderIcon(option.icon)}</span>` : nothing}
      ${custom
        ? html`<span class="sl-select__option-text">${custom}</span>`
        : html`<span class="sl-select__option-text"
            ><span class="sl-select__option-label">${highlight(option.text, entry.ranges)}</span
            >${option.description ? html`<span class="sl-select__option-description">${option.description}</span>` : nothing}</span
          >`}
    </div>`;
  }

  private renderListbox() {
    const template = this.querySelector<HTMLTemplateElement>(':scope > template[slot="item-template"]');
    const groups = groupIndices(this.view.map((v) => v.option?.group ?? ''));
    const hasGroups = groups.some((g) => g.key !== '');
    const body = hasGroups
      ? groups.map(
          (g, gi) => html`<div class="sl-select__group" part="group" role="group" aria-labelledby=${`${this.ids.listbox}-g${gi}`}>
            ${g.key ? html`<div class="sl-select__group-label" id=${`${this.ids.listbox}-g${gi}`}>${g.key}</div>` : nothing}
            ${g.indices.map((i) => this.renderOption(this.view[i], i, template))}
          </div>`,
        )
      : this.view.map((v, i) => this.renderOption(v, i, template));

    return html`<div class="sl-popover-panel sl-select__panel" hidden>
      <div
        id=${this.ids.listbox}
        class="sl-select__listbox"
        part="listbox"
        role="listbox"
        aria-label=${this.label ?? 'Options'}
        aria-multiselectable=${this.multiple ? 'true' : nothing}
      >
        ${body}
        ${this.loading
          ? html`<div class="sl-select__empty" role="status">Loading…</div>`
          : this.view.length === 0
            ? html`<div class="sl-select__empty" part="empty"><slot name="empty-content">No results</slot></div>`
            : nothing}
      </div>
      <slot name="item-template" hidden></slot>
    </div>`;
  }

  override render() {
    const { variant, size, radius } = this.resolved;
    const invalid = !!this.error;
    const active = this.open && this.activeIndex >= 0 ? this.view[this.activeIndex]?.id : undefined;
    const describedBy = invalid || this.helperText ? this.ids.desc : undefined;
    const showClear = this.clearable && this.hasValue() && !this.disabled;
    const classes = {
      'sl-field': true,
      'sl-select': true,
      [`sl-field--${variant}`]: true,
      [`sl-field--${size}`]: size !== 'medium',
      [`sl-radius-${radius}`]: radius !== 'default',
      'sl-select--multiple': this.multiple,
      'sl-select--searchable': this.searchable,
      'is-open': this.open,
      'sl-field--invalid': invalid,
      'sl-field--disabled': this.disabled,
    };

    const single = !this.multiple && this.hasValue() ? this.textFor(this.value) : '';
    const placeholder = this.placeholder ?? '';
    const common = {
      role: 'combobox',
      expanded: this.open ? 'true' : 'false',
    };

    const control = this.searchable
      ? html`<span class="sl-select__value" part="value">
          ${this.multiple ? this.renderChips() : nothing}
          <input
            id=${this.ids.control}
            class="sl-field__input sl-select__input"
            role=${common.role}
            aria-autocomplete="list"
            aria-expanded=${common.expanded}
            aria-controls=${this.ids.listbox}
            aria-activedescendant=${ifDefined(active)}
            aria-invalid=${invalid ? 'true' : 'false'}
            aria-describedby=${ifDefined(describedBy)}
            aria-required=${this.required ? 'true' : nothing}
            autocomplete="off"
            .value=${live(this.open || this.multiple ? this.query : single)}
            placeholder=${this.multiple ? ((this.values ?? []).length ? '' : placeholder) : this.open ? single || placeholder : placeholder}
            ?disabled=${this.disabled}
            @input=${this.onInput}
            @keydown=${this.onKeyDown}
          />
        </span>`
      : html`<button
          id=${this.ids.control}
          class="sl-select__trigger"
          type="button"
          role=${common.role}
          aria-haspopup="listbox"
          aria-expanded=${common.expanded}
          aria-controls=${this.ids.listbox}
          aria-activedescendant=${ifDefined(active)}
          aria-labelledby=${ifDefined(this.label ? this.ids.label : undefined)}
          aria-invalid=${invalid ? 'true' : 'false'}
          aria-describedby=${ifDefined(describedBy)}
          aria-required=${this.required ? 'true' : nothing}
          ?disabled=${this.disabled}
          @keydown=${this.onKeyDown}
        >
          <span class="sl-select__value" part="value">
            ${this.multiple
              ? (this.values ?? []).length
                ? this.renderChips()
                : html`<span class="sl-select__placeholder">${placeholder}</span>`
              : single
                ? html`<span class="sl-select__text">${single}</span>`
                : html`<span class="sl-select__placeholder">${placeholder}</span>`}
          </span>
        </button>`;

    return html`<div class=${classMap(classes)} part="base">
      ${this.label
        ? html`<label class="sl-field__label" part="label" id=${this.ids.label} for=${this.ids.control} @click=${() => this.control?.focus()}
            >${this.label}${this.required ? html`<span class="sl-field__required" aria-hidden="true">*</span>` : nothing}</label
          >`
        : nothing}
      <div class="sl-field__control sl-select__control" part="control" @click=${this.onControlClick}>
        ${control}
        ${this.loading ? html`<span class="sl-select__spinner"><span class="sl-spinner sl-spinner--small" aria-hidden="true"></span></span>` : nothing}
        ${showClear
          ? html`<button class="sl-field__clear" type="button" aria-label="Clear" @click=${(e: Event) => (e.stopPropagation(), this.clear())}>
              ${renderIcon('x')}
            </button>`
          : nothing}
        <span class="sl-select__chevron" aria-hidden="true">${renderIcon('chevron-down')}</span>
      </div>
      ${invalid || this.helperText
        ? html`<div class="sl-field__footer">
            ${invalid
              ? html`<p class="sl-field__error" id=${this.ids.desc}>${renderIcon('alert-circle')}${this.error}</p>`
              : html`<p class="sl-field__helper" id=${this.ids.desc}>${this.helperText}</p>`}
          </div>`
        : nothing}
      ${this.renderListbox()}
    </div>`;
  }
}
