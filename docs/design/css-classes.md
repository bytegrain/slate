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

---

# Wave 2

## Overlays (shared rules)

Every floating surface (select listbox, menu, submenu, popover, tooltip, date-picker calendar) is a
`.sl-popover-panel` element, plus the component's own panel class.
- **Top layer:** web promotes the panel with `popover="manual"` + `showPopover()`. Blazor does the same, or
  portals the panel into `sl-provider`. While the panel is closed it carries `hidden`.
- **Position:** set inline `left`/`top`/`max-height`, computed by the shared engine (`core/overlay` ↔
  `Slate.Core.Overlay.PopoverPositioner`).
  - Write `data-side="top|bottom|left|right"`, which drives the enter animation, and
    `data-actual-placement="bottom-start"…`.
  - Write `--_arrow: <px>` when there is an arrow.
  - `data-placement` holds the *requested* placement.
- **Repositioning:** on scroll (capture) and on resize.
- **Dismissal is a stack:** Escape closes only the topmost overlay and is consumed. A pointer-down outside
  closes overlays from the top down until it reaches the one that contains the target.
- **Focus:** returns to the anchor when an overlay is dismissed by keyboard or Escape.
- **Tokens:** `component.popover.{radius,background,border,shadow,padding}`, `component.menu.*`,
  `component.tooltip.*`.

Wrapper spans `.sl-popover__anchor`, `.sl-menu__trigger` and `.sl-tooltip__anchor` are `display: contents`.

## Select

```html
<div class="sl-field sl-select sl-field--outlined [sl-select--multiple] [sl-select--searchable] [is-open]
            [sl-field--small|--large] [sl-field--radius-*] [is-disabled] [is-invalid]">
  <label class="sl-field__label" id="s-label" for="s-input">Region</label>
  <div class="sl-field__control sl-select__control">
    <!-- select-only -->
    <button class="sl-select__trigger" type="button" id="s-input" role="combobox" aria-haspopup="listbox"
            aria-expanded="false" aria-controls="s-list" aria-labelledby="s-label" aria-activedescendant="opt-3">
      <span class="sl-select__value"><span class="sl-select__text">Europe West</span>
        <!-- or --> <span class="sl-select__placeholder">Choose…</span></span>
    </button>
    <!-- searchable and/or multiple: chips + input inside __value -->
    <span class="sl-select__value">
      <span class="sl-select__chip"><span class="sl-select__chip-text">bug</span>
        <button class="sl-select__chip-remove" type="button" tabindex="-1" aria-label="Remove bug">x</button></span>
      <span class="sl-select__chip sl-select__chip--more">+2</span>          <!-- beyond MaxVisibleChips -->
      <input class="sl-field__input sl-select__input" id="s-input" role="combobox" aria-autocomplete="list"
             aria-expanded="true" aria-controls="s-list" aria-activedescendant="opt-3" autocomplete="off">
    </span>
    <button class="sl-field__clear" type="button" aria-label="Clear">x</button>   <!-- Clearable && has value -->
    <span class="sl-select__spinner sl-spinner" aria-hidden="true"></span>       <!-- Loading -->
    <span class="sl-select__chevron" aria-hidden="true">chevron-down</span>
  </div>
  <div class="sl-field__footer"><p class="sl-field__helper|sl-field__error">…</p></div>
  <div class="sl-popover-panel sl-select__panel" hidden>                        <!-- min-width = trigger width -->
    <div class="sl-select__listbox" role="listbox" id="s-list" aria-label="Region" [aria-multiselectable="true"]>
      <div class="sl-select__group" role="group" aria-labelledby="s-list-g0">
        <div class="sl-select__group-label" id="s-list-g0">Europe</div>
        <div class="sl-select__option [is-active] [is-selected] [is-disabled]" role="option" id="opt-0"
             aria-selected="true" [aria-disabled="true"]>
          <span class="sl-select__option-check" aria-hidden="true">check</span>   <!-- multiple: sl-select__option-box -->
          <span class="sl-select__option-icon">icon</span>                      <!-- optional -->
          <span class="sl-select__option-text"><span class="sl-select__option-label">Europe <mark>We</mark>st</span>
            <span class="sl-select__option-description">London</span></span>
        </div>
      </div>
      <div class="sl-select__option sl-select__option--create" role="option">Create “…”</div>  <!-- Creatable -->
      <div class="sl-select__empty">No results</div>                                         <!-- EmptyContent -->
    </div>
  </div>
</div>
```
- **Active option:** `is-active` is the `aria-activedescendant` target; DOM focus never leaves the combobox.
- **Search highlight:** filter matches are wrapped in `<mark>`.
- **Keys:**
  - ↑/↓ moves the active option; PgUp/PgDn moves it by 10; Home/End jump to the ends (select-only).
  - Enter selects; Space selects (select-only); Alt+↓ opens; Esc closes; Tab selects and closes.
  - Backspace on an empty input removes the last chip.
  - Printable characters run typeahead (select-only).
- **Form value:** the value key, or one entry per value when `Multiple`. Validity: `valueMissing`.

## Menu (and context menu)

```html
<span class="sl-menu__trigger"><button aria-haspopup="menu" aria-expanded="false">Actions</button></span>
<div class="sl-popover-panel sl-menu__panel" role="menu" data-placement="bottom-start" hidden>
  <div class="sl-menu-label">Group heading</div>
  <div class="sl-menu-item sl-tone-neutral [is-active] [is-disabled] [is-expanded]" role="menuitem" tabindex="-1">
    <span class="sl-menu-item__icon">pencil</span>
    <span class="sl-menu-item__label">Rename</span>
    <kbd class="sl-menu-item__shortcut">F2</kbd>
    <span class="sl-menu-item__chevron">chevron-right</span>             <!-- has submenu: aria-haspopup="menu" -->
  </div>
  <div class="sl-menu-item sl-tone-neutral is-checked" role="menuitemcheckbox" aria-checked="true">
    <span class="sl-menu-item__check">check</span> <span class="sl-menu-item__label">Show hidden</span></div>
  <div class="sl-menu-separator" role="separator"></div>
  <div class="sl-menu-item sl-tone-danger" role="menuitem">…Delete…</div>
</div>
```
- **Web nesting:** a web `sl-menu-item` host carries the role, and `.sl-menu-item` sits in its shadow root. Flat
  markup (Blazor) puts role and class on the same element.
- **Checkable items:** an item is checkable only when `Checked` is non-null, which renders `menuitemcheckbox`.
  A checkable item that is unchecked keeps an empty `__check` column.
- **Submenus:** a submenu is a nested `.sl-menu__panel` with placement `right-start`. → or Enter opens it and
  ← closes it.
- **Keys:** ↑/↓ wrap and skip separators and disabled items; Home/End jump to the ends; typeahead; Tab closes.
- **ContextMenu:** opens at the pointer on `contextmenu`, or at the trigger on Shift+F10 / the ContextMenu key.
  Its placement is `bottom-start` at the point.
- **Events:** activation fires `sl-select {item, value, checked}` and closes the whole menu tree.

## Tabs

```html
<div class="sl-tabs sl-tabs--line [sl-tabs--small|--large] [sl-tabs--column]">
  <div class="sl-tabs__bar">
    <div class="sl-tabs__list" role="tablist" aria-orientation="horizontal|vertical">
      <!-- A div, not a button: a closable tab contains its own close button, and buttons can't nest. -->
      <div class="sl-tab sl-tab--line [sl-tab--small|--large] [sl-tab--column] [is-selected]"
           role="tab" id="t1" aria-selected="true" aria-controls="p1" tabindex="0" [aria-disabled="true"]>
        <span class="sl-tab__icon">home</span><span class="sl-tab__label">Overview</span>
        <span class="sl-tab__badge">12</span>
        <button class="sl-tab__close" type="button" tabindex="-1" aria-label="Close Overview">x</button>   <!-- Closable -->
      </div>
      <span class="sl-tabs__indicator" style="--_x:0px;--_y:0px;--_w:88px;--_h:2px"></span>  <!-- line only -->
    </div>
    <div class="sl-tabs__overflow" hidden>…two ghost icon buttons “Scroll tabs left/right”…</div>
  </div>
  <div class="sl-tabs__panels">
    <div class="sl-tab-panel [sl-tab-panel--column]" role="tabpanel" id="p1" aria-labelledby="t1" tabindex="0">…</div>
  </div>
</div>
```
- **Modifiers live on each `.sl-tab`**, not only on `.sl-tabs`: shadow boundaries block ancestor selectors on
  web, so flat markup must also put them on the tab.
- **Variants:** `--line` (default), `--pills`, `--enclosed`.
- **Indicator:** measured from the selected tab's box.
- **Keys:** arrows (←/→ for row, ↑/↓ for column) select automatically; Home/End jump to the ends; Delete closes
  a closable tab and fires `sl-closed {key}`.
- **Panels:** inactive panels are `hidden` and removed unless `KeepAlive`.

## Tooltip

```html
<span class="sl-tooltip__anchor"><button aria-description="Copy link (⌘C)">…</button></span>
<div class="sl-tooltip" role="tooltip" id="tt1" data-placement="top" data-side="top" hidden>
  Copy link<kbd class="sl-kbd">⌘C</kbd>
</div>
```
- **Anchor:** the web default slot is the anchor; rich content goes in `slot="content"`. Since ARIA IDs can't
  cross shadow roots, the anchor gets `aria-description`. Flat markup may use `aria-describedby="tt1"` instead.
- **Show:** after `Delay` (default 500 ms) on hover or focus. Shows immediately within 400 ms of another tooltip
  hiding (warm-up).
- **Hide:** on leave, blur, pointer-down and Escape.
- **Tokens:** `component.tooltip.*`.

## Popover

```html
<span class="sl-popover__anchor"><button aria-haspopup="dialog" aria-expanded="true">Filters</button></span>
<div class="sl-popover-panel sl-popover__panel" role="dialog" [aria-modal="true"] tabindex="-1"
     data-placement="bottom-start" data-side="bottom" style="--_arrow:24px">
  <span class="sl-popover__arrow" aria-hidden="true"></span>
  …content…
</div>
```
- **Behaviour:** clicking the anchor toggles the popover.
- **`Modal`:** focuses the first focusable element inside and traps Tab.
- **`CloseOnOutsideClick`:** defaults to true.
- **`MatchAnchorWidth`:** sets `min-width` to the anchor's width.
- **Events:** `sl-open-changed {open}`.

## DatePicker

```html
<div class="sl-field sl-date-picker sl-field--outlined sl-date-picker--single|--range [is-open] [sl-date-picker--inline]">
  <label class="sl-field__label" for="d1">Due date</label>
  <div class="sl-field__control">
    <input class="sl-field__input sl-date-picker__input" id="d1" type="text" inputmode="numeric"
           aria-haspopup="dialog" aria-expanded="false" placeholder="M/d/yyyy">
    <button class="sl-field__clear" aria-label="Clear">x</button>
    <button class="sl-date-picker__trigger" type="button" aria-label="Open calendar">calendar</button>
  </div>
  <div class="sl-popover-panel sl-date-picker__panel" role="dialog" aria-label="Due date">  <!-- inline: no panel wrapper -->
    <div class="sl-calendar">
      <div class="sl-calendar__presets"><button class="sl-calendar__preset [is-selected]">Last 7 days</button>…</div>
      <div class="sl-calendar__main">
        <div class="sl-calendar__header">
          <button class="sl-button sl-button--ghost sl-tone-neutral sl-button--small sl-button--icon-only" aria-label="Previous month">‹</button>
          <span class="sl-calendar__title" id="d1-title" aria-live="polite">October 2026</span>
          <button … aria-label="Next month">›</button>
        </div>
        <table class="sl-calendar__grid" role="grid" aria-labelledby="d1-title">
          <thead><tr><th class="sl-calendar__weekday" scope="col" abbr="Sunday">Su</th>…</tr></thead>
          <tbody><tr>
            <td class="sl-calendar__cell" role="gridcell" aria-selected="false">
              <button class="sl-calendar__day [is-outside] [is-today] [is-selected] [is-in-range] [is-in-preview]
                             [is-range-start] [is-range-end]" type="button" tabindex="-1|0"
                      data-date="2026-10-06" aria-label="Tuesday, October 6, 2026" [aria-current="date"] [disabled]>6</button>
            </td>…
          </tr>…</tbody>   <!-- always 6 rows × 7 -->
        </table>
      </div>
    </div>
  </div>
</div>
```
- **Focus:** a roving `tabindex="0"` sits on the focused day.
- **Keys:** arrows move a day or a week, PgUp/PgDn a month, Shift+PgUp/PgDn a year, and Home/End the start or end
  of the week (all via `navigateCalendar`). Enter or Space picks; Escape closes.
- **Range:** the first pick is held as the pending start, and hovering paints `is-in-preview`.
- **Typed input:** parsed with the culture's short pattern when the field blurs or Enter is pressed.
- **Form value:** ISO `yyyy-MM-dd`, or `start/end` for a range.
- **Icon:** the `calendar` icon is drawn inline for now — **add `calendar` to `design/icons/icons.json`**.

## TreeView

```html
<div class="sl-tree [sl-tree--dense] [sl-tree--checkbox]" role="tree" [aria-multiselectable="true"]>
  <div class="sl-tree__item" role="treeitem" id="t-0" tabindex="0" aria-level="1" aria-setsize="3" aria-posinset="1"
       [aria-expanded="true"] [aria-selected="true"] [aria-checked="true|false|mixed"] [aria-busy="true"]>
    <div class="sl-tree__row" style="--_depth:0">
      <span class="sl-tree__expander [is-leaf]" aria-hidden="true">chevron-right</span>   <!-- rotates when expanded -->
      <span class="sl-tree__checkbox" data-state="checked|unchecked|indeterminate" aria-hidden="true"></span>
      <span class="sl-tree__icon">folder</span>
      <span class="sl-tree__label">Slate.<mark>Core</mark></span>
      <span class="sl-tree__spinner sl-spinner"></span>                                    <!-- loading children -->
    </div>
    <div class="sl-tree__children" role="group">…nested .sl-tree__item…</div>
  </div>
  <div class="sl-tree__empty">No matches</div>
</div>
```
- **Selection:** the item gets `is-selected` / `aria-selected` in `single` and `multiple` modes.
- **Indent:** `--_depth` × `component.tree.indent`.
- **Keys:** handled by `TreeModel.navigate`.
  - ↑/↓ move; → expands or moves to the first child; ← collapses or moves to the parent.
  - Home/End jump to the ends; `*` expands siblings; typeahead.
  - Enter or Space selects (or toggles the check). Shift and Ctrl extend the selection in `multiple` mode.
- **Ids:** `Expanded` and `SelectedItems` accept items or ids. `itemKey` (default `item.id ?? text`) is a web extra.

## SegmentedControl

```html
<div class="sl-segmented [sl-segmented--small|--large] [sl-segmented--full]" role="radiogroup" aria-label="Range">
  <button class="sl-segmented__item [is-selected]" type="button" role="radio" aria-checked="true" tabindex="0">
    [<svg class="sl-icon">…</svg>] 7d</button>…
</div>
```
Focus is roving: arrows move it and select, and Home/End jump to the ends. Fires `sl-value-changed`.

## Slider

```html
<div class="sl-slider sl-tone-accent [sl-slider--range] [is-disabled]">
  <div class="sl-slider__header"><span class="sl-slider__label" id="sl1">Volume</span>
    <output class="sl-slider__value">20 – 60</output></div>                               <!-- ShowValue -->
  <div class="sl-slider__track">
    <span class="sl-slider__rail"></span>
    <span class="sl-slider__fill" style="--_start:0.2;--_end:0.6"></span>                  <!-- fractions 0..1 -->
    <span class="sl-slider__tick" style="--_pos:0.2"></span>…                              <!-- Ticks -->
    <span class="sl-slider__thumb [is-dragging]" role="slider" tabindex="0" style="--_pos:0.2"
          aria-valuemin="0" aria-valuemax="60" aria-valuenow="20" aria-label="Volume minimum"></span>
    <span class="sl-slider__thumb" … aria-valuemin="20" aria-valuemax="100" aria-label="Volume maximum"></span>
  </div>
</div>
```
- **Ticks:** one per step when there are 50 or fewer steps, otherwise 11.
- **Keys:** handled by `sliderKey` — arrows step, PgUp/PgDn move 10%, Home/End jump to the ends.
- **Range:** each thumb is clamped by the other.
- **Form value:** `value`, plus a second entry (`rangeEnd`) for a range.

## Avatar

```html
<span class="sl-avatar sl-tone-accent [sl-avatar--small|--large]" role="img" aria-label="Aaron Griffin, online" data-status="online">
  <span class="sl-avatar__initials" aria-hidden="true">AG</span>        <!-- or <img class="sl-avatar__image" alt=""> -->
  <span class="sl-avatar__status sl-avatar__status--online" aria-hidden="true"></span>
</span>
<span class="sl-avatar-group">…avatars overlap…</span>
```
- **Initials and tone:** `avatarInitials(name)` and `avatarTone(name)` (shared core). Tone is derived when not set.
- **Fallback:** if the image fails to load, the initials are shown.

## Breadcrumbs

```html
<nav class="sl-breadcrumbs" aria-label="Breadcrumb"><ol class="sl-breadcrumbs__list">
  <li class="sl-breadcrumbs__item"><a class="sl-breadcrumbs__link" href="#">[icon] Workspace</a>
    <span class="sl-breadcrumbs__separator" aria-hidden="true">chevron-right</span></li>
  <li class="sl-breadcrumbs__item"><!-- collapsed: a Menu whose trigger is the ellipsis -->
    <button class="sl-breadcrumbs__link sl-breadcrumbs__more" aria-label="Show 2 more" aria-haspopup="menu">…</button> …</li>
  <li class="sl-breadcrumbs__item"><span class="sl-breadcrumbs__current" aria-current="page">Button.razor</span></li>
</ol></nav>
```
- **Collapsing:** when the count exceeds `MaxItems`, keep the **first** item and the **last `MaxItems − 1`** items.
- **Events:** clicking a link fires `sl-item-click {item, index}`, which is cancelable.

## Pagination

```html
<nav class="sl-pagination [sl-pagination--small|--large]" aria-label="Pagination">
  <span class="sl-pagination__summary" aria-live="polite">41–50 of 200</span>              <!-- TotalCount set -->
  <ul class="sl-pagination__pages">
    <li><button class="sl-pagination__page" aria-label="Previous page" [disabled]>‹</button></li>
    <li><button class="sl-pagination__page" aria-label="Page 1">1</button></li>
    <li class="sl-pagination__ellipsis" aria-hidden="true">…</li>
    <li><button class="sl-pagination__page" aria-label="Page 5" aria-current="page">5</button></li>…
    <li><button class="sl-pagination__page" aria-label="Next page">›</button></li>
  </ul>
  <label class="sl-pagination__size">Rows per page
    <select class="sl-pagination__select"><option>10</option>…</select></label>          <!-- PageSizes set -->
</nav>
```
- **Pages:** from `paginationRange(page, pageCount, siblings)`.
- **Page size:** changing it keeps the first visible item (`pageForFirstItem`).
- **Events:** `sl-page-changed {page}` and `sl-page-size-changed {pageSize, page}`.

## Skeleton

```html
<span class="sl-skeleton sl-skeleton--text|--rect|--circle [is-animated]" style="width:…;height:…"></span>
<span class="sl-skeleton-group">…Lines × text skeletons, last at 60% width…</span>
```
- **Accessibility:** the host is `aria-hidden`.
- **Motion:** the shimmer is disabled under reduced motion.
- **Tokens:** `component.skeleton.background` (the shimmer highlight is `color.background.surface`).
