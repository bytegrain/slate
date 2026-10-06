import { LitElement, css, html, nothing } from 'lit';
import { hostReset } from '../internal/styles';
import type { SnackbarPosition } from '../core/snackbar-queue';

export type ThemeMode = 'light' | 'dark' | 'system';
export type Density = 'compact' | 'comfortable';

/**
 * <sl-provider theme="system" density="compact"> app… </sl-provider>
 *
 * Sets data-sl-theme / data-sl-density for its subtree (tokens.css does the rest) and hosts the
 * snackbar layer. Dialogs opened through the dialog service are attached here so they inherit the theme.
 * Nested providers scope a theme to a region and don't render a second snackbar host.
 */
export class SlProvider extends LitElement {
  static override properties = {
    theme: { reflect: true },
    density: { reflect: true },
    snackbarPosition: { attribute: 'snackbar-position' },
  };
  static override styles = [
    hostReset,
    css`
      :host {
        display: block;
        color: var(--sl-color-text-primary);
        background: var(--sl-color-background-canvas);
        font-family: var(--sl-font-family-ui);
        font-size: var(--sl-font-size-md);
        line-height: var(--sl-font-line-height-normal);
        font-variant-numeric: tabular-nums;
        -webkit-font-smoothing: antialiased;
        -moz-osx-font-smoothing: grayscale;
      }
    `,
  ];

  declare theme: ThemeMode;
  declare density: Density;
  declare snackbarPosition: SnackbarPosition | undefined;

  constructor() {
    super();
    this.theme = 'light';
    this.density = 'compact';
  }

  /** True when this is the outermost provider (the one that hosts snackbars). */
  get isRoot(): boolean {
    return !this.parentElement?.closest('sl-provider');
  }

  /** The theme actually in effect ('light' | 'dark'), resolving 'system'. */
  get resolvedTheme(): 'light' | 'dark' {
    if (this.theme !== 'system') return this.theme;
    return typeof matchMedia === 'function' && matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
  }

  override willUpdate(): void {
    this.setAttribute('data-sl-theme', this.theme === 'system' ? 'auto' : this.theme);
    this.setAttribute('data-sl-density', this.density);
  }

  override render() {
    return html`<slot></slot>${this.isRoot
        ? html`<sl-snackbar-host part="snackbars" position=${this.snackbarPosition ?? nothing}></sl-snackbar-host>`
        : nothing}`;
  }
}
