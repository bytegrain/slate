import type { ReactiveController, ReactiveControllerHost } from 'lit';
import type { DialogOptions } from './dialog-stack';
import type { SnackbarConfiguration } from './snackbar-queue';

/** Shared configuration vocabulary — mirrors Slate.Core (docs/design/configurability.md). */
export type Tone = 'neutral' | 'accent' | 'success' | 'warning' | 'danger' | 'info';
export type ButtonVariant = 'outlined' | 'solid' | 'soft' | 'ghost' | 'link';
export type FieldVariant = 'outlined' | 'filled' | 'underlined';
export type CardVariant = 'elevated' | 'outlined' | 'flat';
export type BadgeVariant = 'soft' | 'solid' | 'outlined';
export type AlertVariant = 'soft' | 'outlined' | 'solid';
export type ControlSize = 'small' | 'medium' | 'large';
export type Radius = 'default' | 'none' | 'small' | 'medium' | 'large' | 'full';
export type Placement = 'start' | 'end';
export type Direction = 'row' | 'column';

export const tones: readonly Tone[] = ['neutral', 'accent', 'success', 'warning', 'danger', 'info'];

/** App-wide component defaults (mirrors Slate.SlateDefaults). A value set on a component always wins. */
export interface SlateDefaults {
  button: { variant: ButtonVariant; tone: Tone; size: ControlSize; radius: Radius };
  field: { variant: FieldVariant; size: ControlSize; radius: Radius };
  card: { variant: CardVariant; radius: Radius };
  selection: { labelPlacement: Placement };
  dialog: Partial<DialogOptions>;
  snackbar: Partial<SnackbarConfiguration>;
}

export type DefaultsPatch = { [K in keyof SlateDefaults]?: Partial<SlateDefaults[K]> };

const initial = (): SlateDefaults => ({
  button: { variant: 'outlined', tone: 'neutral', size: 'medium', radius: 'default' },
  field: { variant: 'outlined', size: 'medium', radius: 'default' },
  card: { variant: 'elevated', radius: 'default' },
  selection: { labelPlacement: 'end' },
  dialog: {},
  snackbar: {},
});

let current: SlateDefaults = initial();
const listeners = new Set<(defaults: SlateDefaults) => void>();

/** The defaults in effect. */
export function getDefaults(): Readonly<SlateDefaults> {
  return current;
}

/**
 * Merges new defaults (per group) and re-renders connected components that rely on them.
 *   configureDefaults({ button: { size: 'small' }, field: { variant: 'filled' } });
 * Snackbar defaults are applied by @bytegrain/slate-web's snackbar service (see index.ts).
 */
export function configureDefaults(patch: DefaultsPatch): Readonly<SlateDefaults> {
  const next = { ...current };
  for (const key of Object.keys(patch) as (keyof SlateDefaults)[]) {
    (next as Record<string, unknown>)[key] = { ...current[key], ...patch[key] };
  }
  current = next;
  for (const l of listeners) l(current);
  return current;
}

/** Restores the Alloy defaults. */
export function resetDefaults(): void {
  current = initial();
  for (const l of listeners) l(current);
}

export function onDefaultsChanged(listener: (defaults: SlateDefaults) => void): () => void {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

/** Re-renders its host whenever the defaults change. */
export class DefaultsController implements ReactiveController {
  private off: (() => void) | undefined;

  constructor(private readonly host: ReactiveControllerHost) {
    host.addController(this);
  }

  hostConnected(): void {
    this.off = onDefaultsChanged(() => this.host.requestUpdate());
  }

  hostDisconnected(): void {
    this.off?.();
  }
}
