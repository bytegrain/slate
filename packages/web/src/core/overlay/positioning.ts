/**
 * Popover / menu / tooltip placement — a port of Slate.Core's PopoverPositioner (flip, shift, size constraint).
 * Results are identical to the C# implementation (tests/fixtures/components.json).
 */

export type PopoverPlacement =
  | 'top' | 'top-start' | 'top-end'
  | 'bottom' | 'bottom-start' | 'bottom-end'
  | 'left' | 'left-start' | 'left-end'
  | 'right' | 'right-start' | 'right-end';

export interface OverlayRect {
  x: number;
  y: number;
  width: number;
  height: number;
}

export interface PositionRequest {
  anchor: OverlayRect;
  popupWidth: number;
  popupHeight: number;
  viewport: OverlayRect;
  placement?: PopoverPlacement;
  /** Gap between anchor and popup (default 6). */
  offset?: number;
  /** Minimum distance from the viewport edges (default 8). */
  padding?: number;
  /** Move to the opposite side when the preferred side doesn't fit (default true). */
  flip?: boolean;
  /** Slide along the anchor's edge to stay in the viewport (default true). */
  shift?: boolean;
  /** Closest the arrow may get to the popup's corners (default 12). */
  arrowPadding?: number;
}

export interface PositionResult {
  rect: OverlayRect;
  placement: PopoverPlacement;
  /** Set when the popup had to be shortened to fit — render it scrollable. */
  maxHeight: number | null;
  /** Arrow distance from the popup's start edge along the cross axis. */
  arrowOffset: number;
  flipped: boolean;
}

type Side = 'top' | 'bottom' | 'left' | 'right';
type Align = 'center' | 'start' | 'end';

const right = (r: OverlayRect) => r.x + r.width;
const bottom = (r: OverlayRect) => r.y + r.height;

function clamp(v: number, min: number, max: number): number {
  return max < min ? min : Math.min(Math.max(v, min), max);
}

function split(p: PopoverPlacement): [Side, Align] {
  const [side, align] = p.split('-') as [Side, 'start' | 'end' | undefined];
  return [side, align ?? 'center'];
}

function join(side: Side, align: Align): PopoverPlacement {
  return (align === 'center' ? side : `${side}-${align}`) as PopoverPlacement;
}

const opposite: Record<Side, Side> = { top: 'bottom', bottom: 'top', left: 'right', right: 'left' };

export function positionPopover(req: PositionRequest): PositionResult {
  const offset = req.offset ?? 6;
  const padding = req.padding ?? 8;
  const flip = req.flip ?? true;
  const shift = req.shift ?? true;
  const arrowPadding = req.arrowPadding ?? 12;
  if (req.popupWidth < 0 || req.popupHeight < 0) throw new RangeError('Popup size must be non-negative.');

  const { anchor: a, viewport: vp } = req;
  const [side, align] = split(req.placement ?? 'bottom');
  const w = req.popupWidth;
  let h = req.popupHeight;
  let maxHeight: number | null = null;

  const space = (s: Side): number => {
    switch (s) {
      case 'bottom': return bottom(vp) - padding - (bottom(a) + offset);
      case 'top': return a.y - offset - (vp.y + padding);
      case 'right': return right(vp) - padding - (right(a) + offset);
      default: return a.x - offset - (vp.x + padding);
    }
  };
  const extent = (s: Side) => (s === 'top' || s === 'bottom' ? h : w);

  let chosen = side;
  if (flip && space(side) < extent(side)) {
    const opp = opposite[side];
    if (space(opp) >= extent(opp)) chosen = opp;
    else if (space(opp) > space(side)) chosen = opp;
  }

  const vertical = chosen === 'top' || chosen === 'bottom';
  const available = vertical ? Math.max(0, space(chosen)) : Math.max(0, vp.height - 2 * padding);
  if (h > available) {
    h = available;
    maxHeight = available;
  }

  const crossX = () => (align === 'start' ? a.x : align === 'end' ? right(a) - w : a.x + (a.width - w) / 2);
  const crossY = () => (align === 'start' ? a.y : align === 'end' ? bottom(a) - h : a.y + (a.height - h) / 2);
  let x: number;
  let y: number;
  switch (chosen) {
    case 'bottom': x = crossX(); y = bottom(a) + offset; break;
    case 'top': x = crossX(); y = a.y - offset - h; break;
    case 'right': x = right(a) + offset; y = crossY(); break;
    default: x = a.x - offset - w; y = crossY(); break;
  }

  if (shift) {
    if (vertical) x = clamp(x, vp.x + padding, right(vp) - padding - w);
    else y = clamp(y, vp.y + padding, bottom(vp) - padding - h);
  }

  const arrowOffset = vertical
    ? clamp(a.x + a.width / 2 - x, arrowPadding, w - arrowPadding)
    : clamp(a.y + a.height / 2 - y, arrowPadding, h - arrowPadding);

  return { rect: { x, y, width: w, height: h }, placement: join(chosen, align), maxHeight, arrowOffset, flipped: chosen !== side };
}

/** Context-menu placement at a pointer position: down-right, flipping left/up at the edges, clamped. */
export function positionAtPoint(pointX: number, pointY: number, popupWidth: number, popupHeight: number, viewport: OverlayRect, padding = 8): PositionResult {
  let x = pointX;
  let y = pointY;
  let h = popupHeight;
  let maxHeight: number | null = null;

  const flippedX = x + popupWidth > right(viewport) - padding;
  if (flippedX) x = pointX - popupWidth;
  const flippedY = y + h > bottom(viewport) - padding;
  if (flippedY) y = pointY - h;

  const availableHeight = Math.max(0, viewport.height - 2 * padding);
  if (h > availableHeight) {
    h = availableHeight;
    maxHeight = availableHeight;
  }

  x = clamp(x, viewport.x + padding, right(viewport) - padding - popupWidth);
  y = clamp(y, viewport.y + padding, bottom(viewport) - padding - h);

  const placement: PopoverPlacement = flippedY ? (flippedX ? 'top-end' : 'top-start') : flippedX ? 'bottom-end' : 'bottom-start';
  return { rect: { x, y, width: popupWidth, height: h }, placement, maxHeight, arrowOffset: 0, flipped: flippedX || flippedY };
}
