let counter = 0;

/** Unique id for wiring label/description relationships inside a shadow root. */
export function uid(prefix: string): string {
  return `${prefix}-${++counter}`;
}

/** The focused element, looking through open shadow roots. */
export function deepActiveElement(root: Document | ShadowRoot = document): Element | null {
  let active = root.activeElement;
  while (active?.shadowRoot?.activeElement) active = active.shadowRoot.activeElement;
  return active;
}

const focusableSelector = [
  'a[href]',
  'button:not([disabled])',
  'input:not([disabled]):not([type="hidden"])',
  'select:not([disabled])',
  'textarea:not([disabled])',
  '[tabindex]:not([tabindex="-1"])',
  'sl-button:not([disabled])',
  'sl-text-field:not([disabled])',
  'sl-checkbox:not([disabled])',
  'sl-switch:not([disabled])',
  'sl-radio-group:not([disabled])',
].join(',');

/** First keyboard-focusable light-DOM descendant (autofocus wins). */
export function firstFocusable(root: ParentNode): HTMLElement | null {
  const auto = root.querySelector<HTMLElement>('[autofocus]');
  if (auto) return auto;
  return root.querySelector<HTMLElement>(focusableSelector);
}

/** True when the user asked the OS for reduced motion. */
export function prefersReducedMotion(): boolean {
  return typeof matchMedia === 'function' && matchMedia('(prefers-reduced-motion: reduce)').matches;
}

/** Parses boolean attributes that default to true: absent or anything but "false" → true. */
export const defaultTrue = {
  fromAttribute: (value: string | null) => value !== 'false',
  toAttribute: (value: boolean) => (value ? null : 'false'),
};
