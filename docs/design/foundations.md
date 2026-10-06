# Typography, spacing & shape

## Type

Two families: **Instrument Sans** (all UI) and **IBM Plex Mono** (IDs, numbers in tables, code, keyboard
hints). Both are SIL OFL; platform packages embed them and fall back to Segoe UI / Cascadia Mono.

| Style | Size / weight / line | Use |
|---|---|---|
| `display` | 56 / 600 / 1.15, −0.035em | Marketing and empty-state heroes only |
| `h1` | 36 / 600 / 1.15, −0.03em | Page titles (rare in apps) |
| `h2` | 28 / 600 / 1.15 | Section titles |
| `h3` | 22 / 600 / 1.3 | Card and dialog-heavy titles |
| `title` | 16 / 600 / 1.3 | Dialog titles, panel headers |
| `body` | 14 / 400 / 1.5 | Default text |
| `bodyStrong` | 14 / 600 / 1.5 | Emphasis within body |
| `label` | 13 / 500 / 1.3 | Form labels, buttons, tabs |
| `caption` | 12 / 400 / 1.3 | Helper text, timestamps |
| `overline` | 11 mono / 500, +0.04em caps | Section eyebrows, table group headers |
| `mono` | 13 mono | Data |

Rules: numbers that are compared (tables, stats) use tabular numerals; never set body copy below 13px; at most
three sizes in one view.

## Spacing

4px grid with half steps for tight optical alignment: `space.0_5`(2) `1`(4) `1_5`(6) `2`(8) `3`(12) `4`(16)
`5`(20) `6`(24) `8`(32) `10`(40) `12`(48) `16`(64).

- Inside controls: 8–12px horizontal padding.
- Between related controls: `space.2`. Between groups: `space.4`–`space.6`. Between sections: `space.8`+.

## Shape

| Radius | px | Use |
|---|---|---|
| `xs` | 3 | Kbd, inline code |
| `sm` | 4 | Checkboxes, badges, chips |
| `md` | 6 | Buttons, inputs, menu items |
| `lg` | 8 | Menus, popovers, snackbars |
| `xl` | 10 | Cards, panels |
| `2xl` | 12 | Dialogs |
| `full` | — | Switches, avatars, pills |

Nested radii are concentric: inner = outer − padding.

## Control sizes

| Size | Compact (default) | Comfortable |
|---|---|---|
| Small | 26 | 32 |
| Medium | 32 | 40 |
| Large | 40 | 48 |

## Elevation

`shadow.e1` resting cards · `shadow.e2` hover cards, dropdowns · `shadow.e3` dialogs, snackbars, command
palette. `shadow.control` the milled edge on secondary controls · `shadow.primary` the same on accent fills ·
`shadow.inset` inputs · `shadow.pressed` pressed controls.

## Motion

`motion.duration.fast` (120ms) hovers and toggles · `base` (160ms) small enter/exit · `slow` (240ms)
dialogs and drawers. Easing `standard` for most, `emphasized` for entering, `exit` for leaving. All motion
is removed under `prefers-reduced-motion` / the OS "reduce animations" setting.

## Layering

`z.dropdown` 1000 < `sticky` < `appbar` < `drawer` < `dialog` < `snackbar` < `tooltip` 1600.
