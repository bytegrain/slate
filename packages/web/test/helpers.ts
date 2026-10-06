import type { LitElement } from 'lit';

/** Mounts markup into the document and waits for every Lit element in it to render. */
export async function fixture<T extends Element = HTMLElement>(markup: string): Promise<T> {
  const wrapper = document.createElement('div');
  wrapper.innerHTML = markup.trim();
  document.body.append(wrapper);
  await settle(wrapper);
  return wrapper.firstElementChild as T;
}

export async function settle(root: ParentNode = document.body): Promise<void> {
  for (let i = 0; i < 3; i++) {
    const els = [...root.querySelectorAll('*')].filter((e): e is LitElement => 'updateComplete' in e);
    await Promise.all(els.map((e) => e.updateComplete));
  }
}

export function shadow(el: Element): ShadowRoot {
  if (!el.shadowRoot) throw new Error(`${el.tagName} has no shadow root`);
  return el.shadowRoot;
}

export function $(el: Element, selector: string): HTMLElement {
  const found = shadow(el).querySelector<HTMLElement>(selector);
  if (!found) throw new Error(`${selector} not found in ${el.tagName}`);
  return found;
}

export function cleanup(): void {
  document.body.innerHTML = '';
}

export function key(target: EventTarget, k: string): KeyboardEvent {
  const e = new KeyboardEvent('keydown', { key: k, bubbles: true, composed: true, cancelable: true });
  target.dispatchEvent(e);
  return e;
}
