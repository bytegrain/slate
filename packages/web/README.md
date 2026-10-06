# @slate/web

The **Alloy** design system as framework-agnostic web components (Lit) plus a class-based stylesheet.
Generated from the same design source as Slate.Blazor, Slate.Wpf and Slate.Avalonia.

```bash
npm install @slate/web
```

```js
import '@slate/web/slate.css';        // tokens, fonts, base + every component style
import { snackbar, dialog } from '@slate/web';   // registers all <sl-*> elements
```

```html
<body class="sl-root">
  <sl-provider theme="system" density="compact">
    <sl-app-shell fill-viewport>
      <sl-app-bar slot="app-bar" title="My app"></sl-app-bar>
      <sl-drawer slot="drawer"><nav class="sl-nav">…</nav></sl-drawer>
      <sl-container>
        <sl-button variant="solid" tone="accent" start-icon="plus">New project</sl-button>
      </sl-container>
    </sl-app-shell>
  </sl-provider>
</body>
```

`slate.css` works on its own for static markup — see [`docs/design/css-classes.md`](../../docs/design/css-classes.md).

## Elements

| Area | Elements |
|---|---|
| Root | `sl-provider` (theme, density, snackbar layer) |
| Layout | `sl-app-shell`, `sl-app-bar`, `sl-drawer`, `sl-container`, `sl-grid` + `sl-grid-item`, `sl-stack`, `sl-spacer`, `sl-divider`, `sl-card`, `sl-toolbar` |
| Inputs | `sl-button`, `sl-text-field`, `sl-checkbox`, `sl-switch`, `sl-radio-group` + `sl-radio` (all form-associated) |
| Display | `sl-text`, `sl-icon`, `sl-badge`, `sl-alert`, `sl-progress`, `sl-spinner`, `sl-kbd` |
| Systems | `sl-snackbar-host`, `sl-dialog` |

## Systems

```js
snackbar.show('Saved');
snackbar.success('Deployed', { title: 'slate-web', action: { label: 'View', onInvoke: open } });
snackbar.configure({ position: 'top-center' });

const ok = await dialog.confirm({ title: 'Delete?', message: 'This can’t be undone.', confirmText: 'Delete', destructive: true });
const result = await dialog.show({ title: 'Rename', content: form, actions: [{ label: 'Cancel', cancel: true }, { label: 'Save', value: 'x' }] });
```

The snackbar queue and dialog stack mirror `Slate.Core` rule-for-rule (max visible, FIFO queue, durations from tokens,
pause on hover/focus, duplicate suppression, top-of-stack Escape/backdrop handling). See `docs/design/systems.md`.

## Development

```bash
npm run dev     # demo at http://localhost:5173 (?theme=dark, ?density=comfortable, ?show=snackbars|dialog)
npm test        # vitest
npm run build   # dist/slate.js, dist/slate.css, dist/types, dist/fonts, dist-demo
```

Tokens and icons under `src/**/generated` come from `design/` — regenerate with
`dotnet run --project tools/Slate.Tokens.Cli -- build`, never edit them by hand.

## Configuring

Every component follows the canonical API in `design/api/components.json` (see `docs/design/configurability.md`):

```js
import { configureDefaults, createTheme } from '@slate/web';

// App-wide defaults: unset options on every component use these.
configureDefaults({ button: { size: 'small' }, field: { variant: 'filled' }, snackbar: { position: 'top-center' } });

// Custom theme from a brand colour (contrast guaranteed), softer radii, another font.
document.querySelector('sl-provider').themeOptions = { accent: '#FF5A1F', radiusScale: 1.5, fontFamily: 'Inter, sans-serif' };
```

Per-component styling: override `--sl-component-*` tokens in any scope (e.g. `--sl-component-button-radius: 999px`),
or use `::part(...)` on a single element.
