# CSS class & markup contract

`@slate/web` ships `dist/slate.css`: tokens, fonts, base styles and every component style as plain classes.
It works without JavaScript. **Slate.Blazor renders exactly this markup**, and the web components render the
same markup inside their shadow roots, so one stylesheet defines the look everywhere.

Rules:

- Put `class="sl-root"` on `<body>` (or an app root): font, colour, canvas background, box-sizing, tabular numerals.
- Theme/density are attributes on any ancestor: `data-sl-theme="light|dark|auto"`, `data-sl-density="compact|comfortable"`.
- Classes are BEM-style: block `sl-x`, element `sl-x__part`, modifier `sl-x--variant`. Runtime state uses `is-*`
  classes or ARIA attributes, as listed below.
- Element-local variables start with `--_` (e.g. `--_gap`, `--_value`, `--_duration`) and may be set inline on
  the element that owns them. `--sl-*` variables are tokens: never set them per component.
- Icons are inline SVG: `<svg class="sl-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="{SlateIcons.X}"/></svg>`.
  Sizes: `sl-icon--sm` (14) · default (16) · `sl-icon--lg` (20).
- `[hidden]` always wins inside `.sl-root`.

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
<button type="button" class="sl-button sl-button--primary sl-button--sm">
  <svg class="sl-icon sl-button__icon">…</svg>          <!-- optional start icon -->
  <span class="sl-button__label">Deploy</span>
  <svg class="sl-icon sl-button__icon">…</svg>          <!-- optional end icon -->
  <kbd class="sl-kbd" aria-hidden="true">⌘↵</kbd>        <!-- optional shortcut; add aria-keyshortcuts on the button -->
</button>
```

| Option | Markup |
|---|---|
| Variant | none (secondary) · `sl-button--primary` · `--ghost` · `--danger` · `--danger-solid` · `--link` |
| Size | `sl-button--sm` · none (medium) · `sl-button--lg` |
| Icon-only | `sl-button--icon`, no label span, **`aria-label` required** |
| Full width | `sl-button--full` |
| Disabled | native `disabled` (or `aria-disabled="true"` on links) |
| Loading | `is-loading`, `aria-busy="true"`, `aria-disabled="true"`; replace the start icon with `<span class="sl-spinner sl-button__icon" aria-hidden="true"></span>` |
| Toggle / selected | `aria-pressed="true"` (or `is-selected`) |
| Pressed (static demo) | `is-pressed` |

Links use the same classes on `<a href>`. Group: `<div class="sl-button-group" role="group" aria-label="…">` around buttons.

## Text field

```html
<div class="sl-field [sl-field--sm|--lg] [sl-field--invalid] [sl-field--disabled] [sl-field--multiline]">
  <label class="sl-field__label" for="f1">Email<span class="sl-field__required" aria-hidden="true">*</span></label>
  <div class="sl-field__control">
    <span class="sl-field__affix sl-field__affix--prefix">https://</span>   <!-- optional -->
    <span class="sl-field__icon"><svg class="sl-icon">…</svg></span>        <!-- optional start icon -->
    <input id="f1" class="sl-field__input" aria-invalid="false" aria-describedby="f1-desc" />
    <!-- multiline: <textarea class="sl-field__input sl-field__input--multiline" rows="3"> -->
    <span class="sl-field__affix sl-field__affix--suffix">.dev</span>       <!-- optional -->
  </div>
  <p class="sl-field__helper" id="f1-desc">Helper text</p>
  <!-- when invalid, instead of the helper: -->
  <p class="sl-field__error" id="f1-desc"><svg class="sl-icon">alert-circle</svg>What went wrong and how to fix it</p>
</div>
```
Invalid: add `sl-field--invalid` and `aria-invalid="true"`. Required: native `required` + the asterisk span.

## Checkbox

```html
<label class="sl-checkbox [is-disabled]">
  <input type="checkbox" class="sl-checkbox__input [is-indeterminate]" aria-describedby="c1-desc" />
  <span class="sl-checkbox__text">
    <span class="sl-checkbox__label">Notify on failure</span>
    <span class="sl-checkbox__description" id="c1-desc">Optional description</span>
  </span>
</label>
```
Indeterminate: set the input's `indeterminate` property (needs script). Without script, add `is-indeterminate` and `aria-checked="mixed"`.

## Switch

```html
<label class="sl-switch sl-switch--label-start [sl-switch--spread] [is-disabled]">
  <input type="checkbox" role="switch" class="sl-switch__input" aria-checked="true" checked />
  <span class="sl-switch__text">
    <span class="sl-switch__label">Preview deployments</span>
    <span class="sl-switch__description">Optional</span>
  </span>
</label>
```
`sl-switch--label-start` (label before, default) or `--label-end`. `--spread` puts label and switch at opposite ends of the row.
Size follows density automatically (20×36 compact, 24×44 comfortable). Prevent Enter from submitting forms.

## Radio group

```html
<fieldset class="sl-radio-group [sl-radio-group--row]">
  <legend class="sl-radio-group__label">Plan</legend>
  <div class="sl-radio-group__options">
    <label class="sl-radio [is-disabled]">
      <input type="radio" name="plan" value="team" class="sl-radio__input" checked />
      <span class="sl-radio__text">
        <span class="sl-radio__label">Team</span>
        <span class="sl-radio__description">Unlimited projects, SSO</span>
      </span>
    </label>
  </div>
</fieldset>
```
Native radios give arrow-key behaviour for free. (The web component renders `<span class="sl-radio__input is-checked">`
with `role="radio"` on its host instead, because native radios can't group across shadow roots.)

## Alert

```html
<div class="sl-alert sl-alert--warning" role="alert|status (only if it appeared dynamically)">
  <svg class="sl-icon sl-alert__icon">alert-triangle</svg>
  <div class="sl-alert__content">
    <strong class="sl-alert__title">Usage at 80%</strong>
    <div class="sl-alert__message">Build minutes reset on the 1st.</div>
  </div>
  <div class="sl-alert__actions">
    <button class="sl-button sl-button--sm">View</button>
    <button class="sl-button sl-button--ghost sl-button--sm sl-button--icon sl-alert__close" aria-label="Dismiss">x</button>
  </div>
</div>
```
Severity: none (normal, neutral) · `--info` · `--success` · `--warning` · `--error`. Icons: info, check-circle, alert-triangle, alert-circle. `role="alert"` for errors.

## Badge

```html
<span class="sl-badge sl-badge--success"><span class="sl-badge__dot" aria-hidden="true"></span>Ready</span>
```
Tones: none (neutral) · `--info` · `--success` · `--warning` · `--danger` · `--accent`.

## Progress & spinner

```html
<div class="sl-progress" role="progressbar" aria-label="Uploading" aria-valuemin="0" aria-valuemax="100" aria-valuenow="64" style="--_value: 64%">
  <div class="sl-progress__bar"></div>
</div>
<!-- indeterminate: add sl-progress--indeterminate, omit aria-valuenow, set aria-busy="true" -->
<span class="sl-spinner [sl-spinner--sm|--lg]" role="status" aria-label="Loading"></span>
```

## Kbd

```html
<kbd class="sl-kbd">⌘K</kbd>
```

## Layout

```html
<div class="sl-app-shell sl-app-shell--viewport">
  <header class="sl-app-bar">
    <button class="sl-button sl-button--ghost sl-button--icon" aria-label="Toggle navigation" aria-expanded="true">menu</button>
    <span class="sl-app-bar__title">…</span><span class="sl-spacer"></span>…
  </header>
  <nav class="sl-drawer sl-drawer--responsive is-open" aria-label="Navigation">
    <div class="sl-nav">
      <div class="sl-nav__heading">Section</div>
      <a class="sl-nav__item" href="…" aria-current="page"><svg class="sl-icon">…</svg><span class="sl-nav__label">Overview</span><span class="sl-nav__meta">12</span></a>
    </div>
  </nav>
  <div class="sl-drawer-scrim"></div>   <!-- only while an overlay drawer is open; click closes -->
  <main class="sl-main [sl-main--flush]">…</main>
</div>
```
Drawer variants: `--persistent` (closed = collapsed to 0), `--temporary` (overlay; `is-open` slides it in), `--mini`
(56px rail; `is-open` expands; labels hidden while collapsed), `--responsive` (persistent ≥ 900px, temporary below).
`is-open` = open in every variant. Overlay drawers: close on Escape/scrim, return focus to the toggle.

```html
<div class="sl-container [sl-container--sm|--md|--lg|--xl|--fluid]">…</div>

<div class="sl-grid sl-grid--spacing-4">            <!-- or style="--_gap: var(--sl-space-4)" -->
  <div class="sl-grid__item sl-col-xs-12 sl-col-md-6 sl-col-lg-4">…</div>
</div>
```
Grid spans `sl-col-{xs|sm|md|lg|xl}-{1..12}`, mobile-first (unset breakpoints inherit the next smaller). Spacing steps:
`0, 0-5, 1, 1-5, 2, 3, 4, 5, 6, 8, 10, 12, 16`.

```html
<div class="sl-stack sl-stack--row sl-stack--spacing-2 sl-stack--align-center sl-stack--justify-between sl-stack--wrap">…</div>
<span class="sl-spacer"></span>
<hr class="sl-divider" />  <div class="sl-divider sl-divider--vertical" role="separator" aria-orientation="vertical"></div>
```
Stack align: `--align-{start|center|end|stretch|baseline}`; justify: `--justify-{start|center|end|between}`.

```html
<article class="sl-card [sl-card--outlined] [sl-card--interactive (+ tabindex="0")]">
  <header class="sl-card__header">
    <div class="sl-card__titles"><h3 class="sl-card__title">Title</h3><p class="sl-card__subtitle">Subtitle</p></div>
    <div class="sl-card__actions">…</div>
  </header>
  <div class="sl-card__body [sl-card__body--flush]">…</div>
  <footer class="sl-card__footer">…buttons, primary last…</footer>
</article>

<div class="sl-toolbar [sl-toolbar--flat]" role="toolbar" aria-label="Formatting">…buttons, sl-divider--vertical…</div>
```

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

## Dialog

```html
<dialog class="sl-dialog sl-dialog--sm [sl-dialog--top] [sl-dialog--full-width] [sl-dialog--fullscreen] [is-closing]"
        aria-labelledby="d1-title" aria-describedby="d1-desc">       <!-- open with showModal(); or a <div role="dialog" aria-modal="true"> -->
  <div class="sl-dialog__scrim"></div>
  <div class="sl-dialog__panel" tabindex="-1">
    <header class="sl-dialog__header">
      <div class="sl-dialog__icon sl-dialog__icon--danger" aria-hidden="true"><svg class="sl-icon sl-icon--lg">alert-triangle</svg></div>
      <div class="sl-dialog__titles">
        <h2 class="sl-dialog__title" id="d1-title">Delete Slate.Wpf?</h2>
        <p class="sl-dialog__description" id="d1-desc">This can't be undone.</p>
      </div>
      <button class="sl-button sl-button--ghost sl-button--sm sl-button--icon sl-dialog__close" aria-label="Close">x</button>
    </header>
    <div class="sl-dialog__body">…</div>
    <footer class="sl-dialog__footer">…secondary…, primary last</footer>
  </div>
</dialog>
```
Widths `--xs --sm --md --lg --xl` (360/480/640/880/1120). Icon tones: none (accent) · `--danger --warning --success --info`.
Add `is-closing` for `motion.duration.base` before removing/closing to play the exit animation.
