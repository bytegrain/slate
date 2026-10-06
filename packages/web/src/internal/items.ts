import { html, type TemplateResult } from 'lit';
import type { TextRange } from '../core/collections/list';

/**
 * Items for Select, SegmentedControl and similar: plain strings/numbers, or objects with an optional `value`,
 * `label`, `group`, `icon`, `description` and `disabled`. `itemText` / `itemIcon` / `groupBy` functions override.
 */
export type ItemLike = string | number | boolean | { value?: unknown; label?: string; text?: string; group?: string; icon?: string; description?: string; disabled?: boolean; [key: string]: unknown };

export interface NormalizedItem<T = unknown> {
  item: T;
  value: unknown;
  key: string;
  text: string;
  group: string;
  icon: string | undefined;
  description: string | undefined;
  disabled: boolean;
}

export interface ItemAccessors<T> {
  itemText?: (item: T) => string;
  itemIcon?: (item: T) => string | null | undefined;
  groupBy?: (item: T) => string;
}

const isObject = (x: unknown): x is Record<string, unknown> => typeof x === 'object' && x !== null;

/** Stable string key for a value (objects are matched by identity first, then by JSON). */
export function valueKey(value: unknown): string {
  if (value === null || value === undefined) return '';
  if (isObject(value)) {
    if ('value' in value && !isObject(value.value)) return String(value.value);
    try {
      return JSON.stringify(value);
    } catch {
      return String(value);
    }
  }
  return String(value);
}

export function normalizeItems<T>(items: readonly T[] | null | undefined, accessors: ItemAccessors<T> = {}): NormalizedItem<T>[] {
  return (items ?? []).map((item) => {
    const obj = isObject(item) ? item : undefined;
    const value = obj && 'value' in obj ? obj.value : item;
    const text = accessors.itemText ? accessors.itemText(item) : String(obj ? (obj.label ?? obj.text ?? obj.value ?? '') : item);
    return {
      item,
      value,
      key: valueKey(value),
      text,
      group: accessors.groupBy ? accessors.groupBy(item) : typeof obj?.group === 'string' ? obj.group : '',
      icon: (accessors.itemIcon ? accessors.itemIcon(item) : typeof obj?.icon === 'string' ? obj.icon : undefined) ?? undefined,
      description: typeof obj?.description === 'string' ? obj.description : undefined,
      disabled: obj?.disabled === true,
    };
  });
}

/** Text with <mark> around the matched ranges (from filterOptions). */
export function highlight(text: string, ranges: readonly TextRange[] | undefined): TemplateResult | string {
  if (!ranges || ranges.length === 0) return text;
  const parts: Array<TemplateResult | string> = [];
  let at = 0;
  for (const r of ranges) {
    if (r.start > at) parts.push(text.slice(at, r.start));
    parts.push(html`<mark>${text.slice(r.start, r.start + r.length)}</mark>`);
    at = r.start + r.length;
  }
  if (at < text.length) parts.push(text.slice(at));
  return html`${parts}`;
}

/** Clones a `<template slot="item-template">` and fills `[data-text]` / `[data-description]` / `[data-value]`. */
export function fromTemplate(template: HTMLTemplateElement | null, item: NormalizedItem): Node[] | null {
  if (!template) return null;
  const fragment = template.content.cloneNode(true) as DocumentFragment;
  for (const el of fragment.querySelectorAll('[data-text]')) el.textContent = item.text;
  for (const el of fragment.querySelectorAll('[data-description]')) el.textContent = item.description ?? '';
  for (const el of fragment.querySelectorAll('[data-value]')) el.textContent = item.key;
  return [...fragment.childNodes];
}
