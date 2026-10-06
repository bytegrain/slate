import { describe, expect, it } from 'vitest';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { execFileSync } from 'node:child_process';
import { applyTheme, createTheme, cssVariable, themeToCss } from '../src/core/theme/builder';
import { SlateColor, roundHalfEven } from '../src/core/theme/color';
import { themeValues } from '../src/core/theme/data.generated';

const repo = resolve(import.meta.dirname, '../../..');

interface FixtureCase {
  options: { base: 'light' | 'dark'; accent: string | null; radiusScale: number; fontFamily: string | null; overrides: Record<string, string> };
  changed: Record<string, string>;
}

const fixture: Record<string, FixtureCase> = JSON.parse(readFileSync(resolve(repo, 'tests/fixtures/theme-builder.json'), 'utf8'));

describe('createTheme matches Slate.Core ThemeBuilder byte-for-byte', () => {
  for (const [id, { options, changed }] of Object.entries(fixture)) {
    it(id, () => {
      const theme = createTheme({
        base: options.base,
        accent: options.accent ?? undefined,
        radiusScale: options.radiusScale,
        fontFamily: options.fontFamily ?? undefined,
        overrides: options.overrides,
      });
      const actual = Object.fromEntries([...theme.changedPaths].sort().map((p) => [p, theme.values[p]]));
      expect(JSON.stringify(actual)).toBe(JSON.stringify(changed));
    });
  }
});

describe('theme data', () => {
  it('generated data is in sync with design/dist/tokens.resolved.json', () => {
    expect(() => execFileSync('node', [resolve(import.meta.dirname, '../scripts/sync-theme-data.mjs'), '--check'], { stdio: 'pipe' })).not.toThrow();
  });
});

describe('SlateColor', () => {
  it('parses and formats', () => {
    expect(SlateColor.parse('abc').toHex()).toBe('#AABBCC');
    expect(SlateColor.parse('#2BD4A424').toHex()).toBe('#2BD4A424');
    expect(SlateColor.parse('#2BD4A424').toArgbHex()).toBe('#242BD4A4');
    expect(SlateColor.tryParse('#12')).toBeNull();
    expect(SlateColor.tryParse('')).toBeNull();
  });

  it('computes WCAG contrast', () => {
    expect(SlateColor.parse('#767676').contrastWith(SlateColor.white)).toBeCloseTo(4.54, 2);
    expect(SlateColor.black.contrastWith(SlateColor.white)).toBeCloseTo(21, 5);
  });

  it('rounds midpoints to even like .NET', () => {
    expect(roundHalfEven(2.5)).toBe(2);
    expect(roundHalfEven(3.5)).toBe(4);
    expect(roundHalfEven(2.4)).toBe(2);
    expect(roundHalfEven(2.6)).toBe(3);
  });
});

describe('createTheme guarantees', () => {
  const surfaces = ['color.background.canvas', 'color.background.surface', 'color.background.sunken', 'color.background.raised', 'color.background.subtle'];
  let seed = 20261006;
  const rand = () => ((seed = (seed * 1103515245 + 12345) & 0x7fffffff) / 0x7fffffff);
  const brands = [
    '#FF0000', '#00FF00', '#0000FF', '#FFFF00', '#00FFFF', '#FF00FF', '#FFFFFF', '#000000', '#808080',
    ...Array.from({ length: 40 }, () => `#${Math.floor(rand() * 0xffffff).toString(16).padStart(6, '0')}`),
  ];

  for (const base of ['light', 'dark'] as const) {
    it(`any brand colour is accessible on ${base}`, () => {
      for (const accent of brands) {
        const t = createTheme({ base, accent });
        const c = (p: string) => SlateColor.parse(t.values[p]);
        const on = c('color.text.onAccent');
        for (const s of ['color.accent.default', 'color.accent.hover', 'color.accent.pressed']) {
          expect(on.contrastWith(c(s)), `${accent} ${s}`).toBeGreaterThanOrEqual(4.5);
        }
        for (const s of surfaces) {
          expect(c('color.accent.default').contrastWith(c(s)), `${accent} fill/${s}`).toBeGreaterThanOrEqual(3);
          expect(c('color.text.link').contrastWith(c(s)), `${accent} link/${s}`).toBeGreaterThanOrEqual(4.5);
        }
      }
    });
  }

  it('empty options reproduce the base theme', () => {
    const t = createTheme({ base: 'dark' });
    expect(t.changedPaths).toEqual([]);
    expect(t.values['color.background.canvas']).toBe(new Map(themeValues.dark).get('color.background.canvas'));
  });

  it('propagates through component aliases and respects explicit overrides', () => {
    const t = createTheme({ radiusScale: 2, overrides: { 'component.button.radius': '999px' } });
    expect(t.values['component.card.radius']).toBe('20px');
    expect(t.values['component.button.radius']).toBe('999px');
    expect(createTheme({ accent: '#5B3DF5' }).values['component.selection.checked']).toBe(createTheme({ accent: '#5B3DF5' }).values['color.accent.default']);
  });

  it('rejects invalid options', () => {
    expect(() => createTheme({ base: 'sepia' as never })).toThrow();
    expect(() => createTheme({ accent: 'teal' })).toThrow();
    expect(() => createTheme({ accent: '#0E5E6F80' })).toThrow();
    expect(() => createTheme({ radiusScale: -1 })).toThrow(RangeError);
    expect(() => createTheme({ overrides: { 'color.nope': '#000' } })).toThrow();
    expect(() => createTheme({ overrides: { 'color.background.canvas': 'blue' } })).toThrow();
  });
});

describe('applying themes', () => {
  it('names variables like the generator', () => {
    expect(cssVariable('color.text.onAccent')).toBe('--sl-color-text-on-accent');
    expect(cssVariable('space.0_5')).toBe('--sl-space-0-5');
    expect(cssVariable('component.button.paddingSm')).toBe('--sl-component-button-padding-sm');
  });

  it('applyTheme sets the base and changed tokens, and replaces a previous theme', () => {
    const el = document.createElement('div');
    applyTheme(el, createTheme({ base: 'dark', accent: '#FF5A1F', radiusScale: 0 }));
    expect(el.getAttribute('data-sl-theme')).toBe('dark');
    expect(el.style.getPropertyValue('--sl-radius-md')).toBe('0px');
    expect(el.style.getPropertyValue('--sl-color-accent-default')).not.toBe('');

    applyTheme(el, createTheme({ fontFamily: 'Inter' }));
    expect(el.style.getPropertyValue('--sl-radius-md')).toBe('');
    expect(el.style.getPropertyValue('--sl-font-family-ui')).toBe('Inter');
    applyTheme(el, null);
    expect(el.style.getPropertyValue('--sl-font-family-ui')).toBe('');
  });

  it('themeToCss emits a standalone rule', () => {
    const css = themeToCss(createTheme({ base: 'dark' }), '.acme');
    expect(css.startsWith('.acme {\n  color-scheme: dark;')).toBe(true);
    expect(css).toContain('--sl-color-background-canvas: #0D1014;');
  });
});
