/**
 * Port of Slate.Core's SlateColor. Arithmetic (including .NET's round-half-to-even) matches the C#
 * implementation exactly so createTheme() produces byte-identical values on every platform.
 */

/** .NET Math.Round(double): midpoints round to the even neighbour. */
export function roundHalfEven(x: number): number {
  const f = Math.floor(x);
  const d = x - f;
  if (d > 0.5) return f + 1;
  if (d < 0.5) return f;
  return f % 2 === 0 ? f : f + 1;
}

const clamp = (v: number, lo: number, hi: number) => Math.min(hi, Math.max(lo, v));
const hex2 = (n: number) => n.toString(16).toUpperCase().padStart(2, '0');

export class SlateColor {
  static readonly white = new SlateColor(255, 255, 255);
  static readonly black = new SlateColor(0, 0, 0);

  constructor(
    readonly r: number,
    readonly g: number,
    readonly b: number,
    readonly a = 255,
  ) {}

  /** Parses #RGB, #RRGGBB or #RRGGBBAA (CSS order). Throws on invalid input. */
  static parse(hex: string): SlateColor {
    const c = SlateColor.tryParse(hex);
    if (!c) throw new Error(`'${hex}' is not a #RGB, #RRGGBB or #RRGGBBAA colour.`);
    return c;
  }

  static tryParse(hex: string | null | undefined): SlateColor | null {
    if (!hex || !hex.trim()) return null;
    let s = hex.trim().replace(/^#+/, '');
    if (s.length === 3) s = [...s].map((ch) => ch + ch).join('');
    if ((s.length !== 6 && s.length !== 8) || !/^[0-9a-fA-F]+$/.test(s)) return null;
    const at = (i: number) => Number.parseInt(s.slice(i, i + 2), 16);
    return new SlateColor(at(0), at(2), at(4), s.length === 8 ? at(6) : 255);
  }

  get isOpaque(): boolean {
    return this.a === 255;
  }

  /** #RRGGBB, or #RRGGBBAA when translucent. */
  toHex(): string {
    const rgb = `#${hex2(this.r)}${hex2(this.g)}${hex2(this.b)}`;
    return this.isOpaque ? rgb : rgb + hex2(this.a);
  }

  /** #AARRGGBB (XAML order). */
  toArgbHex(): string {
    return `#${hex2(this.a)}${hex2(this.r)}${hex2(this.g)}${hex2(this.b)}`;
  }

  equals(other: SlateColor): boolean {
    return this.r === other.r && this.g === other.g && this.b === other.b && this.a === other.a;
  }

  withAlpha(alpha: number): SlateColor {
    return new SlateColor(this.r, this.g, this.b, alpha);
  }

  relativeLuminance(): number {
    const lin = (c: number) => {
      const v = c / 255;
      return v <= 0.03928 ? v / 12.92 : Math.pow((v + 0.055) / 1.055, 2.4);
    };
    return 0.2126 * lin(this.r) + 0.7152 * lin(this.g) + 0.0722 * lin(this.b);
  }

  /** WCAG 2.x contrast ratio; translucent colours are composited first. */
  contrastWith(background: SlateColor): number {
    const bg = background.isOpaque ? background : background.over(SlateColor.white);
    const fg = this.isOpaque ? this : this.over(bg);
    const a = fg.relativeLuminance();
    const b = bg.relativeLuminance();
    return (Math.max(a, b) + 0.05) / (Math.min(a, b) + 0.05);
  }

  over(backdrop: SlateColor): SlateColor {
    const a = this.a / 255;
    const mix = (f: number, b: number) => roundHalfEven(f * a + b * (1 - a));
    return new SlateColor(mix(this.r, backdrop.r), mix(this.g, backdrop.g), mix(this.b, backdrop.b));
  }

  /** Linear RGB mix: 0 = this, 1 = other. */
  mix(other: SlateColor, amount: number): SlateColor {
    const t = clamp(amount, 0, 1);
    const m = (x: number, y: number) => roundHalfEven(x + (y - x) * t);
    return new SlateColor(m(this.r, other.r), m(this.g, other.g), m(this.b, other.b), m(this.a, other.a));
  }

  adjustLightness(delta: number): SlateColor {
    const { h, s, l } = this.toHsl();
    return SlateColor.fromHsl(h, s, clamp(l + delta, 0, 1), this.a);
  }

  toHsl(): { h: number; s: number; l: number } {
    const r = this.r / 255;
    const g = this.g / 255;
    const b = this.b / 255;
    const max = Math.max(r, Math.max(g, b));
    const min = Math.min(r, Math.min(g, b));
    const l = (max + min) / 2;
    let h = 0;
    let s = 0;
    if (max !== min) {
      const d = max - min;
      s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
      h = max === r ? (g - b) / d + (g < b ? 6 : 0) : max === g ? (b - r) / d + 2 : (r - g) / d + 4;
      h /= 6;
    }
    return { h, s, l };
  }

  static fromHsl(h: number, s: number, l: number, alpha = 255): SlateColor {
    let r: number;
    let g: number;
    let b: number;
    if (s === 0) {
      r = g = b = l;
    } else {
      const hue = (p: number, q: number, t: number) => {
        if (t < 0) t += 1;
        if (t > 1) t -= 1;
        return t < 1 / 6 ? p + (q - p) * 6 * t : t < 0.5 ? q : t < 2 / 3 ? p + (q - p) * (2 / 3 - t) * 6 : p;
      };
      const q = l < 0.5 ? l * (1 + s) : l + s - l * s;
      const p = 2 * l - q;
      r = hue(p, q, h + 1 / 3);
      g = hue(p, q, h);
      b = hue(p, q, h - 1 / 3);
    }
    const to = (v: number) => roundHalfEven(clamp(v, 0, 1) * 255);
    return new SlateColor(to(r), to(g), to(b), alpha);
  }

  toString(): string {
    return this.toHex();
  }
}
