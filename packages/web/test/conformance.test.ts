import { afterEach, describe, expect, it } from 'vitest';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import type { LitElement } from 'lit';
import { cleanup, fixture, settle } from './helpers';

/**
 * Conformance with the canonical component API (design/api/components.json): every option exists as a
 * reactive property under its web spelling, every documented part renders, enum values are accepted and
 * reach the rendered markup, and documented defaults hold.
 */

interface OptionSpec {
  kind: 'param' | 'bool' | 'slot' | 'event';
  type?: string;
  default?: string | number | boolean;
  platforms?: string[];
}
interface ComponentSpec {
  web: string;
  options: Record<string, OptionSpec>;
  parts?: string[];
}
interface ApiSpec {
  enums: Record<string, string[]>;
  components: Record<string, ComponentSpec>;
}

const api: ApiSpec = JSON.parse(readFileSync(resolve(import.meta.dirname, '../../../design/api/components.json'), 'utf8'));

/** Web spellings that differ from camelCase(canonical) — each one documented in docs/design/css-classes.md. */
const propertyExceptions: Record<string, string> = {
  'TextField.Prefix': 'prefixText', // Element.prefix is reserved by the DOM
  'TextField.Suffix': 'suffixText', // kept symmetrical with prefixText
};

/** Markup that renders every documented part (some parts are conditional). */
const partFixtures: Record<string, string[]> = {
  Button: ['<sl-button loading start-icon="plus" end-icon="x">Go</sl-button>'],
  TextField: [
    '<sl-text-field label="L" helper-text="h" prefix="a" suffix="b"></sl-text-field>',
    '<sl-text-field label="L" error="e"></sl-text-field>',
  ],
  Checkbox: ['<sl-checkbox label="L" description="d"></sl-checkbox>'],
  Switch: ['<sl-switch label="L" description="d"></sl-switch>'],
  Alert: ['<sl-alert title="T" dismissible>m</sl-alert>'],
  Card: ['<sl-card title="T" subtitle="S">b<div slot="footer">f</div></sl-card>'],
  Dialog: ['<sl-dialog title="T"><div slot="footer">f</div></sl-dialog>'],
};

/** Markup used to check enum values; children are included so delegated options (drawer, radios) are visible. */
const enumFixtures: Record<string, string> = {
  AppShell: '<sl-app-shell><sl-drawer slot="drawer"></sl-drawer></sl-app-shell>',
  RadioGroup: '<sl-radio-group><sl-radio value="a" label="A"></sl-radio></sl-radio-group>',
  Dialog: '<sl-dialog title="T" icon="info"></sl-dialog>',
};

/** Options whose value is not visible in rendered markup by design. */
const invisibleValues = new Set(['Button.Type']);

const camel = (name: string) => name[0].toLowerCase() + name.slice(1);
const webProperty = (component: string, option: string) => propertyExceptions[`${component}.${option}`] ?? camel(option);
const kebab = (name: string) => name.replace(/([a-z0-9])([A-Z])/g, '$1-$2').toLowerCase();

function valuesOf(type: string | undefined): string[] | null {
  if (!type) return null;
  if (api.enums[type]) return api.enums[type];
  if (type.includes('|')) return type.split('|');
  return null;
}

/** Shadow markup of an element and of its light-DOM descendants. */
function renderedMarkup(el: Element): string {
  const parts = [el.shadowRoot?.innerHTML ?? ''];
  for (const child of el.querySelectorAll('*')) parts.push(child.shadowRoot?.innerHTML ?? '');
  return parts.join('\n');
}

afterEach(cleanup);

describe('canonical component API (design/api/components.json)', () => {
  for (const [name, spec] of Object.entries(api.components).filter(([, s]) => (s as { status?: string }).status !== 'planned')) {
    const tag = spec.web;

    describe(`${name} → <${tag}>`, () => {
      const options = Object.entries(spec.options).filter(([, o]) => !o.platforms || o.platforms.includes('web'));

      it('is registered', () => {
        expect(customElements.get(tag), `${tag} is not defined`).toBeDefined();
      });

      it('exposes every param/flag as a reactive property', () => {
        const ctor = customElements.get(tag) as unknown as typeof LitElement;
        document.createElement(tag); // finalizes the class
        const missing = options
          .filter(([, o]) => o.kind === 'param' || o.kind === 'bool')
          .map(([option]) => webProperty(name, option))
          .filter((prop) => !ctor.elementProperties.has(prop));
        expect(missing).toEqual([]);
      });

      it('renders every slot', async () => {
        const el = await fixture(enumFixtures[name] ?? `<${tag}></${tag}>`);
        const slots = [...(el.shadowRoot?.querySelectorAll('slot') ?? [])].map((s) => s.getAttribute('name') ?? '');
        const missing = options
          .filter(([, o]) => o.kind === 'slot')
          .map(([option]) => (option === 'Content' ? '' : kebab(option)))
          .filter((slot) => !slots.includes(slot));
        expect(missing).toEqual([]);
      });

      if (spec.parts?.length) {
        it('renders every documented part', async () => {
          const found = new Set<string>();
          for (const markup of partFixtures[name] ?? [`<${tag}></${tag}>`]) {
            const el = await fixture(markup);
            for (const p of el.shadowRoot?.querySelectorAll('[part]') ?? []) {
              for (const token of p.getAttribute('part')!.split(/\s+/)) found.add(token);
            }
            cleanup();
          }
          expect(spec.parts!.filter((p) => !found.has(p))).toEqual([]);
        });
      }

      for (const [option, o] of options) {
        const values = valuesOf(o.type);
        if (!values || o.kind !== 'param') continue;

        it(`${option}: accepts ${values.join(' | ')}`, async () => {
          const el = (await fixture(enumFixtures[name] ?? `<${tag}></${tag}>`)) as LitElement & Record<string, unknown>;
          const prop = webProperty(name, option);
          for (const value of values) {
            el[prop] = value;
            await settle(el.parentElement!);
            expect(el[prop]).toBe(value);
            if (value !== String(o.default) && !invisibleValues.has(`${name}.${option}`)) {
              expect(renderedMarkup(el), `${name}.${option}=${value} is not reflected in the markup`).toContain(value);
            }
          }
        });
      }

      const withDefaults = options.filter(([, o]) => o.default !== undefined && (o.kind === 'param' || o.kind === 'bool'));
      if (withDefaults.length) {
        it('has the documented defaults', async () => {
          const el = (await fixture(`<${tag}></${tag}>`)) as LitElement & Record<string, unknown>;
          const resolved = (el.resolved as Record<string, unknown> | undefined) ?? {};
          for (const [option, o] of withDefaults) {
            const prop = webProperty(name, option);
            const actual = resolved[prop] ?? el[prop];
            expect(actual, `${name}.${option}`).toBe(o.default);
          }
        });
      }
    });
  }
});
