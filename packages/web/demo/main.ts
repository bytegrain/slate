import '../src/styles/index.css';
import './demo.css';
import {
  configureDefaults,
  createTheme,
  dialog,
  icons,
  snackbar,
  themeToCss,
  tones,
  type SlDialog,
  type SlNavItem,
  type SlProvider,
  type SlProgress,
  type SlateThemeOptions,
  type SnackbarPosition,
} from '../src/index';
import componentsJson from '../../../design/api/components.json?raw';

const provider = document.getElementById('provider') as SlProvider;

// ---- Theme & density ----
const themeButtons = [...document.querySelectorAll<HTMLButtonElement>('[data-theme]')];
for (const button of themeButtons) {
  button.addEventListener('click', () => {
    provider.theme = button.dataset.theme as SlProvider['theme'];
    for (const b of themeButtons) {
      const on = b === button;
      b.setAttribute('aria-pressed', String(on));
      b.classList.toggle('is-pressed', on);
    }
    renderSwatches();
  });
}
// ?theme=dark|light|system and ?density=comfortable preselect (handy for screenshots and links).
const params = new URLSearchParams(location.search);
(themeButtons.find((b) => b.dataset.theme === params.get('theme')) ?? themeButtons[0]).click();
if (params.get('density') === 'comfortable') {
  provider.density = 'comfortable';
  (document.getElementById('density') as HTMLElement & { checked: boolean }).checked = true;
}

document.getElementById('density')!.addEventListener('change', (e) => {
  provider.density = (e.target as HTMLInputElement & { checked: boolean }).checked ? 'comfortable' : 'compact';
});

// ---- Swatches (read live token values so they follow the theme) ----
const swatchTokens: Array<[string, string]> = [
  ['Canvas', '--sl-color-background-canvas'],
  ['Surface', '--sl-color-background-surface'],
  ['Subtle', '--sl-color-background-subtle'],
  ['Border', '--sl-color-border-strong'],
  ['Text', '--sl-color-text-primary'],
  ['Accent', '--sl-color-accent-default'],
  ['Focus', '--sl-color-focus-ring'],
  ['Success', '--sl-color-status-success-fg'],
  ['Warning', '--sl-color-status-warning-fg'],
  ['Danger', '--sl-color-status-danger-fg'],
  ['Info', '--sl-color-status-info-fg'],
  ['Inverse', '--sl-color-inverse-background'],
];

function renderSwatches(): void {
  const host = document.getElementById('swatches')!;
  requestAnimationFrame(() => {
    const style = getComputedStyle(provider);
    host.replaceChildren(
      ...swatchTokens.map(([name, token]) => {
        const item = document.createElement('div');
        item.className = 'demo-swatch';
        item.innerHTML = `<div class="demo-swatch__chip" style="background: var(${token})"></div><b>${name}</b><span>${style.getPropertyValue(token).trim()}</span>`;
        return item;
      }),
    );
  });
}
renderSwatches();

// ---- Nav highlighting ----
const navItems = [...document.querySelectorAll<SlNavItem>('sl-nav-item')];
for (const item of navItems) {
  item.addEventListener('click', () => {
    for (const i of navItems) i.active = i === item;
  });
}

// ---- Button matrix: every variant × tone ----
const matrix = document.getElementById('button-matrix')!;
const variants = ['outlined', 'solid', 'soft', 'ghost', 'link'];
matrix.style.setProperty('--_cols', String(tones.length));
matrix.append(document.createElement('span'));
for (const tone of tones) {
  const head = document.createElement('span');
  head.className = 'demo-matrix__head';
  head.textContent = tone;
  matrix.append(head);
}
for (const variant of variants) {
  const head = document.createElement('span');
  head.className = 'demo-matrix__head';
  head.textContent = variant;
  matrix.append(head);
  for (const tone of tones) {
    const b = document.createElement('sl-button');
    b.setAttribute('variant', variant);
    b.setAttribute('tone', tone);
    b.setAttribute('size', 'small');
    b.textContent = tone === 'accent' && variant === 'solid' ? 'Primary' : 'Button';
    matrix.append(b);
  }
}

// ---- Form ----
const form = document.getElementById('demo-form') as HTMLFormElement;
form.addEventListener('submit', (e) => {
  e.preventDefault();
  const data = new FormData(form);
  snackbar.success(`Created ${data.get('project') || 'project'}`, { title: 'Project created' });
});

// ---- Progress animation ----
const progress = document.getElementById('progress') as SlProgress;
const progressLabel = document.getElementById('progress-label')!;
let value = 64;
setInterval(() => {
  value = value >= 100 ? 8 : value + 4;
  progress.value = value;
  progressLabel.textContent = `${value}%`;
}, 900);

// ---- Interactive card ----
document.getElementById('interactive-card')!.addEventListener('click', () => snackbar.info('Card clicked'));

// ---- Snackbars ----
let deleted = 0;
const snackActions: Record<string, () => void> = {
  normal: () => snackbar.show('Copied to clipboard'),
  info: () => snackbar.info('A new version of Slate is available.'),
  success: () => snackbar.success('slate-web is live in 3 regions.', { title: 'Deployed' }),
  warning: () => snackbar.warning('Two components are missing dark tokens.'),
  error: () => snackbar.error('Slate.Wpf failed to compile ResourceDictionary.xaml.', { title: 'Build failed' }),
  action: () => {
    const n = ++deleted;
    snackbar.show({
      message: `Deleted ${n} file${n === 1 ? '' : 's'}`,
      key: `deleted-${n}`,
      action: { label: 'Undo', onInvoke: () => void snackbar.success('Restored') },
    });
  },
  sticky: () => snackbar.warning('Connection lost. Changes will sync when you reconnect.', { requireInteraction: true, key: 'offline' }),
  burst: () => {
    for (let i = 1; i <= 6; i++) snackbar.show({ message: `Uploaded asset_${String(i).padStart(2, '0')}.png`, key: `burst-${Date.now()}-${i}` });
  },
  duplicate: () => {
    for (let i = 0; i < 3; i++) snackbar.show('Saved (shown once — duplicates are suppressed)');
  },
  clear: () => snackbar.clear(),
};
for (const button of document.querySelectorAll<HTMLElement>('[data-snack]')) {
  button.addEventListener('click', () => snackActions[button.dataset.snack!]());
}

document.getElementById('snack-position')!.addEventListener('change', (e) => {
  provider.snackbarPosition = (e.currentTarget as HTMLElement & { value: string }).value as SnackbarPosition;
});

// ---- Theming ----
const presets: Array<[string, string]> = [
  ['Alloy teal', ''],
  ['Cobalt', '#2F4FD8'],
  ['Violet', '#5B3DF5'],
  ['Signal orange', '#FF5A1F'],
  ['Forest', '#1F7A4C'],
  ['Amber', '#FFB020'],
  ['Crimson', '#E5231B'],
  ['Graphite', '#12161C'],
];
const themeState: SlateThemeOptions & { base?: 'light' | 'dark' } = {};
const accentField = document.getElementById('accent-hex') as HTMLElement & { value: string; error?: string };
const radius = document.getElementById('radius-scale') as HTMLInputElement;
const font = document.getElementById('font-family') as HTMLSelectElement;
const baseGroup = document.getElementById('theme-base') as HTMLElement & { value: string };
const themeCss = document.getElementById('theme-css')!;

function applyThemeState(): void {
  const options: SlateThemeOptions = {
    accent: themeState.accent || undefined,
    radiusScale: themeState.radiusScale,
    fontFamily: themeState.fontFamily || undefined,
    base: themeState.base,
  };
  const custom = !!(options.accent || options.fontFamily || (options.radiusScale ?? 1) !== 1 || options.base);
  try {
    provider.themeOptions = custom ? options : null;
    accentField.error = undefined;
    const theme = createTheme({ base: options.base ?? provider.resolvedTheme, ...options });
    themeCss.textContent = custom ? themeToCss({ ...theme, values: Object.fromEntries(theme.changedPaths.map((p) => [p, theme.values[p]])) }, 'sl-provider') : '/* Alloy defaults — pick a colour */';
  } catch (err) {
    accentField.error = (err as Error).message;
  }
  renderSwatches();
}

const presetHost = document.getElementById('accent-presets')!;
for (const [name, hex] of presets) {
  const b = document.createElement('button');
  b.type = 'button';
  b.className = 'demo-preset';
  b.title = name;
  b.setAttribute('aria-label', name);
  b.style.background = hex || 'var(--sl-palette-teal-700)';
  b.addEventListener('click', () => {
    themeState.accent = hex;
    accentField.value = hex;
    applyThemeState();
  });
  presetHost.append(b);
}
accentField.addEventListener('sl-value-changed', () => {
  const v = accentField.value.trim();
  if (v === '' || /^#?[0-9a-fA-F]{6}$/.test(v)) {
    themeState.accent = v && !v.startsWith('#') ? `#${v}` : v;
    applyThemeState();
  }
});
radius.addEventListener('input', () => {
  themeState.radiusScale = Number(radius.value);
  document.getElementById('radius-value')!.textContent = `${Number(radius.value).toFixed(2)}×`;
  applyThemeState();
});
font.addEventListener('change', () => {
  themeState.fontFamily = font.value;
  applyThemeState();
});
baseGroup.addEventListener('change', () => {
  themeState.base = (baseGroup.value || undefined) as 'light' | 'dark' | undefined;
  applyThemeState();
});
document.getElementById('theme-reset')!.addEventListener('click', () => {
  for (const k of Object.keys(themeState)) delete (themeState as Record<string, unknown>)[k];
  accentField.value = '';
  radius.value = '1';
  document.getElementById('radius-value')!.textContent = '1.0×';
  font.value = '';
  baseGroup.value = '';
  applyThemeState();
});
document.getElementById('theme-copy')!.addEventListener('click', () => {
  void navigator.clipboard?.writeText(themeCss.textContent ?? '');
  snackbar.success('Theme CSS copied');
});

// ---- Playground: controls generated from design/api/components.json ----
interface OptionSpec { kind: string; type?: string; default?: unknown; platforms?: string[] }
const api = JSON.parse(componentsJson) as { enums: Record<string, string[]>; components: Record<string, { web: string; options: Record<string, OptionSpec> }> };
const iconNames = Object.keys(icons);
const samples: Record<string, { markup: string; skip?: string[] }> = {
  Button: { markup: '<sl-button>Deploy</sl-button>', skip: ['Href', 'Type', 'Pressed'] },
  TextField: { markup: '<sl-text-field label="Project name" helper-text="Lowercase, numbers and dashes." value="slate-web"></sl-text-field>', skip: ['Value', 'Rows'] },
  Card: { markup: '<sl-card title="Deployments" subtitle="Last 7 days"><sl-text tone="secondary">Card body content.</sl-text><div slot="footer"><sl-button size="small">View all</sl-button></div></sl-card>' },
  Alert: { markup: '<sl-alert title="Deployed to production">slate-web is live in 3 regions.</sl-alert>' },
  Badge: { markup: '<sl-badge>Ready</sl-badge>' },
};
const kebabCase = (n: string) => n.replace(/([a-z0-9])([A-Z])/g, '$1-$2').toLowerCase();
const propName = (component: string, option: string) =>
  component === 'TextField' && (option === 'Prefix' || option === 'Suffix') ? `${option.toLowerCase()}Text` : option[0].toLowerCase() + option.slice(1);

const playgrounds = document.getElementById('playgrounds')!;
for (const [name, sample] of Object.entries(samples)) {
  const spec = api.components[name];
  const card = document.createElement('sl-card');
  card.setAttribute('title', `<${spec.web}>`);
  card.setAttribute('subtitle', name);
  const layout = document.createElement('div');
  layout.className = 'demo-playground';
  const stage = document.createElement('div');
  stage.className = 'demo-playground__stage';
  stage.innerHTML = sample.markup;
  const target = stage.firstElementChild as HTMLElement & Record<string, unknown>;
  const code = document.createElement('pre');
  code.className = 'demo-code demo-code--inline';
  const controls = document.createElement('div');
  controls.className = 'demo-playground__controls';

  const showCode = () => {
    const clone = target.cloneNode(true) as HTMLElement;
    code.textContent = clone.outerHTML.replace(/ (class|style)="[^"]*"/g, '');
  };

  for (const [option, o] of Object.entries(spec.options)) {
    if ((o.platforms && !o.platforms.includes('web')) || sample.skip?.includes(option)) continue;
    if (o.kind !== 'param' && o.kind !== 'bool') continue;
    const prop = propName(name, option);
    const attr = prop === 'prefixText' ? 'prefix' : prop === 'suffixText' ? 'suffix' : kebabCase(option);
    const values = o.type && (api.enums[o.type] ?? (o.type.includes('|') ? o.type.split('|') : null));
    let control: HTMLElement;
    if (o.kind === 'bool') {
      const sw = document.createElement('sl-switch') as HTMLElement & { checked: boolean };
      sw.setAttribute('label', option);
      sw.setAttribute('size', 'small');
      sw.checked = target.hasAttribute(attr);
      sw.addEventListener('change', () => {
        target.toggleAttribute(attr, sw.checked);
        showCode();
      });
      control = sw;
    } else if (values || o.type === 'icon') {
      const wrap = document.createElement('label');
      wrap.className = 'demo-control';
      wrap.innerHTML = `<span>${option}</span>`;
      const select = document.createElement('select');
      select.className = 'demo-select';
      for (const v of ['', ...(values ?? iconNames)]) select.append(new Option(v === '' ? '(unset)' : v, v));
      select.value = target.getAttribute(attr) ?? '';
      select.addEventListener('change', () => {
        if (select.value) target.setAttribute(attr, select.value);
        else target.removeAttribute(attr);
        if (!select.value) (target as Record<string, unknown>)[prop] = undefined;
        showCode();
      });
      wrap.append(select);
      control = wrap;
    } else if (o.type === 'string') {
      const field = document.createElement('sl-text-field') as HTMLElement & { value: string };
      field.setAttribute('label', option);
      field.setAttribute('size', 'small');
      field.value = (target[prop] as string | undefined) ?? '';
      field.addEventListener('sl-value-changed', () => {
        target[prop] = field.value;
        showCode();
      });
      control = field;
    } else {
      continue;
    }
    controls.append(control);
  }
  layout.append(stage, controls);
  card.append(layout, code);
  playgrounds.append(card);
  showCode();
}

// ---- App-wide defaults ----
for (const select of document.querySelectorAll<HTMLSelectElement>('[data-default]')) {
  select.addEventListener('change', () => {
    const [group, key] = select.dataset.default!.split('.');
    configureDefaults({ [group]: { [key]: select.value } } as Parameters<typeof configureDefaults>[0]);
  });
}

// ---- Dialogs ----
/** A rename form that validates before accepting. */
function renameDialog(): { content: (d: SlDialog) => Node; submit: (d: SlDialog) => void } {
  let field: (HTMLElement & { value: string; error?: string }) | undefined;
  const submit = (d: SlDialog) => {
    if (!field || !/^[A-Za-z0-9.]+$/.test(field.value)) {
      if (field) field.error = 'Use letters, numbers and dots only.';
      return;
    }
    d.accept(field.value);
  };
  const content = (d: SlDialog) => {
    const wrap = document.createElement('div');
    wrap.innerHTML = `<sl-text-field label="Package name" value="Slate.Wpf" required helper-text="Letters, numbers and dots." autofocus></sl-text-field>`;
    field = wrap.firstElementChild as HTMLElement & { value: string; error?: string };
    field.addEventListener('input', () => (field!.error = undefined));
    field.addEventListener('keydown', (e) => e.key === 'Enter' && submit(d));
    return wrap;
  };
  return { content, submit };
}

const dialogActions: Record<string, () => Promise<void>> = {
  form: async () => {
    const rename = renameDialog();
    const r = await dialog.show({
      title: 'Rename package',
      description: 'Renaming updates every reference in the solution.',
      icon: 'pencil',
      maxWidth: 'xs',
      closeOnBackdropClick: false,
      content: rename.content,
      actions: [
        { label: 'Cancel', cancel: true },
        { label: 'Rename', onClick: rename.submit },
      ],
    });
    if (!r.canceled) snackbar.success(`Renamed to ${String(r.data)}`);
  },
  confirm: async () => {
    const ok = await dialog.confirm({ title: 'Publish 0.1.4?', message: 'This pushes all four packages to npm and NuGet.', confirmText: 'Publish' });
    snackbar.show(ok ? 'Publishing…' : 'Publish cancelled', ok ? 'info' : 'normal');
  },
  destructive: async () => {
    const ok = await dialog.confirm({
      title: 'Delete Slate.Wpf?',
      message: 'This removes the package and its 14 versions from the feed. This can’t be undone.',
      confirmText: 'Delete package',
      destructive: true,
    });
    if (ok) snackbar.show({ message: 'Slate.Wpf deleted', action: { label: 'Undo', onInvoke: () => void snackbar.success('Restored Slate.Wpf') } });
  },
  alert: async () => {
    await dialog.alert({ title: 'Tokens rebuilt', message: 'All 11 generated files are up to date.', severity: 'success' });
  },
  stacked: async () => {
    await dialog.show({
      title: 'Project settings',
      description: 'Dialogs stack: only the top one responds to Escape.',
      maxWidth: 'md',
      content: (d) => {
        const b = document.createElement('sl-button');
        b.textContent = 'Open a second dialog';
        b.addEventListener('click', async () => {
          const ok = await dialog.confirm({ title: 'Discard changes?', message: 'You have unsaved edits.', confirmText: 'Discard', destructive: true });
          if (ok) d.close();
        });
        return b;
      },
      actions: [{ label: 'Close', cancel: true }],
    });
  },
  declarative: async () => {
    const el = document.getElementById('declarative') as SlDialog;
    const r = await el.show();
    if (!r.canceled) snackbar.success('Invites sent');
  },
};

const declarative = document.getElementById('declarative') as SlDialog;
declarative.querySelector('[data-close]')!.addEventListener('click', () => declarative.close());
declarative.querySelector('[data-accept]')!.addEventListener('click', () => declarative.accept());

for (const button of document.querySelectorAll<HTMLElement>('[data-dialog]')) {
  button.addEventListener('click', () => void dialogActions[button.dataset.dialog!]());
}

// ?show=snackbars|dialog opens examples on load (used for screenshots).
if (params.get('show') === 'snackbars') {
  snackActions.success();
  snackActions.action();
  snackActions.error();
}
if (params.get('show') === 'dialog') void dialogActions.destructive();
if (params.get('show') === 'form') void dialogActions.form();

// ?accent=FF5A1F&radius=1.5 preselects a custom theme (handy for screenshots and links).
if (params.get('accent') || params.get('radius')) {
  if (params.get('accent')) {
    themeState.accent = `#${params.get('accent')!.replace(/^#/, '')}`;
    accentField.value = themeState.accent;
  }
  if (params.get('radius')) {
    themeState.radiusScale = Number(params.get('radius'));
    radius.value = params.get('radius')!;
    document.getElementById('radius-value')!.textContent = `${themeState.radiusScale.toFixed(2)}×`;
  }
  applyThemeState();
}
