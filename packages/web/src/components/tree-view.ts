import { LitElement, css, html, nothing, type PropertyValues, type TemplateResult } from 'lit';
import { classMap } from 'lit/directives/class-map.js';
import { hostReset, styles } from '../internal/styles';
import { renderIcon } from '../internal/icon';
import { uid } from '../internal/dom';
import { filterOptions, foldText, Typeahead } from '../core/collections/list';
import { TreeModel, type TreeKey } from '../core/collections/tree';
import { highlight } from '../internal/items';

export type TreeSelectionMode = 'none' | 'single' | 'multi' | 'checkbox';

type TItem = unknown;

const treeKeys: Record<string, TreeKey> = {
  ArrowUp: 'up',
  ArrowDown: 'down',
  Home: 'home',
  End: 'end',
  ArrowRight: 'right',
  ArrowLeft: 'left',
  '*': 'expand-siblings',
};

const field = (node: TItem, name: string): unknown => (typeof node === 'object' && node !== null ? (node as Record<string, unknown>)[name] : undefined);

/**
 * <sl-tree-view selection-mode="single|multi|checkbox|none" .items=${tree} filter="tex" dense></sl-tree-view>
 *
 * Items are any objects; by default children come from `children`, text from `label`/`name`/`text`, icons from
 * `icon` and identity from `id` (override with `childrenSelector`, `itemText`, `itemIcon`, `itemKey`).
 * Lazy trees: `hasChildren` + async `loadChildren`. `selectedItems` and `expanded` are two-way (they accept items
 * or ids). `filter` shows matches with their ancestors (auto-expanded) and highlights the match.
 *
 * Keyboard (WAI-ARIA tree): Up/Down, Home/End, Right expands/moves to the first child, Left collapses/moves to
 * the parent, `*` expands siblings, Enter activates, Space selects/toggles, typeahead. Shift+click/Shift+arrows
 * extend in multi mode. Events: `sl-selection-changed` (detail: { selectedItems }), `sl-item-activated`
 * (detail: { item }), `sl-expanded-changed` (detail: { expanded }).
 */
export class SlTreeView extends LitElement {
  static override properties = {
    items: { type: Array },
    childrenSelector: { attribute: false },
    hasChildren: { attribute: false },
    loadChildren: { attribute: false },
    itemText: { attribute: false },
    itemIcon: { attribute: false },
    itemKey: { attribute: false },
    selectionMode: { attribute: 'selection-mode', reflect: true },
    selectedItems: { type: Array, attribute: 'selected-items' },
    expanded: { type: Array },
    filter: { reflect: true },
    dense: { type: Boolean, reflect: true },
    focusId: { state: true },
  };

  static override styles = [hostReset, styles.base, styles.feedback, styles.tree, css`:host { display: block; }`];

  declare items: TItem[];
  declare childrenSelector: ((node: TItem) => Iterable<TItem> | null | undefined) | undefined;
  declare hasChildren: ((node: TItem) => boolean) | undefined;
  declare loadChildren: ((node: TItem) => Promise<Iterable<TItem>>) | undefined;
  declare itemText: ((node: TItem) => string) | undefined;
  declare itemIcon: ((node: TItem) => string | null | undefined) | undefined;
  /** Web-only: stable id for a node (default `id`, else the text). */
  declare itemKey: ((node: TItem) => string) | undefined;
  declare selectionMode: TreeSelectionMode;
  declare selectedItems: TItem[];
  declare expanded: TItem[];
  declare filter: string | undefined;
  declare dense: boolean;
  declare focusId: string | null;

  private model: TreeModel<TItem> = new TreeModel<TItem>([], { id: () => '', children: () => [] });
  private expandedIds = new Set<string>();
  private selectedIds = new Set<string>();
  private anchorId: string | null = null;
  private readonly domIds = new Map<string, string>();
  private readonly typeahead = new Typeahead();
  private readonly idPrefix = uid('sl-tree');

  constructor() {
    super();
    this.items = [];
    this.selectionMode = 'single';
    this.selectedItems = [];
    this.expanded = [];
    this.dense = false;
    this.focusId = null;
  }

  idOf(node: TItem): string {
    if (this.itemKey) return this.itemKey(node);
    const id = field(node, 'id') ?? field(node, 'key');
    return id !== undefined && id !== null ? String(id) : this.textOf(node);
  }

  textOf(node: TItem): string {
    if (this.itemText) return this.itemText(node);
    if (typeof node !== 'object' || node === null) return String(node);
    return String(field(node, 'label') ?? field(node, 'name') ?? field(node, 'text') ?? '');
  }

  private iconOf(node: TItem): string | undefined {
    const icon = this.itemIcon ? this.itemIcon(node) : field(node, 'icon');
    return typeof icon === 'string' ? icon : undefined;
  }

  private toId(x: unknown): string {
    return typeof x === 'string' ? x : this.idOf(x);
  }

  protected override willUpdate(changed: PropertyValues<this>): void {
    if (changed.has('items') || changed.has('childrenSelector') || changed.has('hasChildren') || changed.has('itemKey')) {
      this.model = new TreeModel<TItem>(this.items ?? [], {
        id: (n) => this.idOf(n),
        children: (n) => (this.childrenSelector ? this.childrenSelector(n) : (field(n, 'children') as Iterable<TItem> | undefined)),
        hasChildren: this.hasChildren ?? ((n) => field(n, 'hasChildren') === true),
      });
      this.domIds.clear();
    }
    if (changed.has('expanded')) this.expandedIds = new Set((this.expanded ?? []).map((x) => this.toId(x)));
    if (changed.has('selectedItems')) this.selectedIds = new Set((this.selectedItems ?? []).map((x) => this.toId(x)));
  }

  private domId(id: string): string {
    let d = this.domIds.get(id);
    if (!d) {
      d = `${this.idPrefix}-${this.domIds.size}`;
      this.domIds.set(id, d);
    }
    return d;
  }

  /** Filter state: visible ids (null = all) and ancestors to auto-expand. */
  private filtered(): { visible: Set<string> | null; expanded: Set<string> } {
    const query = (this.filter ?? '').trim();
    if (!query) return { visible: null, expanded: this.expandedIds };
    const q = foldText(query);
    const { visible, expand } = this.model.filter((n) => foldText(this.textOf(n)).includes(q));
    return { visible, expanded: new Set([...this.expandedIds, ...expand]) };
  }

  private rows() {
    const { visible, expanded } = this.filtered();
    return { rows: this.model.flatten(expanded, visible), visible, expanded };
  }

  // ---- state changes -------------------------------------------------------------------------------------

  private setExpanded(ids: Set<string>): void {
    this.expandedIds = ids;
    this.expanded = [...ids].map((id) => this.model.node(id));
    this.dispatchEvent(new CustomEvent('sl-expanded-changed', { bubbles: true, composed: true, detail: { expanded: this.expanded } }));
    for (const id of ids) void this.ensureLoaded(id);
  }

  toggle(id: string): void {
    const next = new Set(this.expandedIds);
    if (next.has(id)) next.delete(id);
    else next.add(id);
    this.setExpanded(next);
  }

  private async ensureLoaded(id: string): Promise<void> {
    if (!this.loadChildren || this.model.loadState(id) === 'loaded' || !this.model.beginLoad(id)) return;
    this.requestUpdate();
    try {
      const children = await this.loadChildren(this.model.node(id));
      this.model.completeLoad(id, children);
    } catch {
      this.model.failLoad(id);
    }
    this.requestUpdate();
  }

  private setSelected(ids: Set<string>): void {
    this.selectedIds = ids;
    this.selectedItems = [...ids].map((id) => this.model.node(id));
    this.dispatchEvent(new CustomEvent('sl-selection-changed', { bubbles: true, composed: true, detail: { selectedItems: this.selectedItems } }));
  }

  private select(id: string, e: { shiftKey?: boolean; ctrlKey?: boolean; metaKey?: boolean } = {}): void {
    switch (this.selectionMode) {
      case 'none':
        return;
      case 'checkbox':
        this.setSelected(this.model.toggleCheck(id, this.selectedIds));
        return;
      case 'single':
        this.setSelected(new Set([id]));
        return;
      case 'multi': {
        if (e.shiftKey && this.anchorId) {
          const rows = this.rows().rows.map((r) => r.id);
          const [a, b] = [rows.indexOf(this.anchorId), rows.indexOf(id)].sort((x, y) => x - y);
          if (a >= 0 && b >= 0) this.setSelected(new Set(rows.slice(a, b + 1)));
          return;
        }
        this.anchorId = id;
        if (e.ctrlKey || e.metaKey) {
          const next = new Set(this.selectedIds);
          if (next.has(id)) next.delete(id);
          else next.add(id);
          this.setSelected(next);
        } else {
          this.setSelected(new Set([id]));
        }
      }
    }
  }

  private activate(id: string): void {
    this.dispatchEvent(new CustomEvent('sl-item-activated', { bubbles: true, composed: true, detail: { item: this.model.node(id) } }));
  }

  private focusItem(id: string | null): void {
    this.focusId = id;
    if (!id) return;
    this.updateComplete.then(() => this.renderRoot.querySelector<HTMLElement>(`#${this.domId(id)}`)?.focus());
  }

  // ---- input ---------------------------------------------------------------------------------------------

  private onKeyDown(e: KeyboardEvent): void {
    const { rows, visible, expanded } = this.rows();
    if (rows.length === 0) return;
    const focus = this.focusId ?? rows[0].id;
    const key = treeKeys[e.key];
    if (key) {
      e.preventDefault();
      const nav = this.model.navigate(focus, key, expanded, visible);
      if (nav.expanded.size !== expanded.size || [...nav.expanded].some((id) => !expanded.has(id))) {
        // Keep filter-forced expansion separate from the user's own expansion state.
        const user = new Set(this.expandedIds);
        for (const id of nav.expanded) if (!expanded.has(id)) user.add(id);
        for (const id of expanded) if (!nav.expanded.has(id)) user.delete(id);
        this.setExpanded(user);
      }
      this.focusItem(nav.focusId);
      if (e.shiftKey && this.selectionMode === 'multi' && nav.focusId && (key === 'up' || key === 'down')) this.select(nav.focusId, { shiftKey: true });
      return;
    }
    if (e.key === 'Enter') {
      e.preventDefault();
      if (this.selectionMode === 'single') this.select(focus);
      this.activate(focus);
      return;
    }
    if (e.key === ' ') {
      e.preventDefault();
      this.select(focus, { ctrlKey: this.selectionMode === 'multi' });
      return;
    }
    if (e.key.length === 1 && !e.ctrlKey && !e.metaKey && !e.altKey) {
      const labels = rows.map((r) => this.textOf(this.model.node(r.id)));
      const index = this.typeahead.search(e.key, Date.now(), labels, rows.findIndex((r) => r.id === focus));
      if (index >= 0) this.focusItem(rows[index].id);
    }
  }

  private onRowClick(id: string, e: MouseEvent): void {
    this.focusItem(id);
    this.select(id, e);
  }

  // ---- rendering -----------------------------------------------------------------------------------------

  private renderLevel(ids: readonly string[], depth: number, ctx: { visible: Set<string> | null; expanded: Set<string>; template: HTMLTemplateElement | null; tabStop: string | null }): TemplateResult[] {
    const shown = ctx.visible ? ids.filter((id) => ctx.visible!.has(id)) : ids;
    return shown.map((id, i) => {
      const node = this.model.node(id);
      const has = this.model.hasChildren(id);
      const open = has && ctx.expanded.has(id);
      const state = this.model.loadState(id);
      const text = this.textOf(node);
      const icon = this.iconOf(node);
      const checkState = this.selectionMode === 'checkbox' ? this.model.checkState(id, this.selectedIds) : null;
      const selected = this.selectionMode === 'single' || this.selectionMode === 'multi' ? this.selectedIds.has(id) : false;
      const ranges = this.filter ? filterOptions([text], this.filter)[0]?.ranges : undefined;
      const custom = ctx.template ? this.fromTemplate(ctx.template, text) : null;

      return html`<div
        id=${this.domId(id)}
        class=${classMap({ 'sl-tree__item': true, 'is-selected': selected })}
        part="item"
        role="treeitem"
        tabindex=${id === ctx.tabStop ? 0 : -1}
        aria-level=${depth + 1}
        aria-setsize=${shown.length}
        aria-posinset=${i + 1}
        aria-expanded=${has ? (open ? 'true' : 'false') : nothing}
        aria-selected=${this.selectionMode === 'single' || this.selectionMode === 'multi' ? (selected ? 'true' : 'false') : nothing}
        aria-checked=${checkState ? (checkState === 'indeterminate' ? 'mixed' : checkState === 'checked' ? 'true' : 'false') : nothing}
        aria-busy=${state === 'loading' ? 'true' : nothing}
        @focus=${() => (this.focusId = id)}
      >
        <div class="sl-tree__row" style=${`--_depth:${depth}`} @click=${(e: MouseEvent) => this.onRowClick(id, e)} @dblclick=${() => this.activate(id)}>
          <span
            class=${classMap({ 'sl-tree__expander': true, 'is-leaf': !has })}
            part="expander"
            aria-hidden="true"
            @click=${(e: Event) => {
              e.stopPropagation();
              this.focusItem(id);
              if (has) this.toggle(id);
            }}
            >${renderIcon('chevron-right')}</span
          >
          ${checkState
            ? html`<span class="sl-tree__checkbox" part="checkbox" data-state=${checkState} aria-hidden="true"
                >${checkState === 'checked' ? renderIcon('check') : checkState === 'indeterminate' ? renderIcon('minus') : nothing}</span
              >`
            : nothing}
          ${icon ? html`<span class="sl-tree__icon" part="icon">${renderIcon(icon)}</span>` : nothing}
          <span class="sl-tree__label" part="label">${custom ?? highlight(text, ranges)}</span>
          ${state === 'loading' ? html`<span class="sl-tree__spinner"><span class="sl-spinner sl-spinner--small" role="status" aria-label="Loading"></span></span>` : nothing}
        </div>
        ${open
          ? html`<div class="sl-tree__children" part="children" role="group">
              ${state === 'failed'
                ? html`<div class="sl-tree__empty" style=${`padding-left: calc(${depth + 1} * var(--sl-component-tree-indent))`}>Couldn’t load</div>`
                : this.renderLevel(this.model.childrenOf(id), depth + 1, ctx)}
            </div>`
          : nothing}
      </div>`;
    });
  }

  private fromTemplate(template: HTMLTemplateElement, text: string): ChildNode[] {
    const fragment = template.content.cloneNode(true) as DocumentFragment;
    for (const el of fragment.querySelectorAll('[data-text]')) el.textContent = text;
    return [...fragment.childNodes];
  }

  override render() {
    const { rows, visible, expanded } = this.rows();
    const tabStop = this.focusId && rows.some((r) => r.id === this.focusId) ? this.focusId : (rows.find((r) => this.selectedIds.has(r.id))?.id ?? rows[0]?.id ?? null);
    const template = this.querySelector<HTMLTemplateElement>(':scope > template[slot="item-template"]');
    const classes = { 'sl-tree': true, [`sl-tree--${this.selectionMode}`]: true, 'sl-tree--dense': this.dense };
    return html`<div
        class=${classMap(classes)}
        part="base"
        role="tree"
        aria-multiselectable=${this.selectionMode === 'multi' || this.selectionMode === 'checkbox' ? 'true' : nothing}
        @keydown=${this.onKeyDown}
      >
        ${rows.length === 0 && this.filter ? html`<div class="sl-tree__empty">No matches</div>` : this.renderLevel(this.model.roots, 0, { visible, expanded, template, tabStop })}
      </div>
      <slot name="item-template" hidden></slot>`;
  }
}
