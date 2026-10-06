/*
 * Form association. happy-dom has no ElementInternals, so these tests install a recording fake that
 * behaves like the browser's: it exercises every call our components make (setFormValue, setValidity,
 * form, checkValidity) exactly as a browser would receive them.
 */
import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest';
import type { SlTextField } from '../src/components/text-field';
import type { SlCheckbox, SlRadioGroup, SlSwitch } from '../src/components/selection';
import { $, cleanup, fixture, settle } from './helpers';

class FakeInternals {
  value: string | null = null;
  flags: ValidityStateFlags = {};
  message = '';
  constructor(private readonly host: HTMLElement) {}
  get form(): HTMLFormElement | null {
    return this.host.closest('form');
  }
  setFormValue(value: string | null): void {
    this.value = value;
  }
  setValidity(flags: ValidityStateFlags = {}, message = ''): void {
    this.flags = { ...flags };
    this.message = message;
  }
  get validity(): Partial<ValidityState> {
    return { ...this.flags, valid: this.valid };
  }
  get validationMessage(): string {
    return this.message;
  }
  get valid(): boolean {
    return !Object.values(this.flags).some(Boolean);
  }
  checkValidity(): boolean {
    return this.valid;
  }
  reportValidity(): boolean {
    return this.valid;
  }
}

const original = Object.getOwnPropertyDescriptor(HTMLElement.prototype, 'attachInternals');

beforeAll(() => {
  Object.defineProperty(HTMLElement.prototype, 'attachInternals', {
    configurable: true,
    value(this: HTMLElement) {
      return new FakeInternals(this);
    },
  });
});

afterAll(() => {
  if (original) Object.defineProperty(HTMLElement.prototype, 'attachInternals', original);
  else delete (HTMLElement.prototype as { attachInternals?: unknown }).attachInternals;
});

afterEach(cleanup);

const internals = (el: Element) => (el as unknown as { internals: FakeInternals }).internals;

describe('form association', () => {
  it('text field submits its value and reports required/custom validity', async () => {
    const el = await fixture<SlTextField>('<sl-text-field label="Name" name="name" required></sl-text-field>');
    expect(internals(el).value).toBe('');
    expect(internals(el).flags.valueMissing).toBe(true);
    expect(el.checkValidity()).toBe(false);

    el.value = 'Ada';
    await el.updateComplete;
    expect(internals(el).value).toBe('Ada');
    expect(el.checkValidity()).toBe(true);

    el.error = 'Taken';
    await el.updateComplete;
    expect(internals(el).flags.customError).toBe(true);
    expect(el.validationMessage).toBe('Taken');
  });

  it('text field resets to its initial value', async () => {
    const el = await fixture<SlTextField>('<sl-text-field label="x" value="start"></sl-text-field>');
    el.value = 'changed';
    el.formResetCallback();
    await el.updateComplete;
    expect(el.value).toBe('start');
  });

  it('checkbox submits its value only when checked, and validates required', async () => {
    const el = await fixture<SlCheckbox>('<sl-checkbox name="tos" value="yes" required label="Accept"></sl-checkbox>');
    expect(internals(el).value).toBeNull();
    expect(internals(el).flags.valueMissing).toBe(true);
    ($(el, 'input') as HTMLInputElement).click();
    await el.updateComplete;
    expect(internals(el).value).toBe('yes');
    expect(el.checkValidity()).toBe(true);
  });

  it('switch defaults to "on" and resets to its initial state', async () => {
    const el = await fixture<SlSwitch>('<sl-switch name="beta" checked label="Beta"></sl-switch>');
    expect(internals(el).value).toBe('on');
    el.checked = false;
    await el.updateComplete;
    expect(internals(el).value).toBeNull();
    el.formResetCallback();
    await el.updateComplete;
    expect(el.checked).toBe(true);
  });

  it('radio group submits the selected value and disables with its fieldset', async () => {
    const group = await fixture<SlRadioGroup>(`<sl-radio-group name="plan" value="a" required>
        <sl-radio value="a" label="A"></sl-radio><sl-radio value="b" label="B"></sl-radio>
      </sl-radio-group>`);
    await settle(group);
    expect(internals(group).value).toBe('a');
    group.value = '';
    await group.updateComplete;
    expect(internals(group).flags.valueMissing).toBe(true);
    group.formDisabledCallback(true);
    await group.updateComplete;
    expect([...group.querySelectorAll('sl-radio')].map((r) => r.tabIndex)).toEqual([-1, -1]);
  });
});
