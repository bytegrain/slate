/**
 * @slate/web — the Alloy design system as framework-agnostic web components.
 *
 *   import '@slate/web/slate.css';      // tokens, fonts, base + class-based component styles
 *   import { snackbar, dialog } from '@slate/web';   // registers every <sl-*> element
 */
import { SlProvider } from './components/provider';
import { SlAppBar, SlAppShell, SlCard, SlContainer, SlDivider, SlDrawer, SlGrid, SlGridItem, SlSpacer, SlStack, SlToolbar } from './components/layout';
import { SlAlert, SlBadge, SlIcon, SlKbd, SlProgress, SlSpinner, SlText } from './components/display';
import { SlButton } from './components/button';
import { SlTextField } from './components/text-field';
import { SlCheckbox, SlRadio, SlRadioGroup, SlSwitch } from './components/selection';
import { SlSnackbarHost } from './components/snackbar-host';
import { SlDialog } from './components/dialog';

export const elements = {
  'sl-provider': SlProvider,
  'sl-app-shell': SlAppShell,
  'sl-app-bar': SlAppBar,
  'sl-drawer': SlDrawer,
  'sl-container': SlContainer,
  'sl-grid': SlGrid,
  'sl-grid-item': SlGridItem,
  'sl-stack': SlStack,
  'sl-spacer': SlSpacer,
  'sl-divider': SlDivider,
  'sl-card': SlCard,
  'sl-toolbar': SlToolbar,
  'sl-icon': SlIcon,
  'sl-kbd': SlKbd,
  'sl-text': SlText,
  'sl-badge': SlBadge,
  'sl-spinner': SlSpinner,
  'sl-progress': SlProgress,
  'sl-alert': SlAlert,
  'sl-button': SlButton,
  'sl-text-field': SlTextField,
  'sl-checkbox': SlCheckbox,
  'sl-switch': SlSwitch,
  'sl-radio': SlRadio,
  'sl-radio-group': SlRadioGroup,
  'sl-snackbar-host': SlSnackbarHost,
  'sl-dialog': SlDialog,
} as const;

/** Registers every element (idempotent). Called automatically on import. */
export function defineElements(): void {
  if (typeof customElements === 'undefined') return;
  for (const [tag, ctor] of Object.entries(elements)) {
    if (!customElements.get(tag)) customElements.define(tag, ctor);
  }
}

defineElements();

export {
  SlProvider,
  SlAppShell,
  SlAppBar,
  SlDrawer,
  SlContainer,
  SlGrid,
  SlGridItem,
  SlStack,
  SlSpacer,
  SlDivider,
  SlCard,
  SlToolbar,
  SlIcon,
  SlKbd,
  SlText,
  SlBadge,
  SlSpinner,
  SlProgress,
  SlAlert,
  SlButton,
  SlTextField,
  SlCheckbox,
  SlSwitch,
  SlRadio,
  SlRadioGroup,
  SlSnackbarHost,
  SlDialog,
};

export type { ThemeMode, Density } from './components/provider';
export type { ButtonVariant, ControlSize } from './components/button';
export type { TextFieldType } from './components/text-field';
export type { BadgeTone, TextVariant, TextTone } from './components/display';
export type { DrawerVariant, ContainerSize } from './components/layout';
export type { DialogTone } from './components/dialog';

export { snackbar, SnackbarService } from './services/snackbar';
export { dialog, type DialogAction, type DialogShowOptions, type MessageBoxOptions } from './services/dialog';

export {
  SnackbarQueue,
  defaultSnackbarConfiguration,
  type Severity,
  type Snackbar,
  type SnackbarAction,
  type SnackbarOptions,
  type SnackbarConfiguration,
  type SnackbarPosition,
  type SnackbarState,
  type SnackbarCloseReason,
  type SnackbarClosedEvent,
} from './core/snackbar-queue';
export {
  DialogStack,
  DialogReference,
  DialogResult,
  dialogStack,
  defaultDialogOptions,
  dialogWidthPixels,
  type DialogOptions,
  type DialogWidth,
  type DialogPlacement,
} from './core/dialog-stack';
export { breakpoints, breakpointFromWidth, breakpointMinWidth, resolveSpan, spaceVar, gridColumns, type Breakpoint, type GridSpan } from './core/layout';
export { systemClock, FakeClock, type Clock } from './core/clock';
export { tokenValue, tokenNumber } from './core/tokens';
export { icons, type IconName } from './icons/generated/icons';

declare global {
  interface HTMLElementTagNameMap {
    'sl-provider': SlProvider;
    'sl-app-shell': SlAppShell;
    'sl-app-bar': SlAppBar;
    'sl-drawer': SlDrawer;
    'sl-container': SlContainer;
    'sl-grid': SlGrid;
    'sl-grid-item': SlGridItem;
    'sl-stack': SlStack;
    'sl-spacer': SlSpacer;
    'sl-divider': SlDivider;
    'sl-card': SlCard;
    'sl-toolbar': SlToolbar;
    'sl-icon': SlIcon;
    'sl-kbd': SlKbd;
    'sl-text': SlText;
    'sl-badge': SlBadge;
    'sl-spinner': SlSpinner;
    'sl-progress': SlProgress;
    'sl-alert': SlAlert;
    'sl-button': SlButton;
    'sl-text-field': SlTextField;
    'sl-checkbox': SlCheckbox;
    'sl-switch': SlSwitch;
    'sl-radio': SlRadio;
    'sl-radio-group': SlRadioGroup;
    'sl-snackbar-host': SlSnackbarHost;
    'sl-dialog': SlDialog;
  }
}
