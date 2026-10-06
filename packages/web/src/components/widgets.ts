import { LitElement, css, html, nothing, type PropertyValues } from 'lit';
import { classMap } from 'lit/directives/class-map.js';
import { styleMap } from 'lit/directives/style-map.js';
import { hostReset, styles } from '../internal/styles';
import { renderIcon } from '../internal/icon';
import { defaultTrue, uid } from '../internal/dom';
import { attachFormInternals, owningForm, watchFormReset } from '../internal/form';
import { normalizeItems, valueKey } from '../internal/items';
import { moveIndex, type ListKey } from '../core/collections/list';
import {
  avatarInitials,
  avatarTone,
  fractionToValue,
  pageCount as computePageCount,
  pageForFirstItem,
  paginationRange,
  setRangeThumb,
  sliderKey,
  snapValue,
  valueToFraction,
  type SliderKey,
} from '../core/collections/widgets';
import type { ControlSize, Tone } from '../core/defaults';

const segmentKeys: Record<string, ListKey> = { ArrowRight: 'next', ArrowDown: 'next', ArrowLeft: 'previous', ArrowUp: 'previous', Home: 'first', End: 'last' };

/**
 * <sl-segmented .items=${['Day', 'Week', 'Month']} value="Week" size="small" full-width></sl-segmented>
 * A radio group drawn as a segmented control (one tab stop, arrows move and select).
 * Items: strings or { value, label, icon, disabled }. Fires `sl-value-changed` (detail: { value }) and `change`.
 */
export class SlSegmented extends LitElement {
  static override properties = {
    items: { type: Array },
    value: {},
    itemText: { attribute: false },
    itemIcon: { attribute: false },
    size: { reflect: true },
    fullWidth: { type: Boolean, attribute: 'full-width', reflect: true },
    disabled: { type: Boolean, reflect: true },
    label: {},
  };

  static override styles = [
    hostReset,
    styles.base,
    styles.navigation,
    css`
      :host { display: inline-block; }
      :host([full-width]) { display: block; }
    `,
  ];

  declare items: unknown[];
  declare value: unknown;
  declare itemText: ((item: unknown) => string) | undefined;
  declare itemIcon: ((item: unknown) => string | null | undefined) | undefined;
  declare size: ControlSize;
  declare fullWidth: boolean;
  declare disabled: boolean;
  /** Accessible name for the group. */
  declare label: string | undefined;

  constructor() {
    super();
    this.items = [];
    this.size = 'medium';
    this.fullWidth = false;
    this.disabled = false;
  }

  private get options() {
    return normalizeItems(this.items, { itemText: this.itemText, itemIcon: this.itemIcon });
  }

  private choose(value: unknown, focus = false): void {
    const key = valueKey(value);
    if (key !== valueKey(this.value)) {
      this.value = value;
      this.dispatchEvent(new CustomEvent('sl-value-changed', { bubbles: true, composed: true, detail: { value } }));
      this.dispatchEvent(new Event('change', { bubbles: true, composed: true }));
    }
    if (focus) this.updateComplete.then(() => this.renderRoot.querySelector<HTMLElement>('[aria-checked="true"]')?.focus());
  }

  private onKeyDown(e: KeyboardEvent): void {
    const key = segmentKeys[e.key];
    if (!key || this.disabled) return;
    e.preventDefault();
    const options = this.options;
    const current = options.findIndex((o) => o.key === valueKey(this.value));
    const next = moveIndex(current, key, options.map((o) => o.disabled), { wrap: true });
    if (next >= 0) this.choose(options[next].value, true);
  }

  override render() {
    const options = this.options;
    const selectedKey = valueKey(this.value);
    const tabStop = options.some((o) => o.key === selectedKey) ? selectedKey : options.find((o) => !o.disabled)?.key;
    const classes = {
      'sl-segmented': true,
      [`sl-segmented--${this.size}`]: this.size !== 'medium',
      'sl-segmented--full': this.fullWidth,
      'is-disabled': this.disabled,
    };
    return html`<div class=${classMap(classes)} part="base" role="radiogroup" aria-label=${this.label ?? nothing} aria-disabled=${this.disabled ? 'true' : nothing} @keydown=${this.onKeyDown}>
      ${options.map(
        (o) => html`<button
          class=${classMap({ 'sl-segmented__item': true, 'is-selected': o.key === selectedKey })}
          part="item"
          type="button"
          role="radio"
          aria-checked=${o.key === selectedKey ? 'true' : 'false'}
          tabindex=${o.key === tabStop ? 0 : -1}
          ?disabled=${this.disabled || o.disabled}
          @click=${() => this.choose(o.value)}
        >
          ${o.icon ? renderIcon(o.icon) : nothing}${o.text}
        </button>`,
      )}
    </div>`;
  }
}

const sliderKeys: Record<string, SliderKey> = {
  ArrowRight: 'increase',
  ArrowUp: 'increase',
  ArrowLeft: 'decrease',
  ArrowDown: 'decrease',
  PageUp: 'page-increase',
  PageDown: 'page-decrease',
  Home: 'home',
  End: 'end',
};

/**
 * <sl-slider label="Bandwidth" min="0" max="1000" step="10" value="420" show-value ticks></sl-slider>
 * <sl-slider label="Price" value="20" range-end="80"></sl-slider>   (two thumbs; they never cross)
 *
 * Thumbs are ARIA sliders: arrows step, PageUp/PageDown move 10 %, Home/End. Dragging snaps to `step`.
 * Form-associated (a range submits two entries). Fires `input` while dragging, `change` on release and
 * `sl-value-changed` (detail: { value, rangeEnd }).
 */
export class SlSlider extends LitElement {
  static formAssociated = true;

  static override properties = {
    value: { type: Number },
    rangeEnd: { type: Number, attribute: 'range-end' },
    min: { type: Number },
    max: { type: Number },
    step: { type: Number },
    ticks: { type: Boolean, reflect: true },
    showValue: { type: Boolean, attribute: 'show-value', reflect: true },
    label: {},
    tone: { reflect: true },
    disabled: { type: Boolean, reflect: true },
    name: { reflect: true },
    dragging: { state: true },
  };

  static override styles = [hostReset, styles.base, styles.tone, styles.widgets, css`:host { display: block; }`];

  declare value: number;
  declare rangeEnd: number | undefined;
  declare min: number;
  declare max: number;
  declare step: number;
  declare ticks: boolean;
  declare showValue: boolean;
  declare label: string | undefined;
  declare tone: Tone;
  declare disabled: boolean;
  declare name: string | undefined;
  declare dragging: number;

  private readonly internals: ElementInternals | undefined;
  private readonly labelId = uid('sl-slider-label');
  private defaults: { value: number; rangeEnd: number | undefined } = { value: 0, rangeEnd: undefined };
  private stopWatchingReset: () => void = () => {};

  constructor() {
    super();
    this.value = 0;
    this.min = 0;
    this.max = 100;
    this.step = 1;
    this.ticks = false;
    this.showValue = false;
    this.tone = 'accent';
    this.disabled = false;
    this.dragging = -1;
    this.internals = attachFormInternals(this);
  }

  get isRange(): boolean {
    return this.rangeEnd !== undefined && this.rangeEnd !== null && !Number.isNaN(this.rangeEnd);
  }

  get form(): HTMLFormElement | null {
    return owningForm(this, this.internals);
  }

  override connectedCallback(): void {
    super.connectedCallback();
    this.defaults = { value: this.value, rangeEnd: this.rangeEnd };
    this.stopWatchingReset = watchFormReset(this, this.internals);
  }

  override disconnectedCallback(): void {
    super.disconnectedCallback();
    this.stopWatchingReset();
  }

  formResetCallback(): void {
    this.value = this.defaults.value;
    this.rangeEnd = this.defaults.rangeEnd;
  }

  formDisabledCallback(disabled: boolean): void {
    this.disabled = disabled;
  }

  override focus(options?: FocusOptions): void {
    this.renderRoot?.querySelector<HTMLElement>('.sl-slider__thumb')?.focus(options);
  }

  protected override updated(): void {
    if (!this.internals) return;
    if (this.isRange && this.name) {
      const data = new FormData();
      data.append(this.name, String(this.value));
      data.append(this.name, String(this.rangeEnd));
      this.internals.setFormValue(data);
    } else {
      this.internals.setFormValue(String(this.value));
    }
  }

  private setThumb(thumb: 0 | 1, raw: number, commit: boolean): void {
    let value = this.value;
    let end = this.rangeEnd;
    if (this.isRange) {
      [value, end] = setRangeThumb([this.value, this.rangeEnd!], thumb, raw, this.min, this.max, this.step);
    } else {
      value = snapValue(raw, this.min, this.max, this.step);
    }
    const changed = value !== this.value || end !== this.rangeEnd;
    this.value = value;
    if (this.isRange) this.rangeEnd = end;
    if (changed) {
      this.dispatchEvent(new Event('input', { bubbles: true, composed: true }));
      this.dispatchEvent(new CustomEvent('sl-value-changed', { bubbles: true, composed: true, detail: { value: this.value, rangeEnd: this.rangeEnd } }));
    }
    if (commit && changed) this.dispatchEvent(new Event('change', { bubbles: true, composed: true }));
  }

  private onThumbKeyDown(thumb: 0 | 1, e: KeyboardEvent): void {
    const key = sliderKeys[e.key];
    if (!key || this.disabled) return;
    e.preventDefault();
    const current = thumb === 0 ? this.value : this.rangeEnd!;
    this.setThumb(thumb, sliderKey(current, key, this.min, this.max, this.step), true);
  }

  private valueAt(e: PointerEvent): number {
    const track = this.renderRoot.querySelector<HTMLElement>('.sl-slider__track')!;
    const rect = track.getBoundingClientRect();
    const fraction = rect.width > 0 ? (e.clientX - rect.left) / rect.width : 0;
    return fractionToValue(Math.min(1, Math.max(0, fraction)), this.min, this.max, this.step);
  }

  private onPointerDown(e: PointerEvent): void {
    if (this.disabled || e.button !== 0) return;
    const value = this.valueAt(e);
    const thumb: 0 | 1 = this.isRange && Math.abs(value - this.rangeEnd!) < Math.abs(value - this.value) ? 1 : 0;
    this.dragging = thumb;
    const track = e.currentTarget as HTMLElement;
    track.setPointerCapture?.(e.pointerId);
    this.setThumb(thumb, value, false);
    this.updateComplete.then(() => this.renderRoot.querySelectorAll<HTMLElement>('.sl-slider__thumb')[thumb]?.focus());
  }

  private onPointerMove(e: PointerEvent): void {
    if (this.dragging < 0) return;
    this.setThumb(this.dragging as 0 | 1, this.valueAt(e), false);
  }

  private onPointerUp(): void {
    if (this.dragging < 0) return;
    this.dragging = -1;
    this.dispatchEvent(new Event('change', { bubbles: true, composed: true }));
  }

  private tickPositions(): number[] {
    const span = this.max - this.min;
    if (span <= 0 || this.step <= 0) return [];
    const count = Math.round(span / this.step);
    if (count <= 50) return Array.from({ length: count + 1 }, (_, i) => i / count);
    return Array.from({ length: 11 }, (_, i) => i / 10);
  }

  override render() {
    const f = (v: number) => valueToFraction(v, this.min, this.max);
    const start = this.isRange ? f(this.value) : 0;
    const end = this.isRange ? f(this.rangeEnd!) : f(this.value);
    const display = this.isRange ? `${this.value} – ${this.rangeEnd}` : String(this.value);
    const thumbs: Array<{ value: number; name: string }> = this.isRange
      ? [
          { value: this.value, name: 'Minimum' },
          { value: this.rangeEnd!, name: 'Maximum' },
        ]
      : [{ value: this.value, name: this.label ?? 'Value' }];
    const classes = { 'sl-slider': true, [`sl-tone-${this.tone}`]: true, 'sl-slider--range': this.isRange, 'is-disabled': this.disabled };

    return html`<div class=${classMap(classes)} part="base">
      ${this.label || this.showValue
        ? html`<div class="sl-slider__header">
            <span class="sl-slider__label" id=${this.labelId}>${this.label ?? ''}</span>
            ${this.showValue ? html`<output class="sl-slider__value">${display}</output>` : nothing}
          </div>`
        : nothing}
      <div
        class="sl-slider__track"
        part="track"
        @pointerdown=${this.onPointerDown}
        @pointermove=${this.onPointerMove}
        @pointerup=${this.onPointerUp}
        @pointercancel=${this.onPointerUp}
      >
        <span class="sl-slider__rail"></span>
        <span class="sl-slider__fill" style=${`--_start:${start};--_end:${end}`}></span>
        ${this.ticks ? this.tickPositions().map((p) => html`<span class="sl-slider__tick" style=${`--_pos:${p}`}></span>`) : nothing}
        ${thumbs.map(
          (t, i) => html`<span
            class=${classMap({ 'sl-slider__thumb': true, 'is-dragging': this.dragging === i })}
            part="thumb"
            role="slider"
            tabindex=${this.disabled ? -1 : 0}
            style=${`--_pos:${f(t.value)}`}
            aria-valuemin=${this.isRange && i === 1 ? this.value : this.min}
            aria-valuemax=${this.isRange && i === 0 ? this.rangeEnd! : this.max}
            aria-valuenow=${t.value}
            aria-orientation="horizontal"
            aria-disabled=${this.disabled ? 'true' : nothing}
            aria-labelledby=${this.label && !this.isRange ? this.labelId : nothing}
            aria-label=${this.label && !this.isRange ? nothing : this.label ? `${this.label} ${t.name.toLowerCase()}` : t.name}
            @keydown=${(e: KeyboardEvent) => this.onThumbKeyDown(i as 0 | 1, e)}
          ></span>`,
        )}
      </div>
    </div>`;
  }
}

export type AvatarStatus = 'online' | 'away' | 'busy' | 'offline';
const statuses = new Set(['online', 'away', 'busy', 'offline']);

/**
 * <sl-avatar name="Aaron Griffin" status="online" size="large"></sl-avatar>
 * <sl-avatar name="Jo" image="/jo.png"></sl-avatar>
 * Initials and a deterministic tone come from the name (same on every platform); `tone` overrides.
 */
export class SlAvatar extends LitElement {
  static override properties = {
    name: {},
    image: {},
    size: { reflect: true },
    tone: { reflect: true },
    status: { reflect: true },
    failed: { state: true },
  };

  static override styles = [hostReset, styles.base, styles.tone, styles.widgets, css`:host { display: inline-flex; }`];

  declare name: string | undefined;
  declare image: string | undefined;
  declare size: ControlSize;
  declare tone: Tone | undefined;
  declare status: AvatarStatus | 'null' | undefined;
  declare failed: boolean;

  constructor() {
    super();
    this.size = 'medium';
    this.failed = false;
  }

  protected override willUpdate(changed: PropertyValues<this>): void {
    if (changed.has('image')) this.failed = false;
  }

  override render() {
    const tone = this.tone ?? avatarTone(this.name);
    const status = this.status && statuses.has(this.status) ? this.status : undefined;
    const label = [this.name, status].filter(Boolean).join(', ');
    const classes = { 'sl-avatar': true, [`sl-tone-${tone}`]: true, [`sl-avatar--${this.size}`]: this.size !== 'medium' };
    return html`<span class=${classMap(classes)} part="base" role="img" aria-label=${label || 'Avatar'} data-status=${this.status ?? nothing}>
      ${this.image && !this.failed
        ? html`<img class="sl-avatar__image" src=${this.image} alt="" @error=${() => (this.failed = true)} />`
        : html`<span class="sl-avatar__initials" aria-hidden="true">${avatarInitials(this.name)}</span>`}
      ${status ? html`<span class=${`sl-avatar__status sl-avatar__status--${status}`} aria-hidden="true"></span>` : nothing}
    </span>`;
  }
}

export interface BreadcrumbItem {
  label: string;
  href?: string;
  icon?: string;
}

/**
 * <sl-breadcrumbs max-items="4" items='[{"label":"Library","href":"#"},{"label":"Inputs","href":"#"},{"label":"Text field"}]'></sl-breadcrumbs>
 * The last item is the current page. With `max-items`, the middle collapses into a "…" menu.
 * Fires `sl-item-click` (detail: { item, index }) — cancel it to stop navigation.
 */
export class SlBreadcrumbs extends LitElement {
  static override properties = {
    items: { type: Array },
    maxItems: { type: Number, attribute: 'max-items' },
    separator: {},
  };

  static override styles = [hostReset, styles.base, styles.navigation, css`:host { display: block; }`];

  declare items: BreadcrumbItem[];
  declare maxItems: number | undefined;
  declare separator: string;

  constructor() {
    super();
    this.items = [];
    this.separator = 'chevron-right';
  }

  private onItemClick(e: Event, item: BreadcrumbItem, index: number): void {
    const ok = this.dispatchEvent(new CustomEvent('sl-item-click', { bubbles: true, composed: true, cancelable: true, detail: { item, index } }));
    if (!ok) e.preventDefault();
  }

  private renderCrumb(item: BreadcrumbItem, index: number, last: boolean) {
    const content = html`${item.icon ? renderIcon(item.icon) : nothing}${item.label}`;
    const crumb = last
      ? html`<span class="sl-breadcrumbs__current" aria-current="page">${content}</span>`
      : item.href
        ? html`<a class="sl-breadcrumbs__link" href=${item.href} @click=${(e: Event) => this.onItemClick(e, item, index)}>${content}</a>`
        : html`<button class="sl-breadcrumbs__link" type="button" @click=${(e: Event) => this.onItemClick(e, item, index)}>${content}</button>`;
    return html`<li class="sl-breadcrumbs__item">
      ${crumb}${last ? nothing : html`<span class="sl-breadcrumbs__separator" aria-hidden="true">${renderIcon(this.separator)}</span>`}
    </li>`;
  }

  override render() {
    const items = this.items ?? [];
    const max = this.maxItems;
    const collapse = max !== undefined && max >= 2 && items.length > max;
    const tailCount = collapse ? max - 1 : items.length;
    const head = collapse ? items.slice(0, 1) : [];
    const hidden = collapse ? items.slice(1, items.length - tailCount) : [];
    const tail = collapse ? items.slice(items.length - tailCount) : items;
    const tailStart = items.length - tail.length;

    return html`<nav class="sl-breadcrumbs" part="base" aria-label="Breadcrumb">
      <ol class="sl-breadcrumbs__list">
        ${head.map((item) => this.renderCrumb(item, 0, false))}
        ${hidden.length
          ? html`<li class="sl-breadcrumbs__item">
              <sl-menu>
                <button slot="trigger" class="sl-breadcrumbs__link sl-breadcrumbs__more" type="button" aria-label=${`Show ${hidden.length} more`}>…</button>
                ${hidden.map((item, i) => html`<sl-menu-item label=${item.label} icon=${item.icon ?? nothing} href=${item.href ?? nothing} @click=${(e: Event) => this.onItemClick(e, item, i + 1)}></sl-menu-item>`)}
              </sl-menu>
              <span class="sl-breadcrumbs__separator" aria-hidden="true">${renderIcon(this.separator)}</span>
            </li>`
          : nothing}
        ${tail.map((item, i) => this.renderCrumb(item, tailStart + i, tailStart + i === items.length - 1))}
      </ol>
    </nav>`;
  }
}

/**
 * <sl-pagination page="2" total-count="312" page-size="20" page-sizes="[10,20,50]"></sl-pagination>
 * <sl-pagination page="1" page-count="12" siblings="2" size="small"></sl-pagination>
 * Pages are 1-based. Changing the page size keeps the first visible item on screen.
 * Fires `sl-page-changed` (detail: { page }) and `sl-page-size-changed` (detail: { pageSize, page }).
 */
export class SlPagination extends LitElement {
  static override properties = {
    page: { type: Number, reflect: true },
    pageCount: { type: Number, attribute: 'page-count' },
    siblings: { type: Number },
    pageSize: { type: Number, attribute: 'page-size' },
    pageSizes: { type: Array, attribute: 'page-sizes' },
    totalCount: { type: Number, attribute: 'total-count' },
    size: { reflect: true },
  };

  static override styles = [hostReset, styles.base, styles.navigation, css`:host { display: block; }`];

  declare page: number;
  declare pageCount: number | undefined;
  declare siblings: number;
  declare pageSize: number | undefined;
  declare pageSizes: number[] | undefined;
  declare totalCount: number | undefined;
  declare size: ControlSize;

  constructor() {
    super();
    this.page = 1;
    this.siblings = 1;
    this.size = 'medium';
  }

  /** Effective number of pages (explicit, or from total count and page size). */
  get pages(): number {
    if (this.pageCount !== undefined && this.pageCount !== null) return Math.max(1, this.pageCount);
    if (this.totalCount !== undefined && this.pageSize) return Math.max(1, computePageCount(this.totalCount, this.pageSize));
    return 1;
  }

  goTo(page: number): void {
    const target = Math.min(Math.max(1, Math.round(page)), this.pages);
    if (target === this.page) return;
    this.page = target;
    this.dispatchEvent(new CustomEvent('sl-page-changed', { bubbles: true, composed: true, detail: { page: target } }));
  }

  private onSizeChange(e: Event): void {
    const pageSize = Number((e.target as HTMLSelectElement).value);
    const page = this.pageSize ? pageForFirstItem(this.page, this.pageSize, pageSize) : 1;
    this.pageSize = pageSize;
    this.page = Math.min(page, this.pages);
    this.dispatchEvent(new CustomEvent('sl-page-size-changed', { bubbles: true, composed: true, detail: { pageSize, page: this.page } }));
    this.dispatchEvent(new CustomEvent('sl-page-changed', { bubbles: true, composed: true, detail: { page: this.page } }));
  }

  override render() {
    const pages = this.pages;
    const page = Math.min(Math.max(1, this.page), pages);
    const items = paginationRange(page, pages, this.siblings);
    const summary =
      this.totalCount !== undefined && this.pageSize
        ? `${this.totalCount === 0 ? 0 : (page - 1) * this.pageSize + 1}–${Math.min(page * this.pageSize, this.totalCount)} of ${this.totalCount}`
        : null;
    const classes = { 'sl-pagination': true, [`sl-pagination--${this.size}`]: this.size !== 'medium' };

    return html`<nav class=${classMap(classes)} part="base" aria-label="Pagination">
      ${summary ? html`<span class="sl-pagination__summary" aria-live="polite">${summary}</span>` : nothing}
      <ul class="sl-pagination__pages">
        <li>
          <button class="sl-pagination__page" type="button" aria-label="Previous page" ?disabled=${page <= 1} @click=${() => this.goTo(page - 1)}>
            ${renderIcon('chevron-left')}
          </button>
        </li>
        ${items.map((p) =>
          p === '…'
            ? html`<li class="sl-pagination__ellipsis" aria-hidden="true">…</li>`
            : html`<li>
                <button
                  class="sl-pagination__page"
                  type="button"
                  aria-label=${`Page ${p}`}
                  aria-current=${p === page ? 'page' : nothing}
                  @click=${() => this.goTo(p)}
                >
                  ${p}
                </button>
              </li>`,
        )}
        <li>
          <button class="sl-pagination__page" type="button" aria-label="Next page" ?disabled=${page >= pages} @click=${() => this.goTo(page + 1)}>
            ${renderIcon('chevron-right')}
          </button>
        </li>
      </ul>
      ${this.pageSizes?.length
        ? html`<label class="sl-pagination__size"
            >Rows per page
            <select class="sl-pagination__select" @change=${this.onSizeChange}>
              ${this.pageSizes.map((s) => html`<option value=${s} ?selected=${s === this.pageSize}>${s}</option>`)}
            </select></label
          >`
        : nothing}
    </nav>`;
  }
}

export type SkeletonShape = 'text' | 'rect' | 'circle';

/**
 * <sl-skeleton lines="3"></sl-skeleton>  <sl-skeleton shape="circle"></sl-skeleton>
 * <sl-skeleton shape="rect" height="120px" animated="false"></sl-skeleton>
 * Decorative placeholder (hidden from assistive tech — mark the loading region with aria-busy).
 */
export class SlSkeleton extends LitElement {
  static override properties = {
    shape: { reflect: true },
    width: {},
    height: {},
    lines: { type: Number },
    animated: { converter: defaultTrue, reflect: true },
  };

  static override styles = [hostReset, styles.base, styles.widgets, css`:host { display: block; }`];

  declare shape: SkeletonShape;
  declare width: string | undefined;
  declare height: string | undefined;
  declare lines: number;
  declare animated: boolean;

  constructor() {
    super();
    this.shape = 'text';
    this.lines = 1;
    this.animated = true;
  }

  override connectedCallback(): void {
    super.connectedCallback();
    this.setAttribute('aria-hidden', 'true');
  }

  override render() {
    const block = (width: string | undefined) =>
      html`<span
        class=${classMap({ 'sl-skeleton': true, [`sl-skeleton--${this.shape}`]: true, 'is-animated': this.animated })}
        part="base"
        style=${styleMap({ width, height: this.height })}
      ></span>`;
    if (this.shape === 'text' && this.lines > 1) {
      return html`<span class="sl-skeleton-group">
        ${Array.from({ length: this.lines }, (_, i) => block(i === this.lines - 1 ? '60%' : this.width))}
      </span>`;
    }
    return block(this.width);
  }
}
