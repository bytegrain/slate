/**
 * Cell, editor and filter-editor rendering for <sl-data-grid>. Pure presentation: every value, text and
 * comparison comes from the grid engine (core/grid); this file only turns them into the markup documented in
 * docs/design/css-classes.md#data-grid.
 */
import { html, nothing, type TemplateResult } from 'lit';
import { classMap } from 'lit/directives/class-map.js';
import { renderIcon } from '../internal/icon';
import { col, displayText, formatValue, toDouble, type GridColumn, type GridTone } from '../core/grid/values';
import type { FilterOperator } from '../core/grid/values';
import type { FilterValue, GridFilter } from '../core/grid/state';
import { filterText } from '../core/grid/pipeline';
import type { Tone } from '../core/defaults';

/** A row action for `type: 'actions'` columns (rendered as an sl-menu). */
export interface DataGridAction<T> {
  label: string;
  icon?: string;
  tone?: Tone;
  shortcut?: string;
  separator?: boolean;
  disabled?: (item: T) => boolean;
  run?: (item: T) => void;
}

/** Everything a custom cell renderer receives. */
export interface DataGridCellContext<T> {
  item: T;
  value: unknown;
  text: string;
  column: DataGridColumn<T>;
  rowKey: string;
  rowIndex: number;
}

/**
 * A column of <sl-data-grid>: the engine's GridColumn plus renderer-only options.
 * `cell` returns a Lit template (or string) for `type: 'custom'` — or overrides any type's cell.
 */
// eslint-disable-next-line @typescript-eslint/no-explicit-any
export interface DataGridColumn<T = any> extends GridColumn<T> {
  cell?: (ctx: DataGridCellContext<T>) => unknown;
  actions?: readonly DataGridAction<T>[];
  /** Label of a header group spanning consecutive columns with the same value (multi-row headers). */
  headerGroup?: string;
  /** Editor override (defaults by type: text, number, date, checkbox, select for enums). */
  editor?: 'text' | 'number' | 'date' | 'select' | 'checkbox';
  /** Values offered by the enum filter (default: enumOrder, else distinct values from the data). */
  filterOptions?: readonly string[];
  /** Longer description shown as the header's tooltip. */
  description?: string;
}

const toneClass = (tone: GridTone | Tone | undefined) => `sl-tone-${tone ?? 'neutral'}`;

/** Default cell content by column type. */
export function renderCellContent<T>(column: DataGridColumn<T>, value: unknown, item: T, text: string, ctx: DataGridCellContext<T>): unknown {
  if (column.cell) return column.cell(ctx);
  const type = col.type(column);
  if (value === null || value === undefined || (typeof value === 'string' && value === '')) {
    return type === 'actions' ? renderActions(column, item) : html`<span class="sl-data-grid__muted">—</span>`;
  }
  switch (type) {
    case 'boolean':
      return value === true || text === 'true' || text === 'Yes'
        ? html`<span class="sl-data-grid__bool" role="img" aria-label="Yes">${renderIcon('check')}</span>`
        : html`<span class="sl-data-grid__muted" aria-label="No">—</span>`;
    case 'enum':
      return html`<span class="sl-badge sl-badge--soft ${toneClass(column.enumTones?.[text])}">${text}</span>`;
    case 'progress': {
      const n = Math.min(Math.max(toDouble(value) ?? 0, 0), 100);
      return html`<span class="sl-data-grid__progress">
        <span class="sl-progress sl-progress--small sl-tone-accent" role="progressbar" aria-valuemin="0" aria-valuemax="100" aria-valuenow=${n}>
          <span class="sl-progress__bar" style="--_value:${n}%"></span>
        </span>
        <span class="sl-data-grid__progress-value">${text}</span>
      </span>`;
    }
    case 'sparkline': {
      const values = Array.isArray(value) ? value.map((v) => toDouble(v) ?? 0) : [];
      const max = Math.max(1e-9, ...values.map(Math.abs));
      return html`<span class="sl-data-grid__sparkline" role="img" aria-label=${`Trend: ${values.join(', ')}`}>
        ${values.map((v) => html`<span class="sl-data-grid__spark" style="height:${Math.max(6, (Math.abs(v) / max) * 100)}%"></span>`)}
      </span>`;
    }
    case 'actions':
      return renderActions(column, item);
    default:
      return html`<span class="sl-data-grid__text">${text}</span>`;
  }
}

function renderActions<T>(column: DataGridColumn<T>, item: T): TemplateResult | typeof nothing {
  const actions = column.actions ?? [];
  if (actions.length === 0) return nothing;
  return html`<sl-menu placement="bottom-end" class="sl-data-grid__actions"
    @sl-select=${(e: CustomEvent<{ item: HTMLElement }>) => {
      const i = Number(e.detail.item.dataset.index);
      actions[i]?.run?.(item);
    }}>
    <button slot="trigger" class="sl-data-grid__row-action" type="button" aria-label="Row actions" tabindex="-1">${renderIcon('more-horizontal')}</button>
    ${actions.map((a, i) =>
      a.separator
        ? html`<sl-menu-item separator></sl-menu-item>`
        : html`<sl-menu-item data-index=${i} label=${a.label} icon=${a.icon ?? nothing} shortcut=${a.shortcut ?? nothing}
            tone=${a.tone ?? nothing} ?disabled=${a.disabled?.(item) ?? false}></sl-menu-item>`)}
  </sl-menu>`;
}

/** The editor type for a column. */
export function editorKind<T>(column: DataGridColumn<T>): NonNullable<DataGridColumn<T>['editor']> {
  if (column.editor) return column.editor;
  switch (col.type(column)) {
    case 'number': case 'progress': return 'number';
    case 'date': return 'date';
    case 'boolean': return 'checkbox';
    case 'enum': return 'select';
    default: return 'text';
  }
}

/** Editor text for a value (what the user edits — the engine parses it back). */
export function editorText<T>(column: DataGridColumn<T>, value: unknown): string {
  if (value === null || value === undefined) return '';
  const kind = editorKind(column);
  if (kind === 'date') {
    const text = value instanceof Date ? value.toISOString() : String(value);
    return text.slice(0, 10);
  }
  if (kind === 'number') return String(toDouble(value) ?? '');
  return displayText(column, value);
}

// ---- filters -------------------------------------------------------------------------------------------

export interface FilterDraft {
  field: string;
  operator: FilterOperator;
  value: string;
  value2: string;
  values: string[];
}

export type FilterKind = 'text' | 'number' | 'date' | 'boolean' | 'enum';

export function filterKind<T>(column: DataGridColumn<T>): FilterKind {
  switch (col.type(column)) {
    case 'number': case 'progress': return 'number';
    case 'date': return 'date';
    case 'boolean': return 'boolean';
    case 'enum': return 'enum';
    default: return 'text';
  }
}

const OPERATORS: Record<FilterKind, { value: FilterOperator; label: string }[]> = {
  text: [
    { value: 'contains', label: 'Contains' },
    { value: 'equals', label: 'Equals' },
    { value: 'notEquals', label: 'Does not equal' },
    { value: 'startsWith', label: 'Starts with' },
    { value: 'endsWith', label: 'Ends with' },
    { value: 'isEmpty', label: 'Is empty' },
    { value: 'isNotEmpty', label: 'Is not empty' },
  ],
  number: [
    { value: 'equals', label: '=' },
    { value: 'notEquals', label: '≠' },
    { value: 'lessThan', label: '<' },
    { value: 'lessThanOrEqual', label: '≤' },
    { value: 'greaterThan', label: '>' },
    { value: 'greaterThanOrEqual', label: '≥' },
    { value: 'between', label: 'Between' },
    { value: 'isEmpty', label: 'Is empty' },
  ],
  date: [
    { value: 'equals', label: 'On' },
    { value: 'lessThan', label: 'Before' },
    { value: 'greaterThan', label: 'After' },
    { value: 'between', label: 'Between' },
    { value: 'isEmpty', label: 'Is empty' },
  ],
  boolean: [{ value: 'equals', label: 'Is' }],
  enum: [
    { value: 'anyOf', label: 'Is any of' },
    { value: 'isEmpty', label: 'Is empty' },
  ],
};

export const operatorsFor = (kind: FilterKind) => OPERATORS[kind];

export function operatorLabel(op: FilterOperator): string {
  for (const list of Object.values(OPERATORS)) {
    const found = list.find((o) => o.value === op);
    if (found) return found.label;
  }
  return op;
}

const needsValue = (op: FilterOperator) => op !== 'isEmpty' && op !== 'isNotEmpty';

/** A draft for the filter editor, from the column's active filter (or the kind's default operator). */
export function draftFrom<T>(column: DataGridColumn<T>, active: GridFilter | undefined): FilterDraft {
  const kind = filterKind(column);
  return {
    field: column.field,
    operator: active?.operator ?? OPERATORS[kind][0]!.value,
    value: filterText(active?.value),
    value2: filterText(active?.value2),
    values: (active?.values ?? []).map((v) => filterText(v)),
  };
}

/** The engine filter for a draft, or null when it filters nothing (empty value). */
export function filterFromDraft<T>(column: DataGridColumn<T>, d: FilterDraft): GridFilter | null {
  const kind = filterKind(column);
  if (!needsValue(d.operator)) return { field: d.field, operator: d.operator };
  if (d.operator === 'anyOf') return d.values.length ? { field: d.field, operator: 'anyOf', values: d.values } : null;
  const coerce = (s: string): FilterValue => {
    if (kind === 'number') {
      const n = toDouble(s);
      return n === null ? null : n;
    }
    if (kind === 'boolean') return s === 'true' ? true : s === 'false' ? false : null;
    return s;
  };
  const value = coerce(d.value.trim());
  if (value === null || value === '') return null;
  const filter: GridFilter = { field: d.field, operator: d.operator, value };
  if (d.operator === 'between') {
    const v2 = coerce(d.value2.trim());
    return v2 === null || v2 === '' ? filter : { ...filter, value2: v2 };
  }
  return filter;
}

/** Short human text for a filter chip: "Status: is any of Ready, Failed". */
export function describeFilter<T>(column: DataGridColumn<T>, f: GridFilter): string {
  const title = col.title(column);
  if (f.operator === 'isEmpty' || f.operator === 'isNotEmpty') return `${title} ${operatorLabel(f.operator).toLowerCase()}`;
  if (f.operator === 'anyOf') return `${title}: ${(f.values ?? []).map(filterText).join(', ')}`;
  if (f.operator === 'between') return `${title}: ${filterText(f.value)} – ${filterText(f.value2)}`;
  if (col.type(column) === 'boolean') return `${title} is ${f.value === true ? 'Yes' : 'No'}`;
  const op = operatorLabel(f.operator);
  const value = filterText(f.value);
  return /^[a-z]/i.test(op) ? `${title} ${op.toLowerCase()} ${value}` : `${title} ${op} ${value}`;
}

/** Filter editor body (inside the column's sl-popover). */
export function renderFilterEditor<T>(
  column: DataGridColumn<T>,
  draft: FilterDraft,
  options: readonly string[],
  change: (patch: Partial<FilterDraft>) => void,
  apply: () => void,
  clear: () => void,
): TemplateResult {
  const kind = filterKind(column);
  const ops = OPERATORS[kind];
  const valueField = (key: 'value' | 'value2', label: string) =>
    kind === 'date'
      ? html`<sl-date-picker label=${label} size="small" clearable .value=${draft[key] || undefined}
          @sl-value-changed=${(e: CustomEvent<{ value?: string }>) => change({ [key]: e.detail.value ?? '' })}></sl-date-picker>`
      : html`<sl-text-field label=${label} size="small" input-type=${kind === 'number' ? 'number' : 'text'} .value=${draft[key]}
          @sl-value-changed=${(e: CustomEvent<{ value: string }>) => change({ [key]: e.detail.value })}
          @keydown=${(e: KeyboardEvent) => { if (e.key === 'Enter') apply(); }}></sl-text-field>`;

  return html`<form class="sl-data-grid__panel" part="filter-panel" @submit=${(e: Event) => { e.preventDefault(); apply(); }}>
    <span class="sl-data-grid__panel-title">Filter · ${col.title(column)}</span>
    ${kind === 'boolean'
      ? html`<sl-segmented label="Value" full-width size="small"
          .items=${[{ value: 'true', label: 'Yes' }, { value: 'false', label: 'No' }]}
          .value=${draft.value || undefined}
          @sl-value-changed=${(e: CustomEvent<{ value: string }>) => change({ operator: 'equals', value: e.detail.value })}></sl-segmented>`
      : html`<sl-select label="Condition" size="small" .items=${ops} .value=${draft.operator}
          @sl-value-changed=${(e: CustomEvent<{ value: FilterOperator }>) => e.detail.value && change({ operator: e.detail.value })}></sl-select>`}
    ${!needsValue(draft.operator) || kind === 'boolean'
      ? nothing
      : draft.operator === 'anyOf'
        ? html`<sl-select label="Values" size="small" multiple searchable .items=${options} .values=${draft.values}
            @sl-values-changed=${(e: CustomEvent<{ values: string[] }>) => change({ values: [...e.detail.values] })}></sl-select>`
        : html`${valueField('value', draft.operator === 'between' ? 'From' : 'Value')}
            ${draft.operator === 'between' ? valueField('value2', 'To') : nothing}`}
    <div class="sl-data-grid__panel-actions">
      <sl-button size="small" variant="ghost" @click=${clear}>Clear</sl-button>
      <sl-button size="small" variant="solid" tone="accent" type="submit" @click=${apply}>Apply</sl-button>
    </div>
  </form>`;
}

/** Display text for an aggregate value. */
export function aggregateText<T>(column: DataGridColumn<T>, aggregate: string, value: unknown): string {
  if (value === null || value === undefined) return '—';
  if (aggregate === 'count') return formatValue(value, 'number', '#,##0');
  if (aggregate === 'avg' && typeof value === 'number' && !column.format) return displayText(column, Math.round(value * 100) / 100);
  return displayText(column, value);
}

export const aggregateLabel: Record<string, string> = { sum: 'Σ', avg: 'avg', min: 'min', max: 'max', count: 'count' };

/** Row classes for a data row. */
export function rowClasses(extra: Record<string, boolean>): ReturnType<typeof classMap> {
  return classMap({ 'sl-data-grid__row': true, ...extra });
}
