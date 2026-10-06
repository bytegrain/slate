import { readdirSync, readFileSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { describe, expect, it } from 'vitest';
import { tokenNames, tokenNumber, tokenValue } from '../src/core/tokens';
import { icons } from '../src/icons/generated/icons';

const stylesDir = resolve(__dirname, '../src/styles');
const sources = [
  ...readdirSync(stylesDir).filter((f) => f.endsWith('.css')).map((f) => join(stylesDir, f)),
  ...readdirSync(resolve(__dirname, '../src/components')).map((f) => resolve(__dirname, '../src/components', f)),
];

describe('design tokens', () => {
  it('every var(--sl-…) used by the styles and components exists in tokens.css', () => {
    const missing: string[] = [];
    for (const file of sources) {
      for (const m of readFileSync(file, 'utf8').matchAll(/var\((--sl-[a-z0-9-]+)/g)) {
        if (!tokenNames.has(m[1])) missing.push(`${file.split('/').slice(-2).join('/')}: ${m[1]}`);
      }
    }
    expect(missing).toEqual([]);
  });

  it('styles never hard-code token-able colours', () => {
    const offenders: string[] = [];
    for (const file of sources.filter((f) => f.endsWith('.css'))) {
      const text = readFileSync(file, 'utf8').replace(/\/\*[\s\S]*?\*\//g, '');
      for (const m of text.matchAll(/#[0-9a-fA-F]{3,8}\b/g)) offenders.push(`${file}: ${m[0]}`);
    }
    expect(offenders).toEqual([]);
  });

  it('reads values from the generated file', () => {
    expect(tokenValue('--sl-space-4')).toBe('16px');
    expect(tokenNumber('--sl-size-control-compact-md')).toBe(32);
    expect(tokenValue('--sl-color-background-canvas')).toBe('#F6F7F9'); // light (default) theme wins
    expect(() => tokenValue('--sl-nope')).toThrow();
  });

  it('ships the icons components depend on', () => {
    for (const name of ['check', 'x', 'menu', 'info', 'check-circle', 'alert-triangle', 'alert-circle']) {
      expect(icons).toHaveProperty(name);
    }
  });
});
