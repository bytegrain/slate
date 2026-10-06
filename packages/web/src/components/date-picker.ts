import { LitElement, css, html, nothing, type PropertyValues } from 'lit';
import { classMap } from 'lit/directives/class-map.js';
import { ifDefined } from 'lit/directives/if-defined.js';
import { live } from 'lit/directives/live.js';
import { hostReset, styles } from '../internal/styles';
import { renderIcon } from '../internal/icon';
import { uid } from '../internal/dom';
import { attachFormInternals, owningForm, watchFormReset } from '../internal/form';
import { Overlay } from '../internal/overlay';
import {
  addMonths,
  buildMonth,
  firstDayOfWeek as cultureFirstDay,
  formatDate,
  navigateCalendar,
  parseDate,
  pickRange,
  resolvePreset,
  shortPattern,
  type CalendarKey,
  type CalendarOptions,
  type DatePreset,
  type DateRange,
  type IsoDate,
  type Weekday,
} from '../core/date/calendar';
import { DefaultsController, getDefaults, type ControlSize, type FieldVariant } from '../core/defaults';

export type DateSelection = 'single' | 'range';
export type { DateRange, DatePreset, IsoDate };

const calendarKeys: Record<string, CalendarKey> = {
  ArrowLeft: 'left',
  ArrowRight: 'right',
  ArrowUp: 'up',
  ArrowDown: 'down',
  Home: 'home',
  End: 'end',
  PageUp: 'page-up',
  PageDown: 'page-down',
};



function todayIso(): IsoDate {
  const d = new Date();
  return `${String(d.getFullYear()).padStart(4, '0')}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}

function ym(iso: IsoDate): [number, number] {
  return [Number(iso.slice(0, 4)), Number(iso.slice(5, 7))];
}

function locale(): string {
  return (typeof navigator !== 'undefined' && navigator.language) || 'en-US';
}

/**
 * <sl-date-picker label="Due date" value="2026-10-06" min="2026-01-01" clearable></sl-date-picker>
 * <sl-date-picker selection="range" presets='[{"label":"Last 7 days","kind":"last7-days"}]'></sl-date-picker>
 * <sl-date-picker inline></sl-date-picker>
 *
 * Dates are ISO strings (yyyy-MM-dd). Typing is lenient (ISO or the culture's short date, `format` to override).
 * Calendar keyboard (WAI-ARIA grid): arrows, Home/End (week), PageUp/PageDown (month), Shift+Page (year),
 * Enter/Space pick; disabled days (min/max/disabledDates) are skipped. Form-associated: single → "yyyy-MM-dd",
 * range → "start/end". Events: `sl-value-changed` (detail: { value }), `sl-range-changed` (detail: { range }).
 */
export class SlDatePicker extends LitElement {
  static formAssociated = true;
  static override shadowRootOptions = { ...LitElement.shadowRootOptions, delegatesFocus: true };

  static override properties = {
    value: { reflect: true },
    range: { type: Object },
    selection: { reflect: true },
    min: {},
    max: {},
    disabledDates: { attribute: false },
    firstDayOfWeek: { type: Number, attribute: 'first-day-of-week' },
    format: {},
    presets: { type: Array },
    label: {},
    placeholder: {},
    helperText: { attribute: 'helper-text' },
    error: {},
    variant: { reflect: true },
    size: { reflect: true },
    clearable: { type: Boolean, reflect: true },
    disabled: { type: Boolean, reflect: true },
    inline: { type: Boolean, reflect: true },
    name: { reflect: true },
    open: { type: Boolean, reflect: true },
    view: { state: true },
    focused: { state: true },
    hover: { state: true },
    pending: { state: true },
    text: { state: true },
  };

  static override styles = [
    hostReset,
    styles.base,
    styles.button,
    styles.tone,
    styles.field,
    styles.overlay,
    styles.calendar,
    css`
      :host { display: block; }
      :host([inline]) { display: inline-block; }
    `,
  ];

  declare value: IsoDate | null | undefined;
  declare range: DateRange | null | undefined;
  declare selection: DateSelection;
  declare min: IsoDate | undefined;
  declare max: IsoDate | undefined;
  declare disabledDates: ((date: IsoDate) => boolean) | undefined;
  /** 0 = Sunday … 6 = Saturday; unset follows the culture. */
  declare firstDayOfWeek: Weekday | undefined;
  /** Display/typing pattern (tokens yyyy, yy, MM, M, dd, d); unset follows the culture's short date. */
  declare format: string | undefined;
  declare presets: DatePreset[] | undefined;
  declare label: string | undefined;
  declare placeholder: string | undefined;
  declare helperText: string | undefined;
  declare error: string | undefined;
  declare variant: FieldVariant | undefined;
  declare size: ControlSize | undefined;
  declare clearable: boolean;
  declare disabled: boolean;
  declare inline: boolean;
  declare name: string | undefined;
  declare open: boolean;
  declare view: { year: number; month: number };
  declare focused: IsoDate;
  declare hover: IsoDate | null;
  declare pending: DateRange | null;
  declare text: string | null;

  private readonly internals: ElementInternals | undefined;
  private readonly ids = { input: uid('sl-date'), title: uid('sl-date-title'), desc: uid('sl-date-desc'), panel: uid('sl-date-panel') };
  private defaults: { value: IsoDate | null | undefined; range: DateRange | null | undefined } = { value: undefined, range: undefined };
  private stopWatchingReset: () => void = () => {};
  private focusDayAfterUpdate = false;

  private readonly overlay = new Overlay({
    anchor: () => this.renderRoot?.querySelector('.sl-field__control') ?? null,
    panel: () => (this.inline ? null : (this.renderRoot?.querySelector<HTMLElement>('.sl-date-picker__panel') ?? null)),
    placement: () => 'bottom-start',
    offset: () => 4,
    onDismiss: (reason) => this.closePanel(reason === 'escape'),
  });

  constructor() {
    super();
    new DefaultsController(this);
    this.selection = 'single';
    this.clearable = false;
    this.disabled = false;
    this.inline = false;
    this.open = false;
    this.hover = null;
    this.pending = null;
    this.text = null;
    const today = todayIso();
    this.focused = today;
    const [y, m] = ym(today);
    this.view = { year: y, month: m };
    this.internals = attachFormInternals(this);
  }

  get resolved(): { variant: FieldVariant; size: ControlSize; selection: DateSelection } {
    const d = getDefaults().field;
    return { variant: this.variant ?? d.variant, size: this.size ?? d.size, selection: this.selection };
  }

  get form(): HTMLFormElement | null {
    return owningForm(this, this.internals);
  }

  get pattern(): string {
    return this.format ?? shortPattern(locale());
  }

  private get weekStart(): Weekday {
    return this.firstDayOfWeek ?? cultureFirstDay(locale());
  }

  private get calendarOptions(): CalendarOptions {
    return {
      firstDayOfWeek: this.weekStart,
      today: todayIso(),
      min: this.min ?? null,
      max: this.max ?? null,
      isDateDisabled: this.disabledDates,
      selected: this.selection === 'single' ? (this.value ?? null) : null,
      range: this.selection === 'range' ? (this.pending ?? this.range ?? null) : null,
      hover: this.hover,
    };
  }

  override connectedCallback(): void {
    super.connectedCallback();
    this.defaults = { value: this.value, range: this.range };
    this.stopWatchingReset = watchFormReset(this, this.internals);
    this.jumpTo(this.anchorDate());
  }

  override disconnectedCallback(): void {
    super.disconnectedCallback();
    this.stopWatchingReset();
    this.overlay.close();
  }

  formResetCallback(): void {
    this.value = this.defaults.value;
    this.range = this.defaults.range;
    this.text = null;
  }

  formDisabledCallback(disabled: boolean): void {
    this.disabled = disabled;
  }

  override focus(options?: FocusOptions): void {
    this.renderRoot?.querySelector<HTMLInputElement>('.sl-date-picker__input')?.focus(options);
  }

  /** Opens the calendar (no-op when inline). */
  show(): void {
    if (this.disabled || this.inline || this.open) return;
    this.jumpTo(this.anchorDate());
    this.pending = null;
    this.open = true;
    this.focusDayAfterUpdate = true;
  }

  hide(): void {
    this.closePanel(false);
  }

  protected override updated(changed: PropertyValues<this>): void {
    if (changed.has('open') && !this.inline) {
      if (this.open) this.overlay.open();
      else this.overlay.close();
    }
    if ((changed.has('value') || changed.has('range')) && !this.open) this.jumpTo(this.anchorDate());
    if (this.focusDayAfterUpdate) {
      this.focusDayAfterUpdate = false;
      requestAnimationFrame(() => this.renderRoot.querySelector<HTMLButtonElement>('.sl-calendar__day[tabindex="0"]')?.focus());
    }
    this.syncForm();
  }

  private anchorDate(): IsoDate {
    if (this.selection === 'range') return this.range?.start ?? todayIso();
    return this.value ?? todayIso();
  }

  private jumpTo(date: IsoDate): void {
    this.focused = date;
    const [y, m] = ym(date);
    if (this.view?.year !== y || this.view?.month !== m) this.view = { year: y, month: m };
  }

  private closePanel(returnFocus: boolean): void {
    if (!this.open) return;
    this.open = false;
    this.hover = null;
    this.pending = null;
    if (returnFocus) this.focus();
  }

  private displayText(): string {
    const p = this.pattern;
    if (this.selection === 'range') {
      const r = this.range;
      if (!r?.start) return '';
      return r.end ? `${formatDate(r.start, p)} – ${formatDate(r.end, p)}` : formatDate(r.start, p);
    }
    return this.value ? formatDate(this.value, p) : '';
  }

  private isAllowed(date: IsoDate): boolean {
    if (this.min && date < this.min) return false;
    if (this.max && date > this.max) return false;
    return !(this.disabledDates?.(date) ?? false);
  }

  private setValue(value: IsoDate | null): void {
    this.value = value;
    this.dispatchEvent(new CustomEvent('sl-value-changed', { bubbles: true, composed: true, detail: { value } }));
    this.dispatchEvent(new Event('change', { bubbles: true, composed: true }));
  }

  private setRange(range: DateRange | null): void {
    this.range = range;
    this.dispatchEvent(new CustomEvent('sl-range-changed', { bubbles: true, composed: true, detail: { range } }));
    this.dispatchEvent(new Event('change', { bubbles: true, composed: true }));
  }

  /** Picks a day as if clicked. */
  pick(date: IsoDate): void {
    if (!this.isAllowed(date)) return;
    this.focused = date;
    if (this.selection === 'single') {
      this.setValue(date);
      this.text = null;
      this.closePanel(true);
      return;
    }
    const next = pickRange(this.pending, date);
    if (next.end == null) {
      this.pending = next;
    } else {
      this.pending = null;
      this.setRange(next);
      this.text = null;
      this.closePanel(true);
    }
  }

  private applyPreset(preset: DatePreset): void {
    const r = resolvePreset(preset.kind, todayIso());
    if (this.selection === 'range') this.setRange({ start: r.start, end: r.end });
    else this.setValue(r.start);
    this.jumpTo(r.start);
    this.text = null;
    this.closePanel(true);
  }

  clear(): void {
    if (this.selection === 'range') this.setRange(null);
    else this.setValue(null);
    this.text = null;
    this.focus();
  }

  private commitText(): void {
    const text = this.text;
    if (text === null) return;
    this.text = null;
    const p = this.pattern;
    if (!text.trim()) {
      if (this.selection === 'range') this.setRange(null);
      else this.setValue(null);
      return;
    }
    if (this.selection === 'range') {
      const parts = text.split(/\s*[–—]\s*|\s+-\s+|\s+to\s+/i);
      const start = parseDate(parts[0], p);
      const end = parts[1] ? parseDate(parts[1], p) : null;
      if (start && this.isAllowed(start) && (!end || (this.isAllowed(end) && end >= start))) this.setRange({ start, end: end ?? start });
      return;
    }
    const date = parseDate(text, p);
    if (date && this.isAllowed(date) && date !== this.value) this.setValue(date);
  }

  private syncForm(): void {
    if (!this.internals) return;
    const v = this.selection === 'range' ? (this.range?.start ? `${this.range.start}/${this.range.end ?? this.range.start}` : null) : (this.value ?? null);
    this.internals.setFormValue(v);
    const anchor = this.renderRoot?.querySelector<HTMLElement>('.sl-date-picker__input') ?? undefined;
    if (this.error) this.internals.setValidity({ customError: true }, this.error, anchor);
    else this.internals.setValidity({});
  }

  private onInputKeyDown(e: KeyboardEvent): void {
    if (e.key === 'Enter') {
      e.preventDefault();
      this.commitText();
    } else if (e.key === 'ArrowDown' && (e.altKey || !this.open)) {
      e.preventDefault();
      this.show();
    }
  }

  private onGridKeyDown(e: KeyboardEvent): void {
    const key = calendarKeys[e.key];
    if (key) {
      e.preventDefault();
      const next = navigateCalendar(this.focused, key, e.shiftKey, this.calendarOptions);
      this.jumpTo(next);
      if (this.selection === 'range' && this.pending) this.hover = next;
      this.focusDayAfterUpdate = true;
    } else if (e.key === 'Enter' || e.key === ' ') {
      e.preventDefault();
      this.pick(this.focused);
    }
  }

  private shiftMonth(delta: number): void {
    const target = addMonths(`${String(this.view.year).padStart(4, '0')}-${String(this.view.month).padStart(2, '0')}-01`, delta);
    const [y, m] = ym(target);
    this.view = { year: y, month: m };
    this.focused = target;
  }

  private renderCalendar() {
    const options = this.calendarOptions;
    const month = buildMonth(this.view.year, this.view.month, options);
    const loc = locale();
    const weekday = new Intl.DateTimeFormat(loc, { weekday: 'short', timeZone: 'UTC' });
    const weekdayLong = new Intl.DateTimeFormat(loc, { weekday: 'long', timeZone: 'UTC' });
    const title = new Intl.DateTimeFormat(loc, { month: 'long', year: 'numeric', timeZone: 'UTC' }).format(Date.UTC(this.view.year, this.view.month - 1, 1));
    const dayLabel = new Intl.DateTimeFormat(loc, { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric', timeZone: 'UTC' });
    const utc = (iso: IsoDate) => Date.UTC(Number(iso.slice(0, 4)), Number(iso.slice(5, 7)) - 1, Number(iso.slice(8, 10)));
    // 2023-01-01 was a Sunday, so day w of that week is weekday w.
    const weekdayDate = (w: Weekday) => Date.UTC(2023, 0, 1 + w);

    return html`<div class="sl-calendar">
      ${this.presets?.length
        ? html`<div class="sl-calendar__presets" part="presets" role="group" aria-label="Presets">
            ${this.presets.map((p) => html`<button class="sl-calendar__preset" type="button" @click=${() => this.applyPreset(p)}>${p.label}</button>`)}
          </div>`
        : nothing}
      <div class="sl-calendar__main">
        <div class="sl-calendar__header" part="header">
          <button class="sl-button sl-button--ghost sl-tone-neutral sl-button--small sl-button--icon-only" type="button" aria-label="Previous month" @click=${() => this.shiftMonth(-1)}>
            ${renderIcon('chevron-left')}
          </button>
          <span class="sl-calendar__title" id=${this.ids.title} aria-live="polite">${title}</span>
          <button class="sl-button sl-button--ghost sl-tone-neutral sl-button--small sl-button--icon-only" type="button" aria-label="Next month" @click=${() => this.shiftMonth(1)}>
            ${renderIcon('chevron-right')}
          </button>
        </div>
        <table class="sl-calendar__grid" role="grid" part="grid" aria-labelledby=${this.ids.title} @keydown=${this.onGridKeyDown}>
          <thead>
            <tr>
              ${month.weekdays.map((w) => html`<th class="sl-calendar__weekday" scope="col" abbr=${weekdayLong.format(weekdayDate(w))}>${weekday.format(weekdayDate(w)).slice(0, 2)}</th>`)}
            </tr>
          </thead>
          <tbody @pointerleave=${() => (this.hover = null)}>
            ${month.weeks.map(
              (week) => html`<tr>
                ${week.days.map((day) => {
                  const classes = {
                    'sl-calendar__day': true,
                    'is-outside': !day.inMonth,
                    'is-today': day.isToday,
                    'is-selected': day.isSelected,
                    'is-range-start': day.isRangeStart,
                    'is-range-end': day.isRangeEnd,
                    'is-in-range': day.inRange && !day.isSelected,
                    'is-in-preview': day.inPreview && !day.isSelected,
                  };
                  return html`<td class="sl-calendar__cell" role="gridcell" aria-selected=${day.isSelected ? 'true' : 'false'}>
                    <button
                      class=${classMap(classes)}
                      part="day"
                      type="button"
                      tabindex=${day.date === this.focused ? 0 : -1}
                      data-date=${day.date}
                      aria-label=${dayLabel.format(utc(day.date))}
                      aria-current=${day.isToday ? 'date' : nothing}
                      ?disabled=${day.isDisabled}
                      @click=${() => this.pick(day.date)}
                      @pointerenter=${() => {
                        if (this.selection === 'range' && this.pending) this.hover = day.date;
                      }}
                    >
                      ${Number(day.date.slice(8, 10))}
                    </button>
                  </td>`;
                })}
              </tr>`,
            )}
          </tbody>
        </table>
      </div>
    </div>`;
  }

  override render() {
    const { variant, size } = this.resolved;
    const invalid = !!this.error;
    const describedBy = invalid || this.helperText ? this.ids.desc : undefined;
    const hasValue = this.selection === 'range' ? !!this.range?.start : !!this.value;
    const classes = {
      'sl-field': true,
      'sl-date-picker': true,
      [`sl-field--${variant}`]: true,
      [`sl-field--${size}`]: size !== 'medium',
      [`sl-date-picker--${this.selection}`]: true,
      'sl-date-picker--inline': this.inline,
      'is-open': this.open,
      'sl-field--invalid': invalid,
      'sl-field--disabled': this.disabled,
    };
    const placeholder = this.placeholder ?? (this.selection === 'range' ? `${this.pattern} – ${this.pattern}` : this.pattern);

    if (this.inline) {
      return html`<div class=${classMap(classes)} part="field">
        ${this.label ? html`<span class="sl-field__label" part="label">${this.label}</span>` : nothing}
        <div class="sl-date-picker__panel" part="panel" role="group" aria-label=${this.label ?? 'Calendar'}>${this.renderCalendar()}</div>
      </div>`;
    }

    return html`<div class=${classMap(classes)} part="field">
      ${this.label
        ? html`<label class="sl-field__label" part="label" for=${this.ids.input}
            >${this.label}</label
          >`
        : nothing}
      <div class="sl-field__control" part="control">
        <input
          id=${this.ids.input}
          class="sl-field__input sl-date-picker__input"
          type="text"
          inputmode="numeric"
          autocomplete="off"
          .value=${live(this.text ?? this.displayText())}
          placeholder=${placeholder}
          aria-invalid=${invalid ? 'true' : 'false'}
          aria-describedby=${ifDefined(describedBy)}
          aria-haspopup="dialog"
          aria-expanded=${this.open ? 'true' : 'false'}
          ?disabled=${this.disabled}
          @input=${(e: Event) => (this.text = (e.target as HTMLInputElement).value)}
          @change=${this.commitText}
          @blur=${this.commitText}
          @keydown=${this.onInputKeyDown}
        />
        ${this.clearable && hasValue && !this.disabled
          ? html`<button class="sl-field__clear" type="button" aria-label="Clear" @click=${this.clear}>${renderIcon('x')}</button>`
          : nothing}
        <button
          class="sl-date-picker__trigger"
          type="button"
          aria-label=${this.open ? 'Close calendar' : 'Open calendar'}
          ?disabled=${this.disabled}
          @click=${() => (this.open ? this.closePanel(true) : this.show())}
        >
          ${renderIcon('calendar')}
        </button>
      </div>
      ${invalid || this.helperText
        ? html`<div class="sl-field__footer">
            ${invalid
              ? html`<p class="sl-field__error" id=${this.ids.desc}>${renderIcon('alert-circle')}${this.error}</p>`
              : html`<p class="sl-field__helper" id=${this.ids.desc}>${this.helperText}</p>`}
          </div>`
        : nothing}
      <div id=${this.ids.panel} class="sl-popover-panel sl-date-picker__panel" part="panel" role="dialog" aria-modal="false" aria-label=${this.label ?? 'Choose date'} hidden>
        ${this.renderCalendar()}
      </div>
    </div>`;
  }
}
