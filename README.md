# Slate

**Slate** is a cross-platform component library and design framework built on the **Alloy** design
language — precise, calm and built for professional software. One design source, four native packages:

| Platform | Package | Status |
|---|---|---|
| Web (any framework) | `@slate/web` — Lit web components + CSS | ✅ preview |
| Blazor | `Slate.Blazor` | ✅ preview |
| WPF | `Slate.Wpf` | ✅ preview — compiles everywhere, UI unverified until run on Windows |
| Avalonia | `Slate.Avalonia` | ✅ preview |
| Shared .NET core | `Slate.Core` | ✅ |

Current state, decisions and next steps: [`docs/HANDOFF.md`](docs/HANDOFF.md). Agent instructions: [`CLAUDE.md`](CLAUDE.md).

## The design source

`design/` is the single source of truth: DTCG design tokens (light + dark themes, compact + comfortable
density) and a shared icon set. A generator turns it into CSS variables, WPF and Avalonia resource
dictionaries and C# constants, and the build fails if a change breaks accessibility (WCAG contrast,
target sizes) or theme parity.

- Design rules: [`docs/design`](docs/design/README.md)
- Architecture: [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md)

## Working on Slate

```bash
# Regenerate platform outputs after editing design/tokens or design/icons
dotnet run --project tools/Slate.Tokens.Cli -- build

# Check generated files are current (CI)
dotnet run --project tools/Slate.Tokens.Cli -- build --check

# Run all .NET tests
dotnet test Slate.slnx
```

Requirements: .NET 10 SDK, Node 22+. WPF projects compile on any OS but run (and run their tests) on Windows only.
