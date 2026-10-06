/**
 * @slate/web — the Alloy design system as framework-agnostic web components.
 *
 *   import '@slate/web/slate.css';      // tokens, fonts, base + class-based component styles
 *   import { snackbar, dialog } from '@slate/web';   // registers every <sl-*> element
 */
import { SlProvider } from './components/provider';
import { SlAppBar, SlAppShell, SlCard, SlContainer, SlDivider, SlDrawer, SlGrid, SlGridItem, SlNavItem, SlSpacer, SlStack, SlToolbar } from './components/layout';
import { SlAlert, SlBadge, SlIcon, SlKbd, SlProgress, SlSpinner, SlText } from './components/display';
import { SlButton } from './components/button';
import { SlTextField } from './components/text-field';
import { SlCheckbox, SlRadio, SlRadioGroup, SlSwitch } from './components/selection';
import { SlSnackbarHost } from './components/snackbar-host';
import { SlDialog } from './components/dialog';
import { configureDefaults as configureCoreDefaults, type DefaultsPatch, type SlateDefaults } from './core/defaults';
import { snackbar } from './services/snackbar';

export const elements = {
  'sl-provider': SlProvider,
  'sl-app-shell': SlAppShell,
  'sl-app-bar': SlAppBar,
  'sl-drawer': SlDrawer,
  'sl-nav-item': SlNavItem,
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
  SlNavItem,
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
export type { TextFieldType } from './components/text-field';
export type { BadgeTone, TextVariant, TextTone } from './components/display';
export { severityTone } from './components/display';
export type { DrawerVariant, ContainerSize, ResponsiveBreakpoint } from './components/layout';
export type { DialogTone } from './components/dialog';

// Configuration vocabulary, defaults and runtime themes (docs/design/configurability.md).
export type {
  Tone,
  ButtonVariant,
  FieldVariant,
  CardVariant,
  BadgeVariant,
  AlertVariant,
  ControlSize,
  Radius,
  Placement,
  Direction,
  SlateDefaults,
  DefaultsPatch,
} from './core/defaults';
export { tones, getDefaults, resetDefaults, onDefaultsChanged } from './core/defaults';
export { createTheme, applyTheme, themeToCss, cssVariable, type SlateTheme, type SlateThemeOptions, type ThemeBase } from './core/theme/builder';
export { SlateColor } from './core/theme/color';

export { snackbar, SnackbarService } from './services/snackbar';

/**
 * Sets app-wide component defaults (mirrors Slate.SlateDefaults). Snackbar defaults reconfigure the
 * shared snackbar service; everything else is read by components at render time.
 */
export function configureDefaults(patch: DefaultsPatch): Readonly<SlateDefaults> {
  const result = configureCoreDefaults(patch);
  if (patch.snackbar) snackbar.configure(patch.snackbar);
  return result;
}
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
    'sl-nav-item': SlNavItem;
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
