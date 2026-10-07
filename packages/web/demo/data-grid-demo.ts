/**
 * Data grid demo: a 100k-row deployments grid with every feature switched on, a server-mode audit log with
 * simulated latency, and a tree of repository file sizes. `?grid-rows=1000` changes the dataset size,
 * `?grid=perf` exposes the main grid as `window.__grid` for scripts/grid-perf.mjs.
 */
import { html } from 'lit';
import { InMemoryGridDataSource, snackbar, type DataGridColumn, type SlDataGrid } from '../src/index';

// ---- Deterministic data ----
function rng(seed: number): () => number {
  let s = seed >>> 0;
  return () => {
    s = (s + 0x6d2b79f5) >>> 0;
    let t = s;
    t = Math.imul(t ^ (t >>> 15), t | 1);
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}
const pick = <T>(r: () => number, list: readonly T[]): T => list[Math.floor(r() * list.length)]!;

interface Deployment {
  id: string;
  service: string;
  region: string;
  env: string;
  status: string;
  owner: string;
  commit: string;
  progress: number;
  duration: number;
  errors: number;
  cost: number;
  started: Date;
  canary: boolean;
  latency: number[];
}

const SERVICES = ['api-gateway', 'billing', 'search', 'auth', 'ingest', 'notifications', 'web', 'reports', 'media', 'scheduler', 'ledger', 'graph'];
const REGIONS = ['eu-west-1', 'eu-central-1', 'us-east-1', 'us-west-2', 'ap-southeast-2', 'ap-northeast-1'];
const ENVS = ['production', 'staging', 'preview'];
const STATUSES = ['Running', 'Succeeded', 'Queued', 'Failed', 'Cancelled'];
const OWNERS = ['Ada Lovelace', 'Grace Hopper', 'Alan Turing', 'Katherine Johnson', 'Linus Torvalds', 'Margaret Hamilton', 'Dennis Ritchie', 'Barbara Liskov', 'Ken Thompson', 'Frances Allen'];
const STATUS_WEIGHTS = [0.08, 0.72, 0.06, 0.09, 0.05];

function statusFor(r: number): string {
  let acc = 0;
  for (let i = 0; i < STATUSES.length; i++) {
    acc += STATUS_WEIGHTS[i]!;
    if (r < acc) return STATUSES[i]!;
  }
  return 'Succeeded';
}

function makeDeployments(count: number): Deployment[] {
  const r = rng(42);
  const now = Date.UTC(2026, 9, 6, 12, 0, 0);
  const out: Deployment[] = new Array(count);
  for (let i = 0; i < count; i++) {
    const status = statusFor(r());
    const progress = status === 'Succeeded' ? 100 : status === 'Queued' ? 0 : status === 'Running' ? Math.round(5 + r() * 90) : Math.round(r() * 80);
    const latency = Array.from({ length: 12 }, () => Math.round(40 + r() * 160));
    out[i] = {
      id: `DEP-${String(count - i).padStart(6, '0')}`,
      service: pick(r, SERVICES),
      region: pick(r, REGIONS),
      env: pick(r, ENVS),
      status,
      owner: pick(r, OWNERS),
      commit: Math.floor(r() * 0xfffffff).toString(16).padStart(7, '0'),
      progress,
      duration: Math.round(30 + r() * 900),
      errors: status === 'Failed' ? Math.round(1 + r() * 40) : r() < 0.1 ? Math.round(r() * 3) : 0,
      cost: Math.round(r() * 4000) / 100,
      started: new Date(now - Math.floor(r() * 90 * 24 * 3600 * 1000)),
      canary: r() < 0.2,
      latency,
    };
  }
  return out;
}

const statusTones = { Running: 'accent', Succeeded: 'success', Queued: 'neutral', Failed: 'danger', Cancelled: 'warning' } as const;
const envTones = { production: 'danger', staging: 'warning', preview: 'info' } as const;

function deploymentColumns(onRedeploy: (d: Deployment) => void): DataGridColumn<Deployment>[] {
  return [
    { field: 'id', title: 'Deployment', width: 156, pinned: 'start', type: 'text', reorderable: false, aggregate: 'count' },
    { field: 'service', title: 'Service', width: 150, headerGroup: 'Target', editable: true, validate: (v) => (String(v ?? '').trim() ? null : 'Service is required') },
    { field: 'region', title: 'Region', width: 140, headerGroup: 'Target', type: 'enum', enumOrder: REGIONS, editable: true },
    { field: 'env', title: 'Environment', width: 150, headerGroup: 'Target', type: 'enum', enumOrder: ENVS, enumTones: envTones },
    { field: 'status', title: 'Status', width: 128, type: 'enum', enumOrder: STATUSES, enumTones: statusTones, editable: true, description: 'Current pipeline state' },
    {
      field: 'owner', title: 'Owner', width: 180, editable: true,
      cell: ({ text }) => html`<span class="sl-data-grid__media"><sl-avatar size="small" name=${text}></sl-avatar>${text}</span>`,
    },
    { field: 'progress', title: 'Progress', width: 160, type: 'progress', aggregate: 'avg', format: '0' },
    { field: 'latency', title: 'p95 latency', width: 132, type: 'sparkline', sortable: false, filterable: false, searchable: false },
    { field: 'duration', title: 'Duration (s)', width: 124, type: 'number', format: '#,##0', aggregate: 'sum', headerGroup: 'Metrics', editable: true, validate: (v) => (typeof v === 'number' && v < 0 ? 'Must be positive' : null) },
    { field: 'errors', title: 'Errors', width: 96, type: 'number', aggregate: 'sum', headerGroup: 'Metrics' },
    { field: 'cost', title: 'Cost ($)', width: 112, type: 'number', format: '#,##0.00', aggregate: 'sum', headerGroup: 'Metrics', editable: true },
    { field: 'started', title: 'Started', width: 170, type: 'date', format: 'yyyy-MM-dd HH:mm', aggregate: 'max' },
    { field: 'commit', title: 'Commit', width: 104, searchable: true },
    { field: 'canary', title: 'Canary', width: 92, type: 'boolean', align: 'center', editable: true },
    {
      field: 'actions', title: '', width: 52, type: 'actions', pinned: 'end', sortable: false, filterable: false, resizable: false, searchable: false, reorderable: false, hideable: false,
      actions: [
        { label: 'Redeploy', icon: 'refresh', run: onRedeploy, disabled: (d) => d.status === 'Running' },
        { label: 'Copy ID', icon: 'copy', run: (d) => { void navigator.clipboard?.writeText(d.id); snackbar.show(`Copied ${d.id}`); } },
        { label: 'View logs', icon: 'external-link', run: (d) => snackbar.info(`Opening logs for ${d.id}`) },
        { label: '', separator: true },
        { label: 'Archive', icon: 'trash', tone: 'danger', run: (d) => snackbar.warning(`${d.id} archived`) },
      ],
    },
  ];
}

function deploymentDetail(d: Deployment) {
  return html`<dl class="sl-data-grid__fields">
    <div><dt>Service</dt><dd>${d.service} · ${d.region}</dd></div>
    <div><dt>Commit</dt><dd>${d.commit}</dd></div>
    <div><dt>Owner</dt><dd>${d.owner}</dd></div>
    <div><dt>Duration</dt><dd>${d.duration.toLocaleString()} s</dd></div>
    <div><dt>Errors</dt><dd>${d.errors}</dd></div>
    <div><dt>Started</dt><dd>${d.started.toISOString().replace('T', ' ').slice(0, 16)} UTC</dd></div>
  </dl>`;
}

// ---- Server mode: an audit log behind a "remote" data source ----
interface AuditEvent { seq: number; at: Date; actor: string; action: string; resource: string; ip: string; severity: string }

function makeAudit(count: number): AuditEvent[] {
  const r = rng(7);
  const start = Date.UTC(2026, 9, 6, 12);
  const actions = ['login', 'logout', 'deploy', 'rollback', 'invite', 'rotate-key', 'update-policy', 'delete'];
  return Array.from({ length: count }, (_, i) => ({
    seq: count - i,
    at: new Date(start - i * 37_000),
    actor: pick(r, OWNERS),
    action: pick(r, actions),
    resource: `${pick(r, SERVICES)}/${pick(r, ENVS)}`,
    ip: `10.${Math.floor(r() * 255)}.${Math.floor(r() * 255)}.${Math.floor(r() * 255)}`,
    severity: r() < 0.06 ? 'High' : r() < 0.25 ? 'Medium' : 'Low',
  }));
}

const auditColumns: DataGridColumn<AuditEvent>[] = [
  { field: 'seq', title: '#', width: 90, type: 'number', format: '0' },
  { field: 'at', title: 'Time', width: 184, type: 'date', format: 'yyyy-MM-dd HH:mm:ss' },
  { field: 'actor', title: 'Actor', flex: 1, minWidth: 130 },
  { field: 'action', title: 'Action', width: 130, type: 'enum' },
  { field: 'resource', title: 'Resource', flex: 1, minWidth: 150 },
  { field: 'severity', title: 'Severity', width: 110, type: 'enum', enumOrder: ['Low', 'Medium', 'High'], enumTones: { Low: 'neutral', Medium: 'warning', High: 'danger' } },
];

// ---- Tree data: repository sizes ----
interface FileNode { path: string; name: string; kind: 'folder' | 'file'; size: number; files: number; modified: Date; children?: FileNode[] }

function makeTree(): FileNode[] {
  const r = rng(3);
  const day = (n: number) => new Date(Date.UTC(2026, 9, 6) - n * 86_400_000);
  const file = (name: string, kb: number): FileNode => ({ path: '', name, kind: 'file', size: Math.round(kb * 1024 * (0.6 + r() * 0.8)), files: 1, modified: day(Math.floor(r() * 60)) });
  const folder = (name: string, children: FileNode[]): FileNode => ({
    path: '', name, kind: 'folder', children,
    size: children.reduce((s, c) => s + c.size, 0),
    files: children.reduce((s, c) => s + c.files, 0),
    modified: new Date(Math.max(...children.map((c) => c.modified.getTime()))),
  });
  const withPaths = (nodes: FileNode[], parent = ''): FileNode[] => {
    for (const n of nodes) {
      n.path = `${parent}/${n.name}`;
      if (n.children) withPaths(n.children, n.path);
    }
    return nodes;
  };
  return withPaths([
    folder('src', [
      folder('Slate.Core', [folder('Data', [file('DataPipeline.cs', 38), file('GridState.cs', 22), file('SelectionModel.cs', 11), file('EditSession.cs', 9)]), file('Tokens.cs', 64), file('Positioning.cs', 18)]),
      folder('Slate.Blazor', [file('SlDataGrid.razor', 41), file('SlButton.razor', 4), file('SlDialog.razor', 9)]),
      folder('Slate.Wpf', [file('DataGrid.xaml', 52), file('Theme.xaml', 120)]),
    ]),
    folder('packages', [
      folder('web', [
        folder('src', [folder('components', [file('data-grid.ts', 74), file('data-grid-cells.ts', 21), file('select.ts', 26)]), folder('core', [folder('grid', [file('pipeline.ts', 34), file('layout.ts', 9), file('state.ts', 15)])]), folder('styles', [file('datagrid.css', 18), file('tokens.css', 96)])]),
        file('package.json', 2),
      ]),
    ]),
    folder('design', [folder('tokens', [file('color.json', 44), file('typography.json', 12), file('icons.json', 31)]), file('api.json', 58)]),
    folder('docs', [file('data-grid.md', 14), file('css-classes.md', 36)]),
    file('README.md', 7),
  ]);
}

const treeColumns: DataGridColumn<FileNode>[] = [
  {
    field: 'name', title: 'Name', flex: 1, minWidth: 180,
    cell: ({ item }) => html`<span class="sl-data-grid__media"><sl-icon name=${item.kind === 'folder' ? 'folder' : 'file'}></sl-icon>${item.name}</span>`,
  },
  { field: 'size', title: 'Size', width: 100, type: 'number', aggregate: 'sum', formatter: (v) => formatBytes(Number(v)) },
  { field: 'files', title: 'Files', width: 72, type: 'number', aggregate: 'sum' },
  { field: 'modified', title: 'Modified', width: 112, type: 'date', format: 'yyyy-MM-dd' },
];

function formatBytes(n: number): string {
  if (!Number.isFinite(n)) return '';
  if (n < 1024) return `${n} B`;
  if (n < 1024 * 1024) return `${(n / 1024).toFixed(1)} KB`;
  return `${(n / 1024 / 1024).toFixed(2)} MB`;
}

// ---- Wiring ----
export function setupDataGridDemo(params: URLSearchParams): void {
  const grid = document.getElementById('demo-grid') as SlDataGrid | null;
  if (!grid) return;

  const sizes = [1_000, 10_000, 100_000];
  let rows = Number(params.get('grid-rows')) || 100_000;
  let data: Deployment[] = [];
  const perf = document.getElementById('grid-perf')!;

  const load = (count: number) => {
    const t0 = performance.now();
    data = makeDeployments(count);
    const t1 = performance.now();
    grid.items = data;
    void grid.updateComplete.then(() => {
      perf.textContent = `${count.toLocaleString()} rows · generated ${Math.round(t1 - t0)} ms · first render ${Math.round(performance.now() - t1)} ms`;
    });
  };

  grid.rowKey = (d: Deployment) => d.id;
  grid.columns = deploymentColumns((d: Deployment) => {
    grid.upsert([{ ...d, status: 'Running', progress: 3, started: new Date() }]);
    snackbar.success(`Redeploying ${d.id}`);
  });
  grid.rowDetail = deploymentDetail;
  grid.rowTone = (d: Deployment) => (d.status === 'Failed' && d.env === 'production' ? 'danger' : null);
  load(rows);

  const size = document.getElementById('grid-size') as HTMLElement & { items: unknown; value: string };
  size.items = sizes.map((n) => ({ value: String(n), label: n >= 1000 ? `${n / 1000}k` : String(n) }));
  size.value = String(rows);
  size.addEventListener('change', () => {
    rows = Number(size.value);
    load(rows);
  });

  // Live updates: a handful of running deployments tick forward every second.
  let live: ReturnType<typeof setInterval> | undefined;
  const tickLive = () => {
    const r = Math.random;
    const changed: Deployment[] = [];
    for (let n = 0; n < 6; n++) {
      const i = Math.floor(r() * Math.min(data.length, 60));
      const d = data[i]!;
      const progress = Math.min(100, d.progress + Math.round(5 + r() * 20));
      data[i] = { ...d, progress, status: progress >= 100 ? 'Succeeded' : 'Running', latency: [...d.latency.slice(1), Math.round(40 + r() * 160)] };
      changed.push(data[i]!);
    }
    grid.upsert(changed);
  };

  for (const sw of document.querySelectorAll<HTMLElement & { checked: boolean }>('#grid-toggles [data-toggle]')) {
    sw.addEventListener('change', () => {
      const on = sw.checked;
      switch (sw.dataset.toggle) {
        case 'striped': grid.striped = on; break;
        case 'bordered': grid.bordered = on; break;
        case 'showFooter': grid.showFooter = on; break;
        case 'batch': grid.editMode = on ? 'batch' : 'cell'; break;
        case 'live':
          clearInterval(live);
          live = on ? setInterval(tickLive, 1000) : undefined;
          break;
      }
    });
  }

  document.getElementById('grid-group')!.addEventListener('click', () => {
    grid.groupBy = grid.groupBy.includes('status') ? [] : ['status'];
  });
  document.getElementById('grid-reset')!.addEventListener('click', () => {
    grid.state = { ...grid.state, columns: [], sorts: [], filters: [], quickFilter: '', groupBy: [], toggledGroups: [], expandedDetails: [] };
  });

  grid.addEventListener('sl-row-activated', (e) => snackbar.show(`Opened ${(e as CustomEvent<{ item: Deployment }>).detail.item.id}`));
  grid.addEventListener('sl-cell-edit-committed', (e) => {
    const { field, newValue } = (e as CustomEvent<{ field: string; newValue: unknown }>).detail;
    if (grid.editMode === 'cell') snackbar.success(`Saved ${field} = ${String(newValue)}`);
  });

  // Server mode.
  const server = document.getElementById('demo-grid-server') as SlDataGrid;
  server.columns = auditColumns;
  server.rowKey = (e: AuditEvent) => e.seq;
  server.dataSource = new InMemoryGridDataSource(makeAudit(25_000), auditColumns, 400);
  const paging = document.getElementById('server-paging') as HTMLElement & { items: unknown; value: string };
  paging.items = [{ value: 'none', label: 'Infinite scroll' }, { value: 'pages', label: 'Pages' }];
  paging.value = 'none';
  paging.addEventListener('change', () => { server.pagination = paging.value as 'none' | 'pages'; });

  // Tree data.
  const tree = document.getElementById('demo-grid-tree') as SlDataGrid;
  tree.columns = treeColumns;
  tree.rowKey = (n: FileNode) => n.path;
  tree.childrenSelector = (n: FileNode) => n.children;
  tree.items = makeTree();

  if (params.get('grid') === 'perf') (window as unknown as { __grid: SlDataGrid }).__grid = grid;
}
