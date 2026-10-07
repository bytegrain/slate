# Handoff — state of Slate (2026-10-07)

Everything below is committed on `main` (local repo, never pushed). Working tree clean at handoff.

## What Slate is

The owner (Aaron) explored ten visual directions on a Claude Design canvas and chose **Alloy** — "precise,
calm, built for the eight-hour day": cool greys, deep teal accent (#0E5E6F light / #3FB3C6 dark), mint used
only for focus/selection, a "milled" control edge (1px inner top highlight + hairline shade), Instrument Sans +
IBM Plex Mono, compact density by default, keyboard-first. He wanted: four platform packages (Web, Blazor, WPF,
Avalonia) each with demos; a shared design rule as the building block; a MudBlazor-style layout framework;
well-designed systems (snackbars, dialogs); MudBlazor-level configurability; and a "really high-tech data
table". He prefers to see real rendering (screenshots of demos) rather than test counts alone.

## Architecture (see docs/ARCHITECTURE.md for the full picture)

```
design/tokens (DTCG: primitives, light/dark semantic, components)  design/icons/icons.json  design/api/components.json
        └── tools/Slate.Tokens generator ──► CSS · WPF XAML · Avalonia XAML · C# (SlateTokens/SlateIcons) · TS icons · resolved JSON
src/Slate.Core   tokens + platform-free engines (+ identical TS port in packages/web/src/core, fixture-proven)
packages/web     @bytegrain/slate-web: class-based CSS (dist/slate.css, works without JS) + Lit custom elements (sl-*)
src/Slate.Blazor Razor components rendering the web markup contract (docs/design/css-classes.md)
src/Slate.Wpf    implicit styles for native controls + Slate controls (net10.0-windows)
src/Slate.Avalonia  SlateTheme over FluentTheme + Slate controls (Avalonia 12.1.3)
demos/*          one demo app per .NET platform; web demo in packages/web/demo
```

## What's done (in commit order — `git log --oneline`)

1. **Foundation** — tokens, generator with validation (references, cycles, theme/density parity, strict icon
   paths), WCAG design-rule tests, Slate.Core (SnackbarQueue, DialogStack, breakpoints/grid spans), docs.
2. **Four platform packages** — theme switching (light/dark/system), density, layout framework (app shell,
   app bar, drawer persistent/temporary/mini/responsive, container, 12-col grid, stack, card, toolbar),
   components (button, text field, checkbox, switch, radio, alert, badge, progress, spinner, kbd, icon, text),
   snackbar + dialog services (incl. confirm/message box), demos, tests.
3. **Configurability** (MudBlazor-style, four levels: theme → component tokens → app defaults → parameters):
   `ThemeBuilder` (brand colour → accessible accent palette, proven for 80 colours × light/dark; radius scale;
   fonts; token overrides; alias propagation), component tokens (`component.*`, live `var()` aliases in CSS,
   `Sl.Component.*` in XAML), `SlateDefaults`, shared vocabulary (Tone, ButtonVariant×Tone, FieldVariant,
   CardVariant, Radius, Placement, …), canonical API contract + conformance tests, Theming + Playground demos.
4. **Wave 2 components** (all four platforms) — Select (search/multiple/creatable/groups), Menu + context menu,
   Tabs, Tooltip, Popover, DatePicker (single/range/presets), TreeView (lazy, tri-state), SegmentedControl,
   Slider (range), Avatar, Breadcrumbs, Pagination, Skeleton; shared logic in `Slate.Overlays/Dates/Collections`.
5. **Data grid** — engine in `src/Slate.Core/Data` (+ TS port): typed filters, multi-sort, grouping +
   aggregates, tree data, detail rows, paging, layout (flex/pinning/column virtualisation/auto-size), viewport,
   keyed selection, WAI-ARIA navigation, cell + batch edit sessions, server data sources with block cache,
   CSV/TSV export, JSON state. Renderers on all four platforms (`sl-data-grid`, `SlDataGrid<T>`, `sl:DataGrid`).
   Spec: `docs/design/data-grid.md` (incl. Engine API + Renderer notes).

Test counts at handoff: tokens 116 · core 590 · web 578 · Blazor 390 · Avalonia 516 · WPF ResourceTests 160
(+ ~90 Windows-only WPF UI tests never executed).

## Key decisions (don't undo without reason)

- Light focus ring is mint **600** (#0B8F6C), not the 400 from the mockups (fails 3:1 on white).
  `border.control` (checkbox/radio/switch outlines) is kept ≥ 3:1; inputs/buttons use the lighter `border.strong`.
- Text sorting in the grid is case-insensitive ordinal with an ordinal tie-break so C# and TS agree
  (no culture collation yet). Grid paging counts group rows. Tree data ignores GroupBy.
- `GridState.Order` (set only by MoveColumn) is separate from `Columns` (overrides) — resizing/pinning/hiding
  never reorders.
- XAML names: both XAML platforms use contract type names (`sl:DatePicker`, `sl:Menu`, `sl:TreeView`, …);
  aliases only where a framework member exists (`ContainerMaxWidth`, `DialogMaxWidth`, `AsContextMenu`,
  `TooltipPlacement`, `DisplayName`, `Minimum/Maximum`, `ShowTicks`), recorded in components.json.
  WPF's `Ui` attached-property class was renamed `Sl` to match Avalonia.
- Avalonia's SlateTheme sits on FluentTheme (Fluent palette mapped to Slate tokens) so unstyled controls blend.
  Avalonia's spinner is `LoadingSpinner` (name clash). Tooltips on nav items only in mini drawers.
- Blazor overlays use the browser top layer with a C# Escape/outside-click stack; Blazor grid keys rows by row
  key and emits plain cells as markup strings (interaction in slate.js) to keep Blazor Server diffs small
  (~5 KB per 6-row scroll shift). Blazor `Content` slot = `ChildContent`.
- Icon sizing CSS lives in `icon.css`, folded into every web component's shadow-root reset (`hostReset`) and
  into slate.css — components must not rely on other stylesheets for icons.
- Tabs markup: `div[role=tab]` (a closable tab nests a close button).
- Package/central versions: `Directory.Packages.props` (Avalonia 12.1.3, xunit 2.9.3 for most tests, xunit.v3
  3.2.2 for Avalonia headless, bUnit 2.11.3). Use central versions, not VersionOverride.

## Known issues / unverified

1. **WPF has never run.** Everything compiles with 0 warnings; ResourceTests (XAML/resource/conformance checks)
   pass on macOS; all UI tests and the WPF demo need Windows. Highest-risk area.
2. **Avalonia grid not yet seen in a real window** (display was asleep). Run the demo, open "Data grid",
   and `--screenshot` to review.
3. Blazor grid: in server mode "select all matching" only includes loaded items; fast typing to begin an edit
   can drop keystrokes on Blazor Server.
4. Web grid: very fast flick scrolling drops some frames (p95 ~28 ms); typical scrolling ~60fps.
5. Blazor TreeView rebuilds when `Items` is a new reference (callers must pass a stable collection).
6. Breakpoint values are duplicated in web CSS media queries (CSS vars can't be used there) — consider a
   generator test asserting they match the tokens.
7. Web form-associated controls still use fake `ElementInternals` in unit tests; Playwright currently smoke-tests Web and Blazor demos in Chromium.
8. NuGet publishing to private GitHub Packages and public npm publishing are configured; the first tagged publish still needs to be verified.
9. No LICENSE file yet (fonts are OFL; licence files in design/fonts).

## Suggested next steps (owner said "keep it up"; these were proposed)

1. **CI**: GitHub Actions — token `--check`, all .NET suites, npm test/build, a **Windows runner for WPF UI
   tests**, Playwright real-browser tests for web/Blazor.
2. **Packaging**: tag-triggered publishing is configured for private GitHub Packages NuGet (Slate.Core,
   Slate.Blazor incl. static assets, Slate.Wpf, Slate.Avalonia) and public npm (`@bytegrain/slate-web` with
   dist/slate.css + fonts). Configure npm trusted publishing and verify the first tagged release.
3. **Docs site** generated from components.json + tokens.resolved.json (+ live web components).
4. **More components** on the same contract: command palette, toasts with progress, file upload, number
   input, rich text, charts (use the dataviz guidance), kanban/board.
5. Address known issues above (WPF verification first).

## How work was organised (useful pattern)

The lead agent wrote specs/contracts and shared engines, then ran one sub-agent per platform in parallel
(web first when Blazor depends on its CSS), each limited to its own folders, followed by lead verification
(build, tests ×N for flakiness, real screenshots) and one commit per platform. Shared files edited
concurrently (`packages/web/src/index.ts`, components.json) need re-read-before-edit discipline.
