# Colour

Two layers: **palette** (`palette.*`, raw values, never used by components) and **semantic**
(`color.*`, defined once per theme with identical paths). Components only use semantic tokens.

## Surfaces

| Token | Use | Light | Dark |
|---|---|---|---|
| `background.canvas` | App/page background | gray 25 | gray 950 |
| `background.surface` | Panels, cards, toolbars | white | gray 875 |
| `background.sunken` | Inside inputs, wells | white | gray 900 (recessed) |
| `background.raised` | Menus, dialogs, popovers | white | gray 850 (lifted) |
| `background.subtle` | Table headers, dialog footers, sidebars | gray 50 | gray 850 |
| `background.muted` | Tracks, segmented-control trough, skeletons | gray 100 | gray 825 |
| `background.hover` / `pressed` | Row and ghost-control states | | |

Rule: in **dark**, elevation = lightness (`sunken < canvas < surface < raised`). Tested in
`Surfaces_step_in_a_consistent_direction`.

## Text

`text.primary` (body), `text.secondary` (supporting copy, labels in dense UI), `text.tertiary` (meta,
timestamps, captions), `text.placeholder`, `text.link`, `text.onAccent` (on accent fills). All are ≥ 4.5:1 on
every reading surface in both themes.

## Accent & focus

- `accent.default/hover/pressed` — primary buttons, checked controls, active tab indicator, links.
- `accent.subtle` — selected-but-not-focused backgrounds, accent tints.
- `focus.ring` — the 2px focus ring, drawn with a 2px gap in the canvas colour. Light uses mint 600
  (mint 400 fails 3:1 on white); dark uses mint 400.
- `selection.background` + `selection.indicator` — selected rows/items: tinted fill plus a 2px inset bar.

## Borders

| Token | Use | Contrast rule |
|---|---|---|
| `border.default` | Dividers, card outlines, table rules | decorative |
| `border.strong` | Buttons, inputs (paired with the inset/milled shadow) | decorative* |
| `border.control` | Checkbox, radio, unchecked switch outlines | ≥ 3:1 (WCAG 1.4.11) |
| `border.controlHover` | Hovered control borders | — |

\* Inputs and buttons are identified by their label/text, fill and shadow, so their border may be lighter.
Selection controls are identified *only* by their outline, so theirs must meet 3:1.

## Status

Each of `success`, `warning`, `danger`, `info` has `fg` (text/icons), `bg` (tint), `border`, `solid` (filled
badges/buttons) and `onSolid`. Dark-theme tints are translucent and are contrast-checked after compositing.

## Inverse

`inverse.background/text/textMuted` — snackbars and the bulk-action bar: dark in the light theme, lifted
gray in the dark theme.

## Effects

`effect.highlight` and `effect.shade` are the two halves of the milled edge (top inner highlight, bottom
hairline). They are folded into `shadow.control` and `shadow.primary`; platforms that cannot render inset
shadows (WPF) draw them with a 1px top border in control templates.
