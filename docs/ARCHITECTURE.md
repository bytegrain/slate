# Slate architecture

Slate is one design system (**Alloy**) shipped as four native packages. Everything visual starts from a
single design source and flows outward; nothing is hand-copied between platforms.

```
design/                      ← THE design rule (single source of truth)
  tokens/                    DTCG JSON: primitives + light/dark semantic themes
  icons/icons.json           Shared stroke icon set
docs/design/                 Written rules: principles, colour, type, layout, component contracts
        │
        ▼  tools/Slate.Tokens (C# generator, `slate-tokens build`)
        │
        ├─ packages/web/src/styles/generated/tokens.css   CSS custom properties (+ dark, auto, density)
        ├─ packages/web/src/icons/generated/icons.ts      Icon path data
        ├─ src/Slate.Core/Generated/*.g.cs                 SlateTokens + SlateIcons constants
        ├─ src/Slate.Wpf/Themes/Generated/*.xaml           Tokens / Theme.Light|Dark / Density.*
        ├─ src/Slate.Avalonia/Themes/Generated/*.axaml     Tokens (ThemeDictionaries) / Density.*
        └─ design/dist/tokens.resolved.json                Every token, resolved, with CSS/XAML names
```

## Packages

| Package | Kind | Built on | Notes |
|---|---|---|---|
| `@bytegrain/slate-web` | npm | Lit web components + CSS | Framework-agnostic. `slate.css` is usable without JS. |
| `Slate.Core` | NuGet, net10.0 | — | Tokens, icons, and the platform-independent engines: `SnackbarQueue`, `DialogStack`, breakpoints. No UI dependencies. |
| `Slate.Blazor` | NuGet, Razor class library | Slate.Core + the web CSS | Razor components that render the same markup/classes as the web package. |
| `Slate.Wpf` | NuGet, net10.0-windows | Slate.Core | Implicit styles for native controls + Slate controls (shell, snackbar host, dialog host). |
| `Slate.Avalonia` | NuGet | Slate.Core | ControlThemes/styles for native controls + Slate controls. |

### Why this split

- **Tokens are generated, never typed.** The generator validates references, theme parity, density parity and
  WCAG contrast (see `tests/Slate.Tokens.Tests/DesignRuleTests.cs`). A token change that breaks accessibility
  fails the build.
- **Behaviour lives in Slate.Core once.** Snackbar timing, queueing, de-duplication and pause rules, and dialog
  stacking/escape/backdrop rules, are implemented and tested once. Each .NET platform only renders state and
  forwards input. The web package mirrors the same rules in TypeScript and is tested against the same cases.
- **Blazor reuses the web CSS.** The web package's CSS is the reference rendering; Blazor emits identical
  class names, so a visual fix lands on both.
- **Generated files are committed.** Packages build without running the generator. `GeneratedOutputTests`
  fails if anything is stale.

## Theming model (all platforms)

- Themes: `light` (default) and `dark`; `System` follows the OS.
  - Web/Blazor: `data-sl-theme="light|dark|auto"` on any element (scoped).
  - WPF: swap `Theme.Light.xaml` ↔ `Theme.Dark.xaml`; consumers use `DynamicResource`.
  - Avalonia: `RequestedThemeVariant` (Light/Dark/Default) — ThemeDictionaries switch automatically.
- Density: `compact` (default) and `comfortable`, same mechanism (`data-sl-density`, `Density.*.xaml/.axaml`).
- Resource naming is identical across XAML platforms: `Sl.Color.*` (Color), `Sl.Brush.*` (brush),
  `Sl.Space.*`, `Sl.Thickness.*`, `Sl.Radius.*`, `Sl.CornerRadius.*`, `Sl.Typography.*`, `Sl.Shadow.*` (Avalonia
  BoxShadows) / `Sl.Effect.*` (WPF DropShadowEffect). CSS uses `--sl-…` kebab equivalents.

## Testing strategy

| Layer | Tooling | What is covered |
|---|---|---|
| Design source | xUnit (`Slate.Tokens.Tests`) | Parsing, references, cycles, parity, emitters, contrast rules, generated-files-up-to-date |
| Shared logic | xUnit + FakeTimeProvider (`Slate.Core.Tests`) | Snackbar queue/timers/pause/dedupe/overflow/concurrency, dialog stack, breakpoints |
| Web | Vitest (+ jsdom) | Component behaviour, a11y attributes, keyboard, snackbar/dialog services |
| Blazor | bUnit | Rendering, parameters, events, services |
| Avalonia | Avalonia.Headless | Real controls in a headless window: styles applied, theme switching, hosts |
| WPF | xUnit (STA), Windows only | Resource loading and controls; compiled on all OSes, executed on Windows |

## Repository layout

```
design/          tokens + icons (source of truth)
docs/            architecture and design rules
tools/           Slate.Tokens (generator library) + Slate.Tokens.Cli
src/             Slate.Core, Slate.Blazor, Slate.Wpf, Slate.Avalonia
packages/web     @bytegrain/slate-web (Lit) + its demo
demos/           Blazor, WPF and Avalonia demo apps
tests/           one test project per .NET project
```
