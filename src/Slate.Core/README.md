# Slate.Core

The platform-independent foundation of the Slate Alloy design system for .NET 10. `Slate.Core` contains
shared tokens, icons, configuration types and behavior engines used by the Slate UI packages.

## Included

- `SnackbarQueue` and `DialogStack` for consistent notification and dialog behavior.
- Theme construction and shared defaults/configuration types.
- Responsive layout helpers and data-grid engines (sorting, filtering, grouping, selection, editing,
  virtualization, paging and export).
- Generated `SlateTokens` and `SlateIcons` constants.

Install a UI package (`Slate.Blazor`, `Slate.Wpf` or `Slate.Avalonia`) for rendered controls and styles.
Those packages reference `Slate.Core` automatically. The core package has no UI framework dependency.

Design tokens are generated from the repository's `design/` source; generated files are not intended for
manual editing. See the [architecture guide](../../docs/ARCHITECTURE.md) and [design rules](../../docs/design/README.md).
