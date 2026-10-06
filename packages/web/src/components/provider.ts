import { LitElement, css, html, nothing } from 'lit';
import { hostReset } from '../internal/styles';
import type { SnackbarPosition } from '../core/snackbar-queue';
import { applyTheme, createTheme, type SlateThemeOptions } from '../core/theme/builder';

export type ThemeMode = 'light' | 'dark' | 'system';
export type Density = 'compact' | 'comfortable';

/**
 * <sl-provider theme="system" density="compact"> app… </sl-provider>
 *
 * Sets data-sl-theme / data-sl-density for its subtree (tokens.css does the rest) and hosts the
 * snackbar layer. Dialogs opened through the dialog service are attached here so they inherit the theme.
 * Nested providers scope a theme to a region and don't render a second snackbar host.
 *
 * Custom themes: set the `themeOptions` property (createTheme options). Without an explicit `base`, the
 * custom theme follows `theme` (including the OS preference for "system").
 */
export class SlProvider extends LitElement {
  static override properties = {
    theme: { reflect: true },
    density: { reflect: true },
    snackbarPosition: { attribute: 'snackbar-position' },
    themeOptions: { attribute: false },
    systemDark: { state: true },
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
        --_density-scale: 1;
      }
      :host([density='comfortable']) {
        --_density-scale: 1.2;
      }
    `,
  ];

  declare theme: ThemeMode;
  declare density: Density;
  declare snackbarPosition: SnackbarPosition | undefined;
  /** Custom theme (see createTheme). Null/undefined uses the built-in Alloy themes. */
  declare themeOptions: SlateThemeOptions | null | undefined;
  declare systemDark: boolean;

  private media: MediaQueryList | undefined;

  constructor() {
    super();
    this.theme = 'light';
    this.density = 'compact';
    this.systemDark = false;
  }

  override connectedCallback(): void {
    super.connectedCallback();
    if (typeof matchMedia === 'function') {
      this.media = matchMedia('(prefers-color-scheme: dark)');
      this.systemDark = this.media.matches;
      this.media.addEventListener?.('change', this.onSchemeChange);
    }
  }

  override disconnectedCallback(): void {
    super.disconnectedCallback();
    this.media?.removeEventListener?.('change', this.onSchemeChange);
  }

  private readonly onSchemeChange = (e: MediaQueryListEvent): void => {
    this.systemDark = e.matches;
  };

  /** True when this is the outermost provider (the one that hosts snackbars). */
  get isRoot(): boolean {
    return !this.parentElement?.closest('sl-provider');
  }

  /** The theme actually in effect ('light' | 'dark'), resolving 'system'. */
  get resolvedTheme(): 'light' | 'dark' {
    if (this.theme !== 'system') return this.theme;
    return this.systemDark ? 'dark' : 'light';
  }

  override willUpdate(): void {
    this.setAttribute('data-sl-density', this.density);
    if (this.themeOptions) {
      applyTheme(this, createTheme({ base: this.resolvedTheme, ...this.themeOptions }));
    } else {
      applyTheme(this, null);
      this.setAttribute('data-sl-theme', this.theme === 'system' ? 'auto' : this.theme);
    }
  }

  override render() {
    return html`<slot></slot>${this.isRoot
        ? html`<sl-snackbar-host part="snackbars" position=${this.snackbarPosition ?? nothing}></sl-snackbar-host>`
        : nothing}`;
  }
}
