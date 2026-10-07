import { css, unsafeCSS, type CSSResult } from 'lit';
import baseCss from '../styles/base.css?inline';
import iconCss from '../styles/icon.css?inline';
import typographyCss from '../styles/typography.css?inline';
import layoutCss from '../styles/layout.css?inline';
import gridCss from '../styles/grid.css?inline';
import toneCss from '../styles/tone.css?inline';
import buttonCss from '../styles/button.css?inline';
import fieldCss from '../styles/field.css?inline';
import selectionCss from '../styles/selection.css?inline';
import feedbackCss from '../styles/feedback.css?inline';
import snackbarCss from '../styles/snackbar.css?inline';
import dialogCss from '../styles/dialog.css?inline';
import overlayCss from '../styles/overlay.css?inline';
import selectCss from '../styles/select.css?inline';
import navigationCss from '../styles/navigation.css?inline';
import calendarCss from '../styles/calendar.css?inline';
import treeCss from '../styles/tree.css?inline';
import widgetsCss from '../styles/widgets.css?inline';
import datagridCss from '../styles/datagrid.css?inline';

/*
 * Components adopt the exact stylesheets that make up slate.css, so a web component and the same
 * class-based markup rendered by Blazor (or by hand) look identical. Custom properties (tokens) inherit
 * into shadow roots, so data-sl-theme / data-sl-density on any ancestor restyles components.
 */
export const styles = {
  base: unsafeCSS(baseCss),
  typography: unsafeCSS(typographyCss),
  layout: unsafeCSS(layoutCss),
  grid: unsafeCSS(gridCss),
  tone: unsafeCSS(toneCss),
  button: unsafeCSS(buttonCss),
  field: unsafeCSS(fieldCss),
  selection: unsafeCSS(selectionCss),
  feedback: unsafeCSS(feedbackCss),
  snackbar: unsafeCSS(snackbarCss),
  dialog: unsafeCSS(dialogCss),
  overlay: unsafeCSS(overlayCss),
  select: unsafeCSS(selectCss),
  navigation: unsafeCSS(navigationCss),
  calendar: unsafeCSS(calendarCss),
  tree: unsafeCSS(treeCss),
  widgets: unsafeCSS(widgetsCss),
  datagrid: unsafeCSS(datagridCss),
} satisfies Record<string, CSSResult>;

/** Reset applied inside every shadow root. */
export const hostReset = css`
  ${unsafeCSS(iconCss)}
  :host {
    box-sizing: border-box;
  }
  :host([hidden]) {
    display: none !important;
  }
  *,
  *::before,
  *::after {
    box-sizing: inherit;
  }
  /* Component classes set display; [hidden] must still win. */
  [hidden] {
    display: none !important;
  }
`;
