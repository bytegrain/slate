import { SlateColor, roundHalfEven } from './color';
import { sharedValues, themeAliases, themeValues } from './data.generated';

/**
 * Port of Slate.Core's ThemeBuilder: custom themes on top of Alloy with the same contrast guarantees
 * and byte-identical output (asserted against tests/fixtures/theme-builder.json).
 */

export type ThemeBase = 'light' | 'dark';

export interface SlateThemeOptions {
  /** Defaults to the base theme's name. */
  name?: string;
  /** Built-in theme to start from. Default 'light'. */
  base?: ThemeBase;
  /** Brand colour (#RRGGBB). Accent states are derived from it, adjusted in lightness only. */
  accent?: string;
  /** Multiplies every radius token except radius.full: 0 = square, 1 = Alloy, 1.5 = softer. */
  radiusScale?: number;
  /** Replaces the UI font family list. */
  fontFamily?: string;
  /** Replaces the monospace font family list. */
  monoFontFamily?: string;
  /** Raw token overrides by path (CSS values), applied last. Unknown paths throw. */
  overrides?: Readonly<Record<string, string>>;
}

export interface SlateTheme {
  readonly name: string;
  readonly base: ThemeBase;
  readonly isDark: boolean;
  /** Token path → CSS value for every token needed to apply this theme. */
  readonly values: Readonly<Record<string, string>>;
  /** Paths whose value differs from the base theme. */
  readonly changedPaths: readonly string[];
}

const TEXT = 4.5;
const NON_TEXT = 3.0;
const READING_SURFACES = [
  'color.background.canvas',
  'color.background.surface',
  'color.background.sunken',
  'color.background.raised',
  'color.background.subtle',
];
const DARK_INK = SlateColor.parse('#05181C');

const shared = new Map(sharedValues);

export function createTheme(options: SlateThemeOptions = {}): SlateTheme {
  const base = options.base ?? 'light';
  if (base !== 'light' && base !== 'dark') {
    throw new Error(`Base theme must be 'light' or 'dark', not '${String(base)}'.`);
  }
  const radiusScale = options.radiusScale ?? 1;
  if (Number.isNaN(radiusScale) || radiusScale < 0 || radiusScale > 4) {
    throw new RangeError('radiusScale must be between 0 and 4.');
  }

  const baseValues = new Map(themeValues[base]);
  const values = new Map(baseValues);
  const dark = base === 'dark';

  if (options.accent !== undefined) {
    const brand = SlateColor.tryParse(options.accent);
    if (!brand || !brand.isOpaque) throw new Error(`Accent '${options.accent}' must be an opaque #RRGGBB colour.`);
    for (const [path, color] of deriveAccent(brand, dark, values)) values.set(path, color.toHex());
  }

  if (radiusScale !== 1) {
    for (const [path, raw] of shared) {
      if (!path.startsWith('radius.') || path === 'radius.full') continue;
      const px = Number.parseFloat(raw.slice(0, -2));
      values.set(path, `${roundHalfEven(px * radiusScale * 10) / 10}px`);
    }
  }

  if (options.fontFamily !== undefined) values.set('font.family.ui', options.fontFamily);
  if (options.monoFontFamily !== undefined) values.set('font.family.mono', options.monoFontFamily);

  const overrides = options.overrides ?? {};
  for (const [path, value] of Object.entries(overrides)) {
    if (!baseValues.has(path) && !shared.has(path)) throw new Error(`Unknown token path '${path}' in overrides.`);
    if (path.startsWith('color.') && !SlateColor.tryParse(value)) {
      throw new Error(`Override '${path}' must be a hex colour, got '${value}'.`);
    }
    values.set(path, value);
  }

  // Carry changes through component-token aliases (unless a component token was overridden explicitly).
  const explicit = new Set(Object.keys(overrides));
  const aliases = themeAliases[base];
  for (let pass = 0; pass < 8; pass++) {
    let moved = false;
    for (const [path, target] of aliases) {
      if (explicit.has(path) || !values.has(target)) continue;
      const targetValue = values.get(target)!;
      if (values.get(path) === targetValue) continue;
      values.set(path, targetValue);
      moved = true;
    }
    if (!moved) break;
  }

  const changedPaths: string[] = [];
  for (const [path, value] of values) {
    const unchanged = baseValues.has(path) ? baseValues.get(path) === value : shared.has(path) && shared.get(path) === value;
    if (!unchanged) changedPaths.push(path);
  }

  return Object.freeze({
    name: options.name ?? base,
    base,
    isDark: dark,
    values: Object.freeze(Object.fromEntries(values)),
    changedPaths: Object.freeze(changedPaths),
  });
}

function deriveAccent(brand: SlateColor, dark: boolean, theme: Map<string, string>): Array<[string, SlateColor]> {
  const surfaces = READING_SURFACES.map((p) => SlateColor.parse(theme.get(p)!));

  const preferred = dark ? DARK_INK : SlateColor.white;
  const other = dark ? SlateColor.white : DARK_INK;
  const candidates = [preferred, other]
    .map((text) => ({
      text,
      fill: nearest(brand, (c) => c.contrastWith(text) >= TEXT && surfaces.every((s) => c.contrastWith(s) >= NON_TEXT)),
    }))
    .filter((x): x is { text: SlateColor; fill: SlateColor } => x.fill !== null)
    .sort((x, y) => distance(brand, x.fill) - distance(brand, y.fill));

  const best = candidates[0] ?? { text: preferred, fill: SlateColor.parse(theme.get('color.accent.default')!) };
  const onAccent = best.text;
  const fill = best.fill;
  const readable = (c: SlateColor) => c.contrastWith(onAccent) >= TEXT;

  const hover = step(fill, dark ? [0.06, -0.06] : [0.05, -0.05], readable);
  const pressed = step(fill, dark ? [-0.06, 0.06] : [-0.07, 0.07], readable);
  const subtle = dark ? fill.withAlpha(0x1f) : fill.mix(SlateColor.white, 0.9);

  const legible = (c: SlateColor) => surfaces.every((s) => c.contrastWith(s) >= TEXT);
  const text = nearest(brand, legible) ?? SlateColor.parse(theme.get('color.accent.text')!);
  const linkHover = step(text, dark ? [0.08, -0.08] : [-0.08, 0.08], legible);

  return [
    ['color.accent.default', fill],
    ['color.accent.hover', hover],
    ['color.accent.pressed', pressed],
    ['color.accent.subtle', subtle],
    ['color.accent.text', text],
    ['color.text.onAccent', onAccent],
    ['color.text.link', text],
    ['color.text.linkHover', linkHover],
  ];
}

function nearest(brand: SlateColor, ok: (c: SlateColor) => boolean): SlateColor | null {
  if (ok(brand)) return brand;
  for (let s = 0.005; s <= 1; s += 0.005) {
    const down = brand.adjustLightness(-s);
    if (ok(down)) return down;
    const up = brand.adjustLightness(s);
    if (ok(up)) return up;
  }
  return null;
}

function step(from: SlateColor, deltas: number[], ok: (c: SlateColor) => boolean): SlateColor {
  for (const d of deltas) {
    const c = from.adjustLightness(d);
    if (ok(c) && !c.equals(from)) return c;
  }
  for (const d of deltas) {
    const c = from.adjustLightness(d / 2);
    if (ok(c)) return c;
  }
  return from;
}

function distance(a: SlateColor, b: SlateColor): number {
  return Math.abs(a.toHsl().l - b.toHsl().l);
}

/** CSS custom-property name for a token path (same rule as the generator): color.text.onAccent → --sl-color-text-on-accent. */
export function cssVariable(path: string): string {
  const kebab = (segment: string) => {
    let out = '';
    for (let i = 0; i < segment.length; i++) {
      const c = segment[i];
      if (c === '_') out += '-';
      else if (c >= 'A' && c <= 'Z') {
        if (i > 0 && segment[i - 1] !== '_') out += '-';
        out += c.toLowerCase();
      } else out += c;
    }
    return out;
  };
  return `--sl-${path.split('.').map(kebab).join('-')}`;
}

const applied = new WeakMap<HTMLElement, string[]>();

/**
 * Applies a theme to an element's subtree: sets data-sl-theme to the base and the changed tokens as
 * inline custom properties. Re-applying replaces the previous theme; pass null to remove it.
 */
export function applyTheme(element: HTMLElement, theme: SlateTheme | null): void {
  for (const name of applied.get(element) ?? []) element.style.removeProperty(name);
  applied.delete(element);
  if (!theme) return;

  element.setAttribute('data-sl-theme', theme.base);
  const names: string[] = [];
  for (const path of theme.changedPaths) {
    const name = cssVariable(path);
    element.style.setProperty(name, theme.values[path]);
    names.push(name);
  }
  applied.set(element, names);
}

/** A standalone CSS rule with every value of the theme (no data-sl-theme needed). */
export function themeToCss(theme: SlateTheme, selector = ':root'): string {
  const lines = [`${selector} {`, `  color-scheme: ${theme.isDark ? 'dark' : 'light'};`];
  for (const [path, value] of Object.entries(theme.values)) lines.push(`  ${cssVariable(path)}: ${value};`);
  lines.push('}');
  return lines.join('\n') + '\n';
}
