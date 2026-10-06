import { afterEach, describe, expect, it, vi } from 'vitest';
import type { SlButton } from '../src/components/button';
import type { SlTextField } from '../src/components/text-field';
import type { SlCheckbox, SlRadioGroup, SlSwitch } from '../src/components/selection';
import type { SlAlert, SlIcon, SlProgress, SlText } from '../src/components/display';
import { $, cleanup, fixture, key, settle, shadow } from './helpers';

afterEach(cleanup);

describe('sl-button', () => {
  it('renders the class contract for variant and size', async () => {
    const el = await fixture<SlButton>('<sl-button variant="primary" size="sm">Deploy</sl-button>');
    const btn = $(el, 'button');
    expect(btn.className).toContain('sl-button');
    expect(btn.className).toContain('sl-button--primary');
    expect(btn.className).toContain('sl-button--sm');
    expect(btn.getAttribute('type')).toBe('button');
  });

  it('secondary/medium are the defaults and add no modifiers', async () => {
    const el = await fixture<SlButton>('<sl-button>Save</sl-button>');
    expect($(el, 'button').className.trim().split(/\s+/)).toEqual(['sl-button']);
  });

  it('icon-only buttons are square and labelled', async () => {
    const el = await fixture<SlButton>('<sl-button icon="search" label="Search"></sl-button>');
    const btn = $(el, 'button');
    expect(btn.classList.contains('sl-button--icon')).toBe(true);
    expect(btn.getAttribute('aria-label')).toBe('Search');
    expect(btn.querySelector('svg.sl-icon path')).not.toBeNull();
    expect(btn.querySelector('.sl-button__label')).toBeNull();
  });

  it('warns when an icon-only button has no label', async () => {
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => {});
    await fixture('<sl-button icon="search"></sl-button>');
    expect(warn).toHaveBeenCalled();
    warn.mockRestore();
  });

  it('loading shows a spinner, sets aria-busy and swallows clicks', async () => {
    const el = await fixture<SlButton>('<sl-button loading>Saving</sl-button>');
    const clicks = vi.fn();
    el.addEventListener('click', clicks);
    const btn = $(el, 'button');
    expect(btn.getAttribute('aria-busy')).toBe('true');
    expect(btn.getAttribute('aria-disabled')).toBe('true');
    expect(btn.querySelector('.sl-spinner')).not.toBeNull();
    btn.click();
    expect(clicks).not.toHaveBeenCalled();
  });

  it('disabled sets the native attribute', async () => {
    const el = await fixture<SlButton>('<sl-button disabled>Nope</sl-button>');
    expect(($(el, 'button') as HTMLButtonElement).disabled).toBe(true);
  });

  it('renders the keyboard shortcut hint', async () => {
    const el = await fixture<SlButton>('<sl-button shortcut="⌘↵">Commit</sl-button>');
    expect($(el, 'kbd.sl-kbd').textContent).toBe('⌘↵');
    expect($(el, 'button').getAttribute('aria-keyshortcuts')).toBe('⌘↵');
  });

  it('renders a link when href is set', async () => {
    const el = await fixture<SlButton>('<sl-button href="/docs" variant="link">Docs</sl-button>');
    const a = $(el, 'a');
    expect(a.getAttribute('href')).toBe('/docs');
    expect(a.className).toContain('sl-button--link');
  });

  it('submits and resets its form', async () => {
    const form = await fixture<HTMLFormElement>(`<form>
      <sl-text-field name="q" value="start"></sl-text-field>
      <sl-button type="submit" name="intent" value="save">Go</sl-button>
      <sl-button type="reset">Reset</sl-button>
    </form>`);
    await settle(form);
    const submitted = vi.fn((e: Event) => e.preventDefault());
    form.addEventListener('submit', submitted);
    const [submit, reset] = form.querySelectorAll('sl-button');
    $(submit, 'button').click();
    expect(submitted).toHaveBeenCalledTimes(1);

    const field = form.querySelector('sl-text-field') as SlTextField;
    field.value = 'changed';
    await field.updateComplete;
    $(reset, 'button').click();
    await field.updateComplete;
    expect(field.value).toBe('start');
  });
});

describe('sl-text-field', () => {
  it('associates the label and input', async () => {
    const el = await fixture<SlTextField>('<sl-text-field label="Email" required></sl-text-field>');
    const label = $(el, 'label');
    const input = $(el, 'input');
    expect(label.getAttribute('for')).toBe(input.id);
    expect(label.querySelector('.sl-field__required')).not.toBeNull();
    expect((input as HTMLInputElement).required).toBe(true);
  });

  it('helper text describes the input', async () => {
    const el = await fixture<SlTextField>('<sl-text-field label="Name" helper-text="Lowercase only"></sl-text-field>');
    const input = $(el, 'input');
    const helper = $(el, '.sl-field__helper');
    expect(input.getAttribute('aria-describedby')).toBe(helper.id);
    expect(input.getAttribute('aria-invalid')).toBe('false');
  });

  it('error replaces helper, marks invalid and is exposed to AT', async () => {
    const el = await fixture<SlTextField>('<sl-text-field label="Email" helper-text="h" error="Enter a full address"></sl-text-field>');
    const input = $(el, 'input');
    expect(shadow(el).querySelector('.sl-field__helper')).toBeNull();
    const error = $(el, '.sl-field__error');
    expect(error.textContent).toContain('Enter a full address');
    expect(input.getAttribute('aria-invalid')).toBe('true');
    expect(input.getAttribute('aria-describedby')).toBe(error.id);
    expect($(el, '.sl-field').classList.contains('sl-field--invalid')).toBe(true);
  });

  it('updates value on input and re-dispatches change from the host', async () => {
    const el = await fixture<SlTextField>('<sl-text-field label="Name"></sl-text-field>');
    const changed = vi.fn();
    el.addEventListener('change', changed);
    const input = $(el, 'input') as HTMLInputElement;
    input.value = 'Ada';
    input.dispatchEvent(new Event('input', { bubbles: true, composed: true }));
    expect(el.value).toBe('Ada');
    input.dispatchEvent(new Event('change'));
    expect(changed).toHaveBeenCalledTimes(1);
  });

  it('renders prefix, suffix and start icon', async () => {
    const el = await fixture<SlTextField>('<sl-text-field label="Site" prefix="https://" suffix=".dev" start-icon="search"></sl-text-field>');
    expect($(el, '.sl-field__affix--prefix').textContent).toBe('https://');
    expect($(el, '.sl-field__affix--suffix').textContent).toBe('.dev');
    expect($(el, '.sl-field__icon svg')).toBeTruthy();
  });

  it('multiline renders a textarea with rows', async () => {
    const el = await fixture<SlTextField>('<sl-text-field label="Notes" multiline rows="5"></sl-text-field>');
    expect($(el, 'textarea').getAttribute('rows')).toBe('5');
    expect($(el, '.sl-field').classList.contains('sl-field--multiline')).toBe(true);
  });

  it('size and disabled map to modifiers', async () => {
    const el = await fixture<SlTextField>('<sl-text-field label="x" size="lg" disabled></sl-text-field>');
    const root = $(el, '.sl-field');
    expect(root.classList.contains('sl-field--lg')).toBe(true);
    expect(root.classList.contains('sl-field--disabled')).toBe(true);
    expect(($(el, 'input') as HTMLInputElement).disabled).toBe(true);
  });

});

describe('sl-checkbox / sl-switch', () => {
  it('checkbox toggles, clears indeterminate and fires change', async () => {
    const el = await fixture<SlCheckbox>('<sl-checkbox label="Run tests" indeterminate></sl-checkbox>');
    const input = $(el, 'input') as HTMLInputElement;
    expect(input.indeterminate).toBe(true);
    expect(input.getAttribute('aria-checked')).toBe('mixed');
    const changed = vi.fn();
    el.addEventListener('change', changed);
    input.click();
    await el.updateComplete;
    expect(el.checked).toBe(true);
    expect(el.indeterminate).toBe(false);
    expect(changed).toHaveBeenCalledTimes(1);
    expect(el.hasAttribute('checked')).toBe(true);
  });

  it('checkbox description is linked', async () => {
    const el = await fixture<SlCheckbox>('<sl-checkbox label="Notify" description="Sends to #builds"></sl-checkbox>');
    const desc = $(el, '.sl-checkbox__description');
    expect($(el, 'input').getAttribute('aria-describedby')).toBe(desc.id);
  });

  it('switch uses the switch role and ignores Enter', async () => {
    const el = await fixture<SlSwitch>('<sl-switch label="Preview deployments" checked></sl-switch>');
    const input = $(el, 'input');
    expect(input.getAttribute('role')).toBe('switch');
    expect(input.getAttribute('aria-checked')).toBe('true');
    expect(key(input, 'Enter').defaultPrevented).toBe(true);
    expect($(el, 'label').className).toContain('sl-switch--label-start');
  });

  it('disabled toggles are marked', async () => {
    const el = await fixture<SlSwitch>('<sl-switch label="x" disabled></sl-switch>');
    expect($(el, 'label').classList.contains('is-disabled')).toBe(true);
    expect(($(el, 'input') as HTMLInputElement).disabled).toBe(true);
  });
});

describe('sl-radio-group', () => {
  const markup = `<sl-radio-group label="Plan" name="plan" value="team">
      <sl-radio value="solo" label="Solo"></sl-radio>
      <sl-radio value="team" label="Team"></sl-radio>
      <sl-radio value="ent" label="Enterprise" disabled></sl-radio>
      <sl-radio value="edu" label="Education"></sl-radio>
    </sl-radio-group>`;

  it('exposes radiogroup semantics with one tab stop', async () => {
    const group = await fixture<SlRadioGroup>(markup);
    await settle(group);
    const radios = [...group.querySelectorAll('sl-radio')];
    expect(group.getAttribute('role')).toBe('radiogroup');
    expect(group.getAttribute('aria-label')).toBe('Plan');
    expect(radios.map((r) => r.getAttribute('role'))).toEqual(['radio', 'radio', 'radio', 'radio']);
    expect(radios.map((r) => r.getAttribute('aria-checked'))).toEqual(['false', 'true', 'false', 'false']);
    expect(radios.map((r) => r.tabIndex)).toEqual([-1, 0, -1, -1]);
  });

  it('arrow keys move selection, skipping disabled and wrapping', async () => {
    const group = await fixture<SlRadioGroup>(markup);
    await settle(group);
    const radios = [...group.querySelectorAll('sl-radio')];
    const changes: string[] = [];
    group.addEventListener('change', () => changes.push(group.value));

    key(radios[1], 'ArrowDown');
    expect(group.value).toBe('edu'); // skipped disabled "ent"
    key(radios[3], 'ArrowRight');
    expect(group.value).toBe('solo'); // wrapped
    key(radios[0], 'ArrowUp');
    expect(group.value).toBe('edu');
    expect(changes).toEqual(['edu', 'solo', 'edu']);
  });

  it('click and Space select; disabled radios cannot be chosen', async () => {
    const group = await fixture<SlRadioGroup>(markup);
    await settle(group);
    const radios = [...group.querySelectorAll('sl-radio')];
    radios[0].click();
    expect(group.value).toBe('solo');
    key(radios[3], ' ');
    expect(group.value).toBe('edu');
    radios[2].click();
    expect(group.value).toBe('edu');
  });
});

describe('display components', () => {
  it('sl-icon is decorative unless labelled', async () => {
    const plain = await fixture<SlIcon>('<sl-icon name="check"></sl-icon>');
    expect($(plain, 'svg').getAttribute('aria-hidden')).toBe('true');
    const labelled = await fixture<SlIcon>('<sl-icon name="check" label="Done"></sl-icon>');
    expect($(labelled, 'svg').getAttribute('role')).toBe('img');
    expect($(labelled, 'svg').getAttribute('aria-label')).toBe('Done');
    const unknown = await fixture<SlIcon>('<sl-icon name="nope"></sl-icon>');
    expect(shadow(unknown).querySelector('svg')).toBeNull();
  });

  it('sl-alert maps severity to class, icon and role', async () => {
    const error = await fixture<SlAlert>('<sl-alert severity="error" heading="Build failed">3 tests failed</sl-alert>');
    const base = $(error, '.sl-alert');
    expect(base.classList.contains('sl-alert--error')).toBe(true);
    expect(base.getAttribute('role')).toBe('alert');
    expect($(error, '.sl-alert__title').textContent).toBe('Build failed');

    const info = await fixture<SlAlert>('<sl-alert severity="info">x</sl-alert>');
    expect($(info, '.sl-alert').hasAttribute('role')).toBe(false);
    const live = await fixture<SlAlert>('<sl-alert severity="success" live>x</sl-alert>');
    expect($(live, '.sl-alert').getAttribute('role')).toBe('status');
  });

  it('sl-alert dismiss is cancelable', async () => {
    const el = await fixture<SlAlert>('<sl-alert dismissible>x</sl-alert>');
    const close = $(el, '.sl-alert__close');
    const handler = vi.fn((e: Event) => e.preventDefault());
    el.addEventListener('sl-dismiss', handler);
    close.click();
    expect(el.hidden).toBe(false);
    el.removeEventListener('sl-dismiss', handler);
    close.click();
    expect(el.hidden).toBe(true);
  });

  it('sl-badge renders tone and dot', async () => {
    const el = await fixture('<sl-badge tone="success" dot>Ready</sl-badge>');
    expect($(el, '.sl-badge').className).toContain('sl-badge--success');
    expect($(el, '.sl-badge__dot')).toBeTruthy();
  });

  it('sl-progress is determinate with a value and indeterminate without', async () => {
    const det = await fixture<SlProgress>('<sl-progress value="40" label="Upload"></sl-progress>');
    const bar = $(det, '[role="progressbar"]');
    expect(bar.getAttribute('aria-valuenow')).toBe('40');
    expect(bar.getAttribute('aria-label')).toBe('Upload');
    expect(bar.style.getPropertyValue('--_value')).toBe('40%');

    const ind = await fixture<SlProgress>('<sl-progress></sl-progress>');
    const bar2 = $(ind, '[role="progressbar"]');
    expect(bar2.hasAttribute('aria-valuenow')).toBe(false);
    expect(bar2.classList.contains('sl-progress--indeterminate')).toBe(true);
  });

  it('sl-progress clamps out-of-range values', async () => {
    const el = await fixture<SlProgress>('<sl-progress value="140"></sl-progress>');
    expect($(el, '[role="progressbar"]').getAttribute('aria-valuenow')).toBe('100');
  });

  it('sl-text renders the requested tag and classes', async () => {
    const el = await fixture<SlText>('<sl-text variant="h2" tone="secondary" as="h2">Title</sl-text>');
    const h2 = $(el, 'h2');
    expect(h2.className).toContain('sl-text--h2');
    expect(h2.className).toContain('sl-text--secondary');
    const unsafe = await fixture<SlText>('<sl-text as="script">x</sl-text>');
    expect(shadow(unsafe).querySelector('script')).toBeNull();
  });
});

describe('icons inside components', () => {
  it('every component that renders icons ships the icon styles', async () => {
    const { elements } = await import('../src/index');
    let checked = 0;
    for (const [tag, ctor] of Object.entries(elements)) {
      const css = ([] as unknown[]).concat((ctor as unknown as { styles?: unknown }).styles ?? []).map(String).join('\n');
      const source = ctor.toString();
      if (/renderIcon\b|<svg/.test(source)) {
        checked++;
        // The base .sl-icon rule (size + stroke) from typography.css, not just a descendant selector.
        expect.soft(/\.sl-icon\s*\{[^}]*stroke:/.test(css), `${tag} renders icons but lacks the base .sl-icon rule`).toBe(true);
      }
    }
    expect(checked).toBeGreaterThanOrEqual(6);
  });

  it('rendered icons carry stroke attributes even without CSS', async () => {
    const el = await fixture('<sl-button icon="x" label="Close"></sl-button>');
    const svg = $(el, 'svg');
    expect(svg.getAttribute('stroke')).toBe('currentColor');
    expect(svg.getAttribute('fill')).toBe('none');
  });
});
