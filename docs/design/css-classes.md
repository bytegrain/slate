# CSS class & markup contract

`@slate/web` ships `dist/slate.css`: tokens, fonts, base styles and every component style as plain classes.
It works without JavaScript. **Slate.Blazor renders exactly this markup**, and the web components render the
same markup inside their shadow roots, so one stylesheet defines the look everywhere. Option names and values
come from [`design/api/components.json`](../../design/api/components.json); see [configurability](configurability.md).

Rules:

- Put `class="sl-root"` on `<body>` (or an app root): font, colour, canvas background, box-sizing, tabular numerals.
- Theme/density are attributes on any ancestor: `data-sl-theme="light|dark|auto"`, `data-sl-density="compact|comfortable"`.
  A custom theme is a set of `--sl-*` custom properties on any element (see `createTheme` / `themeToCss`).
- Classes are BEM-style: block `sl-x`, element `sl-x__part`, modifier `sl-x--value`. Runtime state uses `is-*`
  classes or ARIA attributes, as listed below.
- **Modifiers are emitted only for non-default values**, except variant and tone, which are always present
  (`sl-button--outlined sl-tone-neutral`), so markup is self-describing.
- Element-local variables start with `--_` (e.g. `--_gap`, `--_value`, `--_duration`) and may be set inline on
  the element that owns them. `--sl-*` variables are tokens: override them (including `--sl-component-*`) in a
  scope to restyle, never per instance.
- Icons are inline SVG: `<svg class="sl-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="{SlateIcons.X}"/></svg>`.
  Sizes: `sl-icon--sm` (14) · default (16) · `sl-icon--lg` (20).
- `[hidden]` always wins inside `.sl-root`.

## Shared modifiers

| Concept | Classes | Notes |
|---|---|---|
| Tone | `sl-tone-{neutral\|accent\|success\|warning\|danger\|info}` on the component root | Defines `--_tone`, `--_tone-hover`, `--_tone-press`, `--_on-tone`, `--_tone-fg`, `--_tone-subtle`, `--_tone-border`. Severity maps to tone (`error → danger`, `normal → neutral`). |
| Radius | `sl-radius-{none\|small\|medium\|large\|full}` on the shaped element | Sets `--_radius`, used instead of the component's radius token. Omit for `default`. |
| Size | `sl-{block}--small` / `sl-{block}--large` | Omit for `medium`. |
| Density factor | `--_density-scale` (1 compact, 1.2 comfortable) | Set by `[data-sl-density]` in slate.css and by `<sl-provider>`; switches scale with it. |

## Web spellings and events

Canonical options map to web attributes in kebab-case and properties in camelCase (`StartIcon` → `start-icon` /
`startIcon`; `ReadOnly` → `read-only` / `readOnly`). Exceptions, enforced by `test/conformance.test.ts`:

| Canonical | Web | Why |
|---|---|---|
| `TextField.Prefix` / `Suffix` | attribute `prefix` / `suffix`, property `prefixText` / `suffixText` | `Element.prefix` is a reserved DOM property. |
| `Checkbox.Checked = null` | `indeterminate` boolean (plus `checked`) | The DOM's own tri-state spelling. |
| `Title` (Alert, Card, Dialog, AppBar) | `title` attribute is consumed into the property and removed | Prevents the browser tooltip. |

Events: `Click` → native `click`; `ValueChanged` → `sl-value-changed` (detail `{ value }`); `CheckedChanged` →
`sl-checked-changed` (`{ checked }`); `Dismissed` → `sl-dismissed` (cancelable); `DrawerOpenChanged` →
`sl-drawer-open-changed` (`{ open }`). Inputs also fire native `input`/`change`.

---

## Typography

```html
<p class="sl-text sl-text--body sl-text--secondary">…</p>
```
Variants: `--display --h1 --h2 --h3 --title --body --body-strong --label --caption --overline --mono`.
Tones: `--primary --secondary --tertiary --accent --success --warning --danger --info`. `--truncate` for one-line ellipsis.
Links: `<a class="sl-link" href>`.

## Button

```html
<button type="button" class="sl-button sl-button--solid sl-tone-accent [sl-button--small|--large] [sl-radius-*]
                              [sl-button--icon-only] [sl-button--full] [is-loading]" aria-pressed="true|false (toggles only)">
  <span class="sl-button__start"><svg class="sl-icon sl-button__icon">…</svg></span>   <!-- omit when empty -->
  <span class="sl-button__label">Deploy</span>                                          <!-- omit when icon-only -->
  <span class="sl-button__end"><svg class="sl-icon sl-button__icon">…</svg></span>     <!-- omit when empty -->
  <kbd class="sl-kbd" aria-hidden="true">⌘↵</kbd>       <!-- optional shortcut; add aria-keyshortcuts on the button -->
</button>
```

| Option | Markup |
|---|---|
| Variant | `sl-button--outlined` (default) · `--solid` · `--soft` · `--ghost` · `--link` |
| Tone | `sl-tone-neutral` (default) · `accent` · `success` · `warning` · `danger` · `info`. Primary = `--solid` + `sl-tone-accent`; destructive confirm = `--solid` + `sl-tone-danger`. |
| Size | `sl-button--small` · none · `sl-button--large` |
| Radius | `sl-radius-*` |
| Icon-only | `sl-button--icon-only`, start icon only, **`aria-label` required** |
| Full width | `sl-button--full` |
| Disabled | native `disabled` (or `aria-disabled="true"` on links) |
| Loading | `is-loading`, `aria-busy="true"`, `aria-disabled="true"`; the start slot shows `<span class="sl-spinner sl-button__icon" aria-hidden="true"></span>` |
| Pressed (toggle) | `aria-pressed="true|false"` (or `is-selected`); `is-pressed` for static demos |

Links use the same classes on `<a href>`. Group: `<div class="sl-button-group" role="group" aria-label="…">` around buttons.
Tokens: `component.button.{radius,gap,paddingSm,paddingMd,paddingLg,fontWeight,iconSize,shadow,solidShadow}`.

## Text field

```html
<div class="sl-field sl-field--outlined [sl-field--small|--large] [sl-radius-*] [sl-field--invalid] [sl-field--disabled] [sl-field--multiline]">
  <label class="sl-field__label" for="f1">Email<span class="sl-field__required" aria-hidden="true">*</span></label>
  <div class="sl-field__control">
    <span class="sl-field__affix sl-field__affix--prefix">https://</span>               <!-- Prefix -->
    <span class="sl-field__adornment sl-field__adornment--start">…</span>               <!-- StartContent (replaces StartIcon) -->
    <span class="sl-field__icon sl-field__icon--start"><svg class="sl-icon">…</svg></span>  <!-- StartIcon -->
    <input id="f1" class="sl-field__input" aria-invalid="false" aria-describedby="f1-desc" />
    <!-- multiline: <textarea class="sl-field__input sl-field__input--multiline" rows="3"> -->
    <button class="sl-field__clear" type="button" aria-label="Clear"><svg class="sl-icon">x</svg></button>  <!-- Clearable, when not empty -->
    <span class="sl-field__icon sl-field__icon--end"><svg class="sl-icon">…</svg></span>    <!-- EndIcon -->
    <span class="sl-field__adornment sl-field__adornment--end">…</span>                 <!-- EndContent (replaces EndIcon) -->
    <span class="sl-field__affix sl-field__affix--suffix">.dev</span>                   <!-- Suffix -->
  </div>
  <div class="sl-field__footer">                                                        <!-- omit when empty -->
    <p class="sl-field__helper" id="f1-desc">Helper text</p>
    <!-- when invalid, instead of the helper: -->
    <p class="sl-field__error" id="f1-desc"><svg class="sl-icon">alert-circle</svg>What went wrong and how to fix it</p>
    <span class="sl-field__counter [is-over]" aria-live="polite">12 / 40</span>        <!-- Counter -->
  </div>
</div>
```
Variants: `sl-field--outlined` (default) · `--filled` · `--underlined`. Invalid: add `sl-field--invalid` and `aria-invalid="true"`.
Required: native `required` + the asterisk span.
Tokens: `component.field.{radius,paddingX,background,filledBackground,border,borderHover,borderFocus,shadow}`.

## Checkbox

```html
<label class="sl-checkbox sl-tone-accent [sl-checkbox--small|--large] sl-checkbox--label-end [is-disabled]">
  <input type="checkbox" class="sl-checkbox__input [is-indeterminate]" aria-describedby="c1-desc" />
  <span class="sl-checkbox__text">
    <span class="sl-checkbox__label">Notify on failure</span>
    <span class="sl-checkbox__description" id="c1-desc">Optional description</span>
  </span>
</label>
```
Label placement: `--label-end` (default, label after) or `--label-start`. Tone colours the checked fill (accent uses
`component.selection.checked`). Indeterminate: set the input's `indeterminate` property; without script add
`is-indeterminate` and `aria-checked="mixed"`.

## Switch

```html
<label class="sl-switch sl-tone-accent [sl-switch--small|--large] sl-switch--label-end [sl-switch--spread] [is-disabled]">
  <span class="sl-switch__control">
    <input type="checkbox" role="switch" class="sl-switch__input" aria-checked="true" checked />
    <span class="sl-switch__thumb" aria-hidden="true"></span>
  </span>
  <span class="sl-switch__text">
    <span class="sl-switch__label">Preview deployments</span>
    <span class="sl-switch__description">Optional</span>
  </span>
</label>
```
`--label-end` (default) or `--label-start`. `--spread` puts label and switch at opposite ends (use with `--label-start`
for settings lists). Size = `component.selection.{switchWidth,switchHeight,thumbSize}` × size × `--_density-scale`.
Prevent Enter from submitting forms.

## Radio group

```html
<fieldset class="sl-radio-group [sl-radio-group--row]">
  <legend class="sl-radio-group__label">Plan</legend>
  <div class="sl-radio-group__options">
    <label class="sl-radio sl-tone-accent [sl-radio--small|--large] [is-disabled]">
      <input type="radio" name="plan" value="team" class="sl-radio__input" checked />
      <span class="sl-radio__text">
        <span class="sl-radio__label">Team</span>
        <span class="sl-radio__description">Unlimited projects, SSO</span>
      </span>
    </label>
  </div>
</fieldset>
```
`Direction="row"` → `sl-radio-group--row`. Tone and size are set on the group and applied to every radio.
Native radios give arrow-key behaviour for free. (The web component renders `<span class="sl-radio__input is-checked">`
with `role="radio"` on its host instead, because native radios can't group across shadow roots.)

## Alert

```html
<div class="sl-alert sl-alert--soft sl-alert--warning sl-tone-warning [sl-alert--dense]" role="alert|status (only if it appeared dynamically)">
  <span class="sl-alert__icon"><svg class="sl-icon">alert-triangle</svg></span>   <!-- Icon overrides; "none" omits -->
  <div class="sl-alert__content">
    <strong class="sl-alert__title">Usage at 80%</strong>
    <div class="sl-alert__message">Build minutes reset on the 1st.</div>
  </div>
  <div class="sl-alert__actions">
    <button class="sl-button sl-button--outlined sl-tone-neutral sl-button--small">View</button>
    <button class="sl-button sl-button--ghost sl-tone-neutral sl-button--icon-only sl-button--small sl-alert__close" aria-label="Dismiss">x</button>
  </div>
</div>
```
Variant: `--soft` (default) · `--outlined` · `--solid`. Severity class `--{normal|info|success|warning|error}` plus the
mapped tone class. Default icons: info, check-circle, alert-triangle, alert-circle. `role="alert"` for errors.
Tokens: `component.alert.{radius,padding}`.

## Badge

```html
<span class="sl-badge sl-badge--soft sl-tone-success [sl-badge--small]">
  <span class="sl-badge__dot" aria-hidden="true"></span>        <!-- Dot -->
  <svg class="sl-icon sl-badge__icon">…</svg>                   <!-- Icon -->
  Ready
</span>
```
Variant: `--soft` (default) · `--solid` · `--outlined`. Tokens: `component.badge.{radius,height}`.

## Progress & spinner

```html
<div class="sl-progress sl-tone-accent [sl-progress--small|--large]" role="progressbar" aria-label="Uploading"
     aria-valuemin="0" aria-valuemax="100" aria-valuenow="64" style="--_value: 64%">
  <div class="sl-progress__bar"></div>
</div>
<!-- indeterminate: add sl-progress--indeterminate, omit aria-valuenow, set aria-busy="true" -->
<!-- ShowValue: wrap as <div class="sl-progress-row">…progress…<span class="sl-progress__value" aria-hidden="true">64%</span></div> -->

<span class="sl-spinner [sl-spinner--small|--large] [sl-tone-*]" role="status" aria-label="Loading"></span>
```
Spinner: neutral (no tone class) follows `currentColor`. Tokens: `component.progress.{height,track,fill}`.

## Kbd

```html
<kbd class="sl-kbd">⌘K</kbd>
```

## Layout

```html
<div class="sl-app-shell [sl-app-shell--viewport]">
  <header class="sl-app-bar">
    <button class="sl-button sl-button--ghost sl-tone-neutral sl-button--icon-only" aria-label="Toggle navigation" aria-expanded="true">menu</button>
    <div class="sl-app-bar__leading">…</div>
    <span class="sl-app-bar__title">Title</span>
    <div class="sl-app-bar__center">…</div>
    <div class="sl-app-bar__actions">…</div>
  </header>
  <nav class="sl-drawer sl-drawer--responsive [sl-drawer--breakpoint-sm|lg|xl] is-open [is-overlay]" aria-label="Navigation">
    <div class="sl-nav">
      <div class="sl-nav__heading">Section</div>
      <a class="sl-nav__item [is-active] [is-disabled]" href="…" aria-current="page"><svg class="sl-icon">…</svg><span class="sl-nav__label">Overview</span><span class="sl-nav__meta">12</span></a>
    </div>
  </nav>
  <div class="sl-drawer-scrim"></div>   <!-- only while an overlay drawer is open; click closes -->
  <main class="sl-main [sl-main--flush]">…</main>
</div>
```
Drawer variants: `--persistent` (closed = collapsed to 0), `--temporary` (overlay; `is-open` slides it in), `--mini`
(icon rail; `is-open` expands; labels hidden while collapsed), `--responsive` (persistent from the breakpoint up,
temporary below; md by default, `--breakpoint-{sm|lg|xl}` to change). `is-overlay` forces overlay behaviour from
script for any breakpoint. `is-open` = open in every variant. Overlay drawers close on Escape/scrim and return focus.
NavItem: `Label` → `.sl-nav__label`, `Icon` → leading svg, `Trailing` → `.sl-nav__meta`, `Active` → `is-active` +
`aria-current="page"`, `Disabled` → `is-disabled` (+ `aria-disabled`/`disabled`).
Tokens: `component.appBar.{height,background,border}`, `component.drawer.{width,miniWidth,background,itemRadius,activeItem}`.

```html
<div class="sl-container [sl-container--sm|--md|--lg|--xl|--fluid] [sl-container--no-gutters]">…</div>

<div class="sl-grid sl-grid--spacing-4">            <!-- or style="--_gap: var(--sl-space-4)" -->
  <div class="sl-grid__item sl-col-xs-12 sl-col-md-6 sl-col-lg-4">…</div>
</div>
```
Container `MaxWidth` → size modifier (lg default); `Gutters=false` → `--no-gutters`. Grid spans
`sl-col-{xs|sm|md|lg|xl}-{1..12}`, mobile-first. Spacing steps: `0, 0-5, 1, 1-5, 2, 3, 4, 5, 6, 8, 10, 12, 16`.

```html
<div class="sl-stack sl-stack--row sl-stack--spacing-2 sl-stack--align-center sl-stack--justify-between sl-stack--wrap">…</div>
<span class="sl-spacer"></span>
<hr class="sl-divider" />  <div class="sl-divider sl-divider--vertical" role="separator" aria-orientation="vertical"></div>
```
Stack align (default stretch, no class): `--align-{start|center|end|baseline}`; justify (default start):
`--justify-{center|end|between}`.

```html
<article class="sl-card sl-card--elevated [sl-radius-*] [sl-card--interactive (+ tabindex="0")]">
  <header class="sl-card__header">
    <div class="sl-card__titles"><h3 class="sl-card__title">Title</h3><p class="sl-card__subtitle">Subtitle</p></div>  <!-- or custom Header content -->
    <div class="sl-card__actions">…HeaderActions…</div>
  </header>
  <div class="sl-card__body [sl-card__body--flush]">…</div>
  <footer class="sl-card__footer">…buttons, primary last…</footer>
</article>

<div class="sl-toolbar [sl-toolbar--flat]" role="toolbar" aria-label="Formatting">…buttons, sl-divider--vertical…</div>
```
Card variant: `--elevated` (default) · `--outlined` · `--flat`. Tokens: `component.card.{radius,padding,background,border,shadow,hoverShadow}`.

## Snackbars

```html
<section class="sl-snackbar-host sl-snackbar-host--bottom-right" aria-label="Notifications">
  <div class="sl-snackbar sl-snackbar--success [is-paused] [is-leaving]" role="status|alert" aria-live="polite|assertive" style="--_duration: 5000ms">
    <span class="sl-snackbar__icon" aria-hidden="true"><svg class="sl-icon">check</svg></span>   <!-- omit for normal -->
    <div class="sl-snackbar__content">
      <strong class="sl-snackbar__title">Deployed</strong>                                  <!-- optional -->
      <p class="sl-snackbar__message">slate-web is live in 3 regions.</p>
    </div>
    <div class="sl-snackbar__actions">
      <button class="sl-snackbar__action" type="button">Undo</button>
      <button class="sl-snackbar__close" type="button" aria-label="Dismiss"><svg class="sl-icon">x</svg></button>
    </div>
    <span class="sl-snackbar__timer" aria-hidden="true"></span>                               <!-- omit when sticky -->
  </div>
</section>
```
Positions: `--top-left --top-center --top-right --bottom-left --bottom-center --bottom-right`. Severities: `--info --success
--warning --error` (none = normal). Tile glyphs: info, check, alert-triangle, x. `role="alert"` + assertive for warning/error.
Toggle `is-paused` while hovered/focused (the timer line freezes); `is-leaving` plays the exit fade (`motion.duration.fast`).
Tokens: `component.snackbar.{radius,background,foreground,muted,shadow,timer}`.

## Dialog

```html
<dialog class="sl-dialog sl-dialog--sm [sl-dialog--top] [sl-dialog--full-width] [sl-dialog--full-screen] [is-closing]"
        aria-labelledby="d1-title" aria-describedby="d1-desc">       <!-- open with showModal(); or a <div role="dialog" aria-modal="true"> -->
  <div class="sl-dialog__scrim"></div>
  <div class="sl-dialog__panel" tabindex="-1">
    <header class="sl-dialog__header">
      <div class="sl-dialog__icon sl-tone-danger" aria-hidden="true"><svg class="sl-icon sl-icon--lg">alert-triangle</svg></div>
      <div class="sl-dialog__titles">
        <h2 class="sl-dialog__title" id="d1-title">Delete Slate.Wpf?</h2>
        <p class="sl-dialog__description" id="d1-desc">This can't be undone.</p>
      </div>
      <button class="sl-button sl-button--ghost sl-tone-neutral sl-button--icon-only sl-button--small sl-dialog__close" aria-label="Close">x</button>
    </header>
    <div class="sl-dialog__body">…</div>
    <footer class="sl-dialog__footer">…secondary…, primary last</footer>
  </div>
</dialog>
```
`MaxWidth` → `--xs --sm --md --lg --xl` (360/480/640/880/1120). Icon tile tone: `sl-tone-*` (neutral default). Add
`is-closing` for `motion.duration.base` before removing/closing to play the exit animation.
Tokens: `component.dialog.{radius,padding,background,footer,shadow,scrim}`.
