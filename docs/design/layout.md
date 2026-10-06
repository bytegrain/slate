# Layout framework

Every platform ships the same layout primitives with the same names and parameters (adapted to platform
idiom: elements on web, components on Blazor, controls on WPF/Avalonia).

| Primitive | Web element | Blazor | WPF / Avalonia |
|---|---|---|---|
| Root/provider | `<sl-provider>` | `<SlateProvider>` | `SlateTheme` resources + `SlateRoot` |
| App shell | `<sl-app-shell>` | `<SlAppShell>` | `AppShell` |
| App bar | `<sl-app-bar>` | `<SlAppBar>` | `AppBar` |
| Drawer | `<sl-drawer>` | `<SlDrawer>` | `Drawer` (inside `AppShell`) |
| Main | slot `main` | `<SlMain>` | `AppShell.Content` |
| Container | `<sl-container>` | `<SlContainer>` | `Container` |
| Grid | `<sl-grid>` + `<sl-grid-item>` | `<SlGrid>` + `<SlItem>` | `ResponsiveGrid` + attached spans |
| Stack | `<sl-stack>` | `<SlStack>` | `Stack` (Panel) |
| Spacer | `<sl-spacer>` | `<SlSpacer>` | `Spacer` |
| Divider | `<sl-divider>` | `<SlDivider>` | `Divider` |
| Card | `<sl-card>` | `<SlCard>` | `Card` |
| Toolbar | `<sl-toolbar>` | `<SlToolbar>` | `Toolbar` |

## Provider / root

Sets theme (`light | dark | system`) and density (`compact | comfortable`) for its subtree and hosts the
snackbar and dialog layers. One per app (nesting is allowed to scope a theme to a region).

## App shell

```
┌────────────────────────────── app bar (56px, z.appbar) ──────────────────────────────┐
├──────────── drawer ────────────┬───────────────────── main ───────────────────────────┤
│ 248px (full) / 56px (mini)     │ scrolls independently; padding space.6 (space.4 <sm) │
└────────────────────────────────┴──────────────────────────────────────────────────────┘
```

Drawer variants: `persistent` (pushes content; default ≥ md), `temporary` (overlays with scrim; default
< md; closes on scrim click/Escape and returns focus to the toggle), `mini` (icon rail, labels as
tooltips). `responsive` (default) switches persistent → temporary below `md`.

Desktop apps (WPF/Avalonia) may replace the app bar with the 36px custom title bar (`size.titlebar`):
icon, title, centred search, and neutral window buttons.

## Container

Centres content with a max width: `container.sm` 600 / `md` 900 / `lg` 1200 / `xl` 1440 (default `lg`),
or `fluid`. Horizontal padding `space.4`, `space.6` from `sm`.

## Grid

12 columns, mobile-first spans per breakpoint (`xs sm md lg xl`). Unset spans inherit from the next smaller
breakpoint; `xs` defaults to 12. `spacing` is a space-token step (default 4 → 16px gaps). Implemented by
`Slate.Layout.GridSpan` in Slate.Core so every .NET platform resolves identically.

## Stack

One-dimensional flex: `direction` row|column, `spacing` (token step), `align` start|center|end|stretch|baseline,
`justify` start|center|end|between, `wrap`. Use Stack for anything that is not a 2-D grid.

## Card

Surface + `shadow.e1` + `radius.xl`. Optional header bar (title, subtitle, actions; bottom hairline),
body (padding `space.5`), footer (`background.subtle`, top hairline, actions right-aligned). `outlined`
variant swaps shadow for a `border.default` hairline; `interactive` lifts to `e2` on hover.
