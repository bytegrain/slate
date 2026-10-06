// Port of DialogStackTests / BreakpointTests in tests/Slate.Core.Tests/DialogAndLayoutTests.cs.
import { describe, expect, it } from 'vitest';
import { DialogResult, DialogStack, dialogWidthPixels } from '../src/core/dialog-stack';
import { breakpointFromWidth, resolveSpan, spaceVar } from '../src/core/layout';

describe('DialogStack', () => {
  it('push adds to top and raises changed', () => {
    const stack = new DialogStack();
    let changes = 0;
    stack.onChanged(() => changes++);
    const a = stack.push('a');
    const b = stack.push('b');
    expect(stack.open).toEqual([a, b]);
    expect(stack.top).toBe(b);
    expect(changes).toBe(2);
    expect(a.isOpen).toBe(true);
  });

  it('close completes the result', async () => {
    const stack = new DialogStack();
    const d = stack.push('content');
    expect(stack.close(d, DialogResult.ok(42))).toBe(true);
    const result = await d.result;
    expect(result.canceled).toBe(false);
    expect(result.data).toBe(42);
    expect(d.isOpen).toBe(false);
    expect(stack.top).toBeNull();
  });

  it('close without result cancels and a second close is ignored', async () => {
    const stack = new DialogStack();
    const d = stack.push('x');
    stack.close(d);
    expect(stack.close(d, DialogResult.ok())).toBe(false);
    expect((await d.result).canceled).toBe(true);
  });

  it('escape only closes the top dialog when allowed', () => {
    const stack = new DialogStack();
    const bottom = stack.push('a');
    const locked = stack.push('b', { closeOnEscape: false });
    expect(stack.handleEscape()).toBe(false);
    expect(stack.top).toBe(locked);
    stack.close(locked);
    expect(stack.handleEscape()).toBe(true);
    expect(bottom.isOpen).toBe(false);
    expect(stack.handleEscape()).toBe(false);
  });

  it('backdrop click only affects the top dialog that allows it', () => {
    const stack = new DialogStack();
    const a = stack.push('a');
    const b = stack.push('b', { closeOnBackdropClick: false });
    expect(stack.handleBackdropClick(a)).toBe(false);
    expect(stack.handleBackdropClick(b)).toBe(false);
    stack.close(b);
    expect(stack.handleBackdropClick(a)).toBe(true);
  });

  it('close all cancels everything', async () => {
    const stack = new DialogStack();
    const a = stack.push('a');
    const b = stack.push('b');
    stack.closeAll();
    expect(stack.open).toEqual([]);
    expect((await a.result).canceled).toBe(true);
    expect((await b.result).canceled).toBe(true);
  });

  it('result continuations do not run inline with close', () => {
    const stack = new DialogStack();
    const d = stack.push('x');
    let ran = false;
    void d.result.then(() => (ran = true));
    stack.close(d);
    expect(ran).toBe(false);
  });

  it.each([
    ['xs', 360],
    ['sm', 480],
    ['xl', 1120],
  ] as const)('dialog width %s comes from tokens (%ipx)', (w, px) => {
    expect(dialogWidthPixels(w)).toBe(px);
  });

  it('requires content', () => {
    expect(() => new DialogStack().push(null)).toThrow(TypeError);
  });
});

describe('breakpoints and grid spans', () => {
  it.each([
    [0, 'xs'],
    [599.9, 'xs'],
    [600, 'sm'],
    [899, 'sm'],
    [900, 'md'],
    [1200, 'lg'],
    [1536, 'xl'],
    [4000, 'xl'],
  ] as const)('width %d maps to %s', (w, bp) => {
    expect(breakpointFromWidth(w)).toBe(bp);
  });

  it.each([-1, Number.NaN])('invalid width %d throws', (w) => {
    expect(() => breakpointFromWidth(w)).toThrow(RangeError);
  });

  it('spans inherit upwards mobile-first', () => {
    const span = { xs: 12, md: 6 };
    expect(resolveSpan(span, 'xs')).toBe(12);
    expect(resolveSpan(span, 'sm')).toBe(12);
    expect(resolveSpan(span, 'md')).toBe(6);
    expect(resolveSpan(span, 'xl')).toBe(6);
  });

  it('unset span is full width', () => {
    expect(resolveSpan({}, 'lg')).toBe(12);
    expect(resolveSpan({ lg: 4 }, 'xl')).toBe(4);
    expect(resolveSpan({ lg: 4 }, 'md')).toBe(12);
  });

  it.each([0, 13, 2.5])('span %d is rejected', (n) => {
    expect(() => resolveSpan({ xs: n }, 'xs')).toThrow(RangeError);
  });

  it('space steps map to token variables', () => {
    expect(spaceVar(4)).toBe('var(--sl-space-4)');
    expect(spaceVar('0.5')).toBe('var(--sl-space-0-5)');
    expect(() => spaceVar(7)).toThrow(RangeError);
  });
});
