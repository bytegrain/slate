import { svg, nothing, type TemplateResult } from 'lit';
import { icons, iconViewBox, iconStrokeWidth, type IconName } from '../icons/generated/icons';

export type { IconName };

export function isIconName(name: string | undefined | null): name is IconName {
  return !!name && Object.hasOwn(icons, name);
}

/** Renders a design-system icon as inline SVG with the .sl-icon class. Decorative (aria-hidden). */
export function renderIcon(name: string | undefined | null, extraClass = ''): TemplateResult | typeof nothing {
  if (!isIconName(name)) return nothing;
  return svg`<svg class="sl-icon ${extraClass}" viewBox="0 0 ${iconViewBox} ${iconViewBox}" fill="none" stroke="currentColor" stroke-width=${iconStrokeWidth} stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false"><path d=${icons[name]}></path></svg>`;
}
