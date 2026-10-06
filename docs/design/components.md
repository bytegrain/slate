# Component contracts

Each component below is implemented on every platform with the **same name, options and states**. Values in
`code` are token paths. "All platforms must" items are acceptance criteria — tests should cover them.

Common to every interactive component:
- **Focus:** keyboard focus shows a 2px `color.focus.ring` ring with a 2px gap in `background.canvas`
  (web: `outline` + `outline-offset`; XAML: a focus visual border). Pointer clicks do not show it.
- **Disabled:** `opacity.disabled`, no hover/press feedback, not focusable (web: `disabled`/`aria-disabled`).
- **Sizes:** `small | medium | large` → `size.control.{sm,md,lg}` of the active density.
- **Motion:** state colour transitions use `motion.duration.fast` + `motion.easing.standard`.

---

## Button

Variants (`Slate.ButtonVariant`): `secondary` (default), `primary`, `ghost`, `danger`, `dangerSolid`, `link`.

| Part | Spec |
|---|---|
| Height | `size.control.*`; min width = height |
| Padding x | small 8 · medium 12 · large 16 |
| Radius | `radius.md` |
| Text | `typography.label` (13/500); large uses 14 |
| Icon | 16px (`size.icon.md`), gap `space.1_5`; icon-only buttons are square |

| Variant | Rest | Hover | Pressed |
|---|---|---|---|
| secondary | `background.surface`, 1px `border.strong`, `shadow.control`, `text.primary` | `background.hover`, `border.controlHover` | `background.pressed`, `shadow.pressed` |
| primary | `accent.default`, no border, `shadow.primary`, `text.onAccent` | `accent.hover` | `accent.pressed` |
| ghost | transparent, `text.secondary` | `background.muted`, `text.primary` | `background.pressed` |
| danger | as secondary with `status.danger.fg` text | | |
| dangerSolid | `status.danger.solid`, `status.danger.onSolid`, `shadow.primary` | darken via overlay | |
| link | no chrome, `text.link`, underline offset 3px | `text.linkHover` | |

States: `loading` (spinner replaces start icon, label kept, `aria-busy`, not clickable), `disabled`,
`fullWidth`. Optional `shortcut` renders a `Kbd` after the label.

All platforms must: activate on Enter and Space; icon-only buttons require an accessible label; loading
prevents repeated activation.

## Text field

| Part | Spec |
|---|---|
| Label | Always visible, above the field, `typography.label`; `required` adds a `status.danger.fg` asterisk |
| Field | height `size.control.*`, `radius.md`, `background.sunken`, 1px `border.strong`, `shadow.inset`, padding x 10 |
| Placeholder | `text.placeholder` — never a substitute for the label |
| Helper | `typography.caption`, `text.tertiary`, below the field |
| Error | replaces the helper: `status.danger.fg` + alert-circle icon; field border `status.danger.fg` |
| Adornments | `prefix`/`suffix` text segments (`background.subtle`, mono), or `startIcon` |
| Hover / focus | border `border.controlHover` / border `accent.default` + focus ring |

Options: `label`, `value`, `placeholder`, `helperText`, `error` (string → invalid), `required`, `disabled`,
`readOnly`, `type` (text, password, email, number, search…), `multiline` + `rows`, `prefix`, `suffix`,
`startIcon`, `size`.

All platforms must: associate label and field; reference helper/error text as the field's description;
expose invalid state to assistive tech (`aria-invalid`).

## Checkbox

16×16 box, `radius.sm`, 1px `border.control`, `background.sunken`. Checked/indeterminate: `accent.default`
fill, `text.onAccent` glyph (check / minus icon). Label `typography.body` with `space.2` gap; optional
description in `caption`/`text.tertiary`. Whole row is clickable. States: `checked`, `indeterminate`,
`disabled`. Space toggles.

## Switch

Track 36×20 (compact) / 44×24 (comfortable), `radius.full`. Off: `control.track` + `shadow.inset`;
on: `accent.default`. Thumb 16px (20px comfortable), `control.thumb` → `control.thumbShade` vertical
gradient with a 1px drop shadow; slides `motion.duration.fast`. Label beside it (default before, `labelPosition`).
Semantics: switch role with on/off state. Space toggles; Enter does not submit forms.

## Radio group

Radios: 16px circle, 1px `border.control`; checked fills `accent.default` with a 6px `text.onAccent` dot.
Group has a visible legend/label; arrow keys move selection; one tab stop for the group.

## Alert

Inline, persistent message. `radius.lg`, 1px `status.*.border`, `status.*.bg`, icon in `status.*.fg`
(info / check-circle / alert-triangle / alert-circle), title `bodyStrong` in `text.primary`, message in
`text.secondary`. Optional actions (ghost/secondary small buttons) and `dismissible` close button.
`severity: info | success | warning | error` (+ `normal` uses neutral `background.subtle`). Errors use
alert semantics (assertive); others are status (polite) only when they appear dynamically.

## Badge

20px high, `radius.sm`, `typography.caption` 600, padding x 7. Tones: neutral (`background.subtle`,
`text.secondary`, inset hairline), the four statuses (`status.*.bg/fg` with inset `status.*.border`), and
`accent` (`accent.default` / `text.onAccent`). `dot` adds a 6px leading dot in currentColor.

## Progress

Linear: 6px track `background.muted` with `shadow.inset`, fill `accent.default`, `radius.full`; determinate
(value 0–100) or indeterminate (sliding 30% bar, paused under reduced motion). Spinner: 2px ring 16px,
`currentColor`, 0.8s linear rotation. Both expose progressbar semantics with value/min/max or busy.

## Kbd

Inline key hint: mono 11px, `text.secondary`, `background.surface`, 1px `border.strong` with a 1px bottom
shadow in `border.strong`, `radius.xs`, padding 0 5, line-height 17px.

## Icon

Renders an icon from `design/icons/icons.json`: stroke `currentColor`, `strokeWidth` 2, round caps/joins,
no fill, default 16px. Decorative unless given a label.
