import tokensCss from '../styles/generated/tokens.css?raw';

/**
 * Reads values straight from the generated tokens.css so behaviour (durations, breakpoints, sizes)
 * can never drift from the design source. Only the first (:root / default theme) declaration is used.
 */
const declarations = new Map<string, string>();
for (const match of tokensCss.matchAll(/^\s*(--sl-[a-z0-9-]+):\s*([^;]+);/gm)) {
  if (!declarations.has(match[1])) declarations.set(match[1], match[2].trim());
}

/** Raw CSS value of a token, e.g. tokenValue('--sl-space-4') → "16px". */
export function tokenValue(name: string): string {
  const value = declarations.get(name);
  if (value === undefined) throw new Error(`Unknown design token ${name}`);
  return value;
}

/** Numeric value of a px/ms token or a plain number. */
export function tokenNumber(name: string): number {
  const n = Number.parseFloat(tokenValue(name));
  if (Number.isNaN(n)) throw new Error(`Design token ${name} is not numeric`);
  return n;
}

export function hasToken(name: string): boolean {
  return declarations.has(name);
}

export const tokenNames: ReadonlySet<string> = new Set(declarations.keys());
