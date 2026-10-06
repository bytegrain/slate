import { tokenNumber } from './tokens';

/** Responsive breakpoints (min widths from the breakpoint.* tokens). Mirrors Slate.Layout.Breakpoint. */
export const breakpoints = ['xs', 'sm', 'md', 'lg', 'xl'] as const;
export type Breakpoint = (typeof breakpoints)[number];

export function breakpointMinWidth(bp: Breakpoint): number {
  return tokenNumber(`--sl-breakpoint-${bp}`);
}

export function breakpointFromWidth(width: number): Breakpoint {
  if (!(width >= 0)) throw new RangeError(`Width must be a non-negative number (got ${width}).`);
  for (let i = breakpoints.length - 1; i > 0; i--) {
    if (width >= breakpointMinWidth(breakpoints[i])) return breakpoints[i];
  }
  return 'xs';
}

export const gridColumns = 12;

export type GridSpan = Partial<Record<Breakpoint, number>>;

/** Mobile-first span resolution; unset breakpoints inherit from the next smaller one; xs defaults to 12. Mirrors Slate.Layout.GridSpan. */
export function resolveSpan(span: GridSpan, bp: Breakpoint): number {
  let value: number | undefined;
  for (let i = breakpoints.indexOf(bp); i >= 0 && value === undefined; i--) value = span[breakpoints[i]];
  const result = value ?? gridColumns;
  if (!Number.isInteger(result) || result < 1 || result > gridColumns) {
    throw new RangeError(`Grid spans must be whole numbers between 1 and ${gridColumns} (got ${result}).`);
  }
  return result;
}

/** Space token steps usable for spacing props (space.0, space.0_5 …). */
export const spaceSteps = ['0', '0.5', '1', '1.5', '2', '3', '4', '5', '6', '8', '10', '12', '16'] as const;

/** "4" → "var(--sl-space-4)"; "0.5" → "var(--sl-space-0-5)". Throws for unknown steps. */
export function spaceVar(step: string | number): string {
  const key = String(step);
  if (!(spaceSteps as readonly string[]).includes(key)) {
    throw new RangeError(`Unknown spacing step "${key}". Use one of: ${spaceSteps.join(', ')}.`);
  }
  return `var(--sl-space-${key.replace('.', '-')})`;
}
