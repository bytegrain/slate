# Slate — instructions for agents

Slate is a cross-platform component library and design framework built on the **Alloy** design language,
shipped as four native packages (Web/Lit, Blazor, WPF, Avalonia) generated from one design source.
**Read `docs/HANDOFF.md` first** — it has the current state, decisions, known issues and next steps.

## Ground rules

- **The design source is the single source of truth.** Never hand-edit generated files:
  `packages/web/src/styles/generated/*`, `packages/web/src/icons/generated/*`, `src/Slate.Core/Generated/*`,
  `src/Slate.Wpf/Themes/Generated/*`, `src/Slate.Avalonia/Themes/Generated/*`, `design/dist/*`.
  Edit `design/tokens/**` or `design/icons/icons.json`, then run
  `dotnet run --project tools/Slate.Tokens.Cli -- build`. `GeneratedOutputTests` fails if outputs are stale.
- **The API contract is `design/api/components.json`.** Every platform has a conformance test that reads it.
  Adding an option = add it to the JSON, implement it on all four platforms (or list `platforms`), drive any
  visual change through a component token. XAML spellings that differ live in the JSON (`xamlType`, `xaml`,
  `wpf`, `avalonia`, `xamlConventions`) — never hard-code rename tables in tests.
- **Logic lives once.** Behaviour (snackbar queue, dialog stack, theme builder, grid engine, positioning,
  calendar, typeahead, tree, pagination, slider, avatar) is in `src/Slate.Core` with an identical TypeScript
  port in `packages/web/src/core`. Parity is proven by fixtures in `tests/fixtures/*.json` written by
  Slate.Core tests and asserted by Vitest. Change both sides together, then regenerate:
  `SLATE_UPDATE_FIXTURES=1 dotnet test tests/Slate.Core.Tests` (and `npm run sync:theme -w @bytegrain/slate-web` after
  token changes that affect themes).
- **Renderers contain no logic of their own** — they draw state from the engines and forward input.
- **Blazor renders the web package's markup.** Visuals come only from `packages/web/dist/slate.css`
  (copied into the RCL at build). The markup contract is `docs/design/css-classes.md`. Change web CSS/markup
  first, then Blazor.
- **Accessibility and contrast rules are executable** (`tests/Slate.Tokens.Tests/DesignRuleTests.cs`,
  ThemeBuilder tests). Don't weaken a test to make a colour pass — fix the colour.
- Commit per milestone with a descriptive body; end commit messages with the attribution line the harness gives you.
- Build only the projects you touch when other work runs in parallel; warnings are errors repo-wide.

## Commands

```bash
dotnet run --project tools/Slate.Tokens.Cli -- build          # regenerate platform outputs
dotnet run --project tools/Slate.Tokens.Cli -- build --check  # CI: fail if stale
dotnet test tests/Slate.Tokens.Tests                          # 116
dotnet test tests/Slate.Core.Tests                            # 590
dotnet test tests/Slate.Blazor.Tests                          # 390 (bUnit)
dotnet test tests/Slate.Avalonia.Tests                        # 516 (headless, xunit v3)
dotnet test tests/Slate.Wpf.ResourceTests                     # 160 (runs on macOS)
dotnet test tests/Slate.Wpf.Tests                             # Windows only (skipped elsewhere)
npm test            # @bytegrain/slate-web Vitest, 578
npm run build       # lib + dist/slate.css + demo
npm run dev         # web demo (Vite)
dotnet run --project demos/Slate.Blazor.Demo
dotnet run --project demos/Slate.Avalonia.Demo [-- --screenshot <dir>]
```

## Environment notes (macOS dev machine)

- WPF compiles here (`EnableWindowsTargeting`) but cannot run; its UI tests are Windows-only.
- GUI apps must run outside the Bash sandbox: `.claude/settings.local.json` (gitignored) allows
  `dotnet run --project demos/*`, `screencapture`, `pkill -f Slate.*.Demo`.
- `--screenshot <dir>` on the Avalonia demo captures only the demo window (by window id) for every page in
  light and dark. If it fails with Avalonia.Native RenderTimer error **-6661**, the display is asleep/locked —
  not a code bug. Never full-screen-capture the user's desktop.
- Headless Edge works for web/Blazor visual checks:
  `"/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge" --headless=new --virtual-time-budget=10000 --window-size=1440,1100 --screenshot=out.png <url>`.
  Blazor Server needs the remote-debugging port to drive interactions (prerender-only screenshots otherwise).
  Delete screenshots after reviewing.
