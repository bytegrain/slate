/**
 * Wave-2 components: overlays, navigation, pickers and display widgets (design/api/components.json).
 * Registered on import, like the rest of @slate/web.
 */
import { SlPopover, SlTooltip } from './components/popover';
import { SlMenu, SlMenuItem } from './components/menu';
import { SlSelect } from './components/select';
import { SlTab, SlTabPanel, SlTabs } from './components/tabs';
import { SlDatePicker } from './components/date-picker';
import { SlTreeView } from './components/tree-view';
import { SlAvatar, SlBreadcrumbs, SlPagination, SlSegmented, SlSkeleton, SlSlider } from './components/widgets';

export const wave2Elements = {
  'sl-popover': SlPopover,
  'sl-tooltip': SlTooltip,
  'sl-menu': SlMenu,
  'sl-menu-item': SlMenuItem,
  'sl-select': SlSelect,
  'sl-tabs': SlTabs,
  'sl-tab': SlTab,
  'sl-tab-panel': SlTabPanel,
  'sl-date-picker': SlDatePicker,
  'sl-tree-view': SlTreeView,
  'sl-segmented': SlSegmented,
  'sl-slider': SlSlider,
  'sl-avatar': SlAvatar,
  'sl-breadcrumbs': SlBreadcrumbs,
  'sl-pagination': SlPagination,
  'sl-skeleton': SlSkeleton,
} as const;

export function defineWave2Elements(): void {
  if (typeof customElements === 'undefined') return;
  for (const [tag, ctor] of Object.entries(wave2Elements)) {
    if (!customElements.get(tag)) customElements.define(tag, ctor);
  }
}

defineWave2Elements();

export { SlPopover, SlTooltip, SlMenu, SlMenuItem, SlSelect, SlTabs, SlTab, SlTabPanel, SlDatePicker, SlTreeView, SlSegmented, SlSlider, SlAvatar, SlBreadcrumbs, SlPagination, SlSkeleton };
export type { SelectItem } from './components/select';
export type { TabsVariant } from './components/tabs';
export type { DateSelection } from './components/date-picker';
export type { TreeSelectionMode } from './components/tree-view';
export type { AvatarStatus, BreadcrumbItem, SkeletonShape } from './components/widgets';

declare global {
  interface HTMLElementTagNameMap {
    'sl-popover': SlPopover;
    'sl-tooltip': SlTooltip;
    'sl-menu': SlMenu;
    'sl-menu-item': SlMenuItem;
    'sl-select': SlSelect;
    'sl-tabs': SlTabs;
    'sl-tab': SlTab;
    'sl-tab-panel': SlTabPanel;
    'sl-date-picker': SlDatePicker;
    'sl-tree-view': SlTreeView;
    'sl-segmented': SlSegmented;
    'sl-slider': SlSlider;
    'sl-avatar': SlAvatar;
    'sl-breadcrumbs': SlBreadcrumbs;
    'sl-pagination': SlPagination;
    'sl-skeleton': SlSkeleton;
  }
}
