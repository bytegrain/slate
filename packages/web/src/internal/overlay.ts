import { positionAtPoint, positionPopover, type OverlayRect, type PopoverPlacement, type PositionResult } from '../core/overlay/positioning';

/**
 * Floating layer shared by popover, menu, select, date picker and tooltip.
 *
 * - The panel lives in its component's shadow root (so styles and `::part`s stay encapsulated) and is promoted
 *   to the browser's top layer with the Popover API (`popover="manual"`), which escapes `overflow: hidden` and
 *   stacking contexts. Environments without the API fall back to `position: fixed` with `--sl-z-*`.
 * - Placement comes from Slate.Core's positioning port (`positionPopover` / `positionAtPoint`): flip, shift,
 *   max-height and arrow offset are identical on every platform.
 * - Open overlays form a stack: Escape and outside clicks dismiss only the topmost one(s), and the Escape is
 *   consumed so an enclosing dialog doesn't also close.
 */
export type DismissReason = 'escape' | 'outside';

export interface OverlayConfig {
  /** The element the panel attaches to (ignored when opened at a point). */
  anchor: () => Element | null;
  /** The floating panel. */
  panel: () => HTMLElement | null;
  placement: () => PopoverPlacement;
  offset?: () => number;
  /** Panel at least as wide as the anchor (selects, date fields). */
  matchWidth?: () => boolean;
  /** Elements that count as "inside" for outside-click detection (anchor and panel are included). */
  inside?: () => Array<Element | null>;
  /** Whether a click outside dismisses (default true). */
  dismissOnOutside?: () => boolean;
  /** Called when the user dismisses the overlay; the component must call `close()` (usually via its `open` state). */
  onDismiss: (reason: DismissReason) => void;
  /** Called after each positioning pass. */
  onPositioned?: (result: PositionResult) => void;
}

const stack: Overlay[] = [];
let listening = false;

function onKeyDown(e: KeyboardEvent): void {
  if (e.key !== 'Escape' || stack.length === 0) return;
  const top = stack[stack.length - 1];
  e.preventDefault();
  e.stopPropagation();
  top.config.onDismiss('escape');
}

function onPointerDown(e: Event): void {
  const path = e.composedPath();
  // Walk from the top: dismiss overlays the event is outside of, stop at the first one it is inside.
  for (let i = stack.length - 1; i >= 0; i--) {
    const o = stack[i];
    if (o.contains(path)) break;
    if (o.config.dismissOnOutside?.() ?? true) o.config.onDismiss('outside');
    else break;
  }
}

function listen(on: boolean): void {
  if (on === listening || typeof document === 'undefined') return;
  listening = on;
  const method = on ? 'addEventListener' : 'removeEventListener';
  document[method]('keydown', onKeyDown as EventListener, true);
  document[method]('pointerdown', onPointerDown, true);
}

/** Viewport in CSS pixels (excludes scrollbars). */
export function viewportRect(): OverlayRect {
  const el = document.documentElement;
  return { x: 0, y: 0, width: el.clientWidth || window.innerWidth, height: el.clientHeight || window.innerHeight };
}

function toRect(r: DOMRect): OverlayRect {
  return { x: r.left, y: r.top, width: r.width, height: r.height };
}

export class Overlay {
  private point: { x: number; y: number } | null = null;
  private frame = 0;
  private openState = false;

  constructor(readonly config: OverlayConfig) {}

  get isOpen(): boolean {
    return this.openState;
  }

  /** Opens anchored to the anchor element, or at a viewport point (context menus). */
  open(point?: { x: number; y: number }): void {
    const panel = this.config.panel();
    if (!panel) return;
    this.point = point ?? null;
    if (!this.openState) {
      this.openState = true;
      stack.push(this);
      listen(true);
      window.addEventListener('scroll', this.schedule, true);
      window.addEventListener('resize', this.schedule);
    }
    panel.hidden = false;
    if (typeof panel.showPopover === 'function') {
      if (!panel.hasAttribute('popover')) panel.setAttribute('popover', 'manual');
      try {
        if (!panel.matches(':popover-open')) panel.showPopover();
      } catch {
        /* not connected or already open */
      }
    }
    this.position();
  }

  close(): void {
    if (!this.openState) return;
    this.openState = false;
    const i = stack.indexOf(this);
    if (i >= 0) stack.splice(i, 1);
    if (stack.length === 0) listen(false);
    window.removeEventListener('scroll', this.schedule, true);
    window.removeEventListener('resize', this.schedule);
    cancelAnimationFrame(this.frame);
    const panel = this.config.panel();
    if (!panel) return;
    if (typeof panel.hidePopover === 'function' && panel.hasAttribute('popover')) {
      try {
        panel.hidePopover();
      } catch {
        /* already hidden */
      }
    }
    panel.hidden = true;
  }

  /** True when the event path is inside the anchor, the panel or any extra inside element. */
  contains(path: EventTarget[]): boolean {
    const inside = [this.config.anchor(), this.config.panel(), ...(this.config.inside?.() ?? [])].filter((x): x is Element => !!x);
    return inside.some((el) => path.includes(el));
  }

  /** Recomputes the panel's position (also runs on scroll/resize while open). */
  position(): void {
    const panel = this.config.panel();
    if (!panel || !this.openState) return;
    const anchor = this.config.anchor();
    const anchorRect = anchor ? toRect(anchor.getBoundingClientRect()) : { x: 0, y: 0, width: 0, height: 0 };

    panel.style.maxHeight = '';
    if (this.config.matchWidth?.()) panel.style.minWidth = `${anchorRect.width}px`;

    const width = panel.offsetWidth;
    const height = panel.offsetHeight;
    const viewport = viewportRect();
    const result = this.point
      ? positionAtPoint(this.point.x, this.point.y, width, height, viewport)
      : positionPopover({
          anchor: anchorRect,
          popupWidth: width,
          popupHeight: height,
          viewport,
          placement: this.config.placement(),
          offset: this.config.offset?.() ?? 6,
        });

    panel.style.left = `${Math.round(result.rect.x)}px`;
    panel.style.top = `${Math.round(result.rect.y)}px`;
    if (result.maxHeight != null) panel.style.maxHeight = `${Math.floor(result.maxHeight)}px`;
    panel.dataset.side = result.placement.split('-')[0];
    panel.dataset.actualPlacement = result.placement;
    panel.style.setProperty('--_arrow', `${Math.round(result.arrowOffset)}px`);
    this.config.onPositioned?.(result);
  }

  private readonly schedule = (): void => {
    cancelAnimationFrame(this.frame);
    this.frame = requestAnimationFrame(() => this.position());
  };
}

/** Moves focus into `root`'s first focusable descendant (light or shadow), returning whether it found one. */
export function focusFirstIn(root: Element | ShadowRoot | null): boolean {
  if (!root) return false;
  const selector = 'button:not([disabled]), [href], input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';
  const target = root.querySelector<HTMLElement>(selector);
  if (target) {
    target.focus();
    return true;
  }
  return false;
}

/** For tests: number of open overlays. */
export function openOverlayCount(): number {
  return stack.length;
}
