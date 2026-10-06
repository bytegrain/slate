/**
 * Tree model behind sl-tree-view — a port of Slate.Core's TreeModel<T>: flattening, filtering that keeps
 * ancestors, WAI-ARIA tree keys, tri-state checkboxes and lazy-load bookkeeping. Nodes are addressed by string id.
 */

export type TreeKey = 'up' | 'down' | 'home' | 'end' | 'right' | 'left' | 'expand-siblings';
export type CheckState = 'unchecked' | 'checked' | 'indeterminate';
export type TreeLoadState = 'loaded' | 'not-loaded' | 'loading' | 'failed';

export interface TreeRow {
  id: string;
  parentId: string | null;
  depth: number;
  hasChildren: boolean;
  expanded: boolean;
  /** 1-based position among visible siblings (aria-posinset). */
  positionInSet: number;
  /** Visible sibling count (aria-setsize). */
  setSize: number;
}

export interface TreeNavigation {
  focusId: string | null;
  expanded: Set<string>;
}

export interface TreeAccessors<T> {
  id: (node: T) => string;
  children: (node: T) => Iterable<T> | null | undefined;
  /** Lazy trees: whether a node has children that may not be loaded yet. */
  hasChildren?: (node: T) => boolean;
}

export class TreeModel<T> {
  private readonly nodes = new Map<string, T>();
  private readonly parent = new Map<string, string | null>();
  private readonly childIds = new Map<string, string[]>();
  private readonly load = new Map<string, TreeLoadState>();
  readonly roots: string[] = [];

  constructor(roots: Iterable<T>, private readonly accessors: TreeAccessors<T>) {
    for (const root of roots) this.roots.push(this.index(root, null));
  }

  node(id: string): T {
    const n = this.nodes.get(id);
    if (n === undefined) throw new RangeError(`Unknown tree node '${id}'.`);
    return n;
  }

  parentOf(id: string): string | null {
    return this.parent.get(id) ?? null;
  }

  childrenOf(id: string): readonly string[] {
    return this.childIds.get(id) ?? [];
  }

  loadState(id: string): TreeLoadState {
    return this.load.get(id) ?? 'loaded';
  }

  hasChildren(id: string): boolean {
    const s = this.loadState(id);
    return this.childrenOf(id).length > 0 || s === 'not-loaded' || s === 'loading' || s === 'failed';
  }

  /** Ancestors, nearest first. */
  *ancestors(id: string): Generator<string> {
    for (let p = this.parent.get(id) ?? null; p !== null; p = this.parent.get(p) ?? null) yield p;
  }

  flatten(expanded: ReadonlySet<string>, visible?: ReadonlySet<string> | null): TreeRow[] {
    const rows: TreeRow[] = [];
    const walk = (ids: readonly string[], parentId: string | null, depth: number) => {
      const shown = visible ? ids.filter((id) => visible.has(id)) : ids;
      shown.forEach((id, i) => {
        const has = this.hasChildren(id);
        const open = has && expanded.has(id);
        rows.push({ id, parentId, depth, hasChildren: has, expanded: open, positionInSet: i + 1, setSize: shown.length });
        if (open) walk(this.childrenOf(id), id, depth + 1);
      });
    };
    walk(this.roots, null, 0);
    return rows;
  }

  /** Ids matching `isMatch` plus their ancestors, and the ancestors to expand to reveal the matches. */
  filter(isMatch: (node: T) => boolean): { visible: Set<string>; expand: Set<string> } {
    const visible = new Set<string>();
    const expand = new Set<string>();
    for (const [id, node] of this.nodes) {
      if (!isMatch(node)) continue;
      visible.add(id);
      for (const a of this.ancestors(id)) {
        visible.add(a);
        expand.add(a);
      }
    }
    return { visible, expand };
  }

  navigate(focus: string | null, key: TreeKey, expanded: ReadonlySet<string>, visible?: ReadonlySet<string> | null): TreeNavigation {
    const rows = this.flatten(expanded, visible);
    const set = new Set(expanded);
    if (rows.length === 0) return { focusId: null, expanded: set };
    const index = focus === null ? -1 : rows.findIndex((r) => r.id === focus);
    if (index < 0) return { focusId: rows[0].id, expanded: set };
    const row = rows[index];

    switch (key) {
      case 'up': return { focusId: rows[Math.max(0, index - 1)].id, expanded: set };
      case 'down': return { focusId: rows[Math.min(rows.length - 1, index + 1)].id, expanded: set };
      case 'home': return { focusId: rows[0].id, expanded: set };
      case 'end': return { focusId: rows[rows.length - 1].id, expanded: set };
      case 'right':
        if (!row.hasChildren) return { focusId: row.id, expanded: set };
        if (!row.expanded) {
          set.add(row.id);
          return { focusId: row.id, expanded: set };
        }
        return { focusId: index + 1 < rows.length && rows[index + 1].parentId === row.id ? rows[index + 1].id : row.id, expanded: set };
      case 'left':
        if (row.expanded) {
          set.delete(row.id);
          return { focusId: row.id, expanded: set };
        }
        return { focusId: row.parentId ?? row.id, expanded: set };
      case 'expand-siblings': {
        const siblings = row.parentId === null ? this.roots : this.childrenOf(row.parentId);
        for (const s of siblings) if (this.hasChildren(s) && (!visible || visible.has(s))) set.add(s);
        return { focusId: row.id, expanded: set };
      }
      default:
        throw new RangeError(`Unknown key '${key as string}'.`);
    }
  }

  checkState(id: string, checked: ReadonlySet<string>): CheckState {
    const children = this.childrenOf(id);
    if (children.length === 0) return checked.has(id) ? 'checked' : 'unchecked';
    const states = children.map((c) => this.checkState(c, checked));
    if (states.every((s) => s === 'checked')) return 'checked';
    if (states.every((s) => s === 'unchecked')) return 'unchecked';
    return 'indeterminate';
  }

  /** Toggles a node's subtree; ancestors become checked exactly when all their children are. */
  toggleCheck(id: string, checked: ReadonlySet<string>): Set<string> {
    const result = new Set(checked);
    const check = this.checkState(id, checked) !== 'checked';
    for (const n of this.subtree(id)) {
      if (check) result.add(n);
      else result.delete(n);
    }
    for (const a of this.ancestors(id)) {
      if (this.childrenOf(a).every((c) => this.checkState(c, result) === 'checked')) result.add(a);
      else result.delete(a);
    }
    return result;
  }

  /** Lazy loading: call when a node expands. True means start loading its children. */
  beginLoad(id: string): boolean {
    const s = this.loadState(id);
    if (s !== 'not-loaded' && s !== 'failed') return false;
    this.load.set(id, 'loading');
    return true;
  }

  completeLoad(id: string, children: Iterable<T>): void {
    const list: string[] = [];
    this.childIds.set(id, list);
    for (const c of children) list.push(this.index(c, id));
    this.load.set(id, 'loaded');
  }

  failLoad(id: string): void {
    this.load.set(id, 'failed');
  }

  private *subtree(id: string): Generator<string> {
    yield id;
    for (const c of this.childrenOf(id)) yield* this.subtree(c);
  }

  private index(node: T, parent: string | null): string {
    const id = this.accessors.id(node);
    if (this.nodes.has(id)) throw new Error(`Duplicate tree node id '${id}'.`);
    this.nodes.set(id, node);
    this.parent.set(id, parent);
    const children = [...(this.accessors.children(node) ?? [])];
    if (children.length > 0) {
      this.childIds.set(id, children.map((c) => this.index(c, id)));
    } else if (this.accessors.hasChildren?.(node)) {
      this.load.set(id, 'not-loaded');
    }
    return id;
  }
}
