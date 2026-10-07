#!/usr/bin/env node
/**
 * Scroll performance of <sl-data-grid> with 100k rows in a real (headless) browser.
 *
 *   node scripts/grid-perf.mjs [--url http://localhost:5199/] [--rows 100000] [--frames 240]
 *
 * Without --url it starts the demo dev server itself. The browser is Microsoft Edge (or Chrome) — override with
 * SLATE_BROWSER=/path/to/browser. It loads the demo with `?grid=perf`, then in the page:
 *   1. a smooth flick: scrollTop += 900px per animation frame,
 *   2. random jumps anywhere in the 100k rows (worst case: every row in the window is new),
 *   3. a horizontal sweep (pinned columns + column window),
 * and reports frame intervals (rAF to rAF) and the grid's own update cost (willUpdate → render → updated).
 * Uses only the DevTools protocol over Node's built-in WebSocket — no puppeteer dependency.
 */
import { spawn } from 'node:child_process';
import { existsSync, mkdtempSync, readFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const args = Object.fromEntries(process.argv.slice(2).join(' ').split('--').filter(Boolean).map((a) => a.trim().split(/\s+/)));
const rows = Number(args.rows ?? 100_000);
const frames = Number(args.frames ?? 240);
const root = join(dirname(fileURLToPath(import.meta.url)), '..');

const browsers = [
  process.env.SLATE_BROWSER,
  '/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge',
  '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome',
  'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',
  'C:/Program Files/Google/Chrome/Application/chrome.exe',
  '/usr/bin/microsoft-edge',
  '/usr/bin/google-chrome',
  '/usr/bin/chromium',
].filter(Boolean);
const browserPath = browsers.find((b) => existsSync(b));
if (!browserPath) {
  console.error('No Edge/Chrome found; set SLATE_BROWSER.');
  process.exit(2);
}

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const children = [];
let profile;
// Kill the browser (and our dev server), wait for them to exit, then remove the throwaway profile.
async function shutdown(code) {
  await Promise.all(children.map((c) => new Promise((r) => { if (c.exitCode !== null) r(); else { c.once('exit', r); c.kill(); } })));
  if (profile) { try { rmSync(profile, { recursive: true, force: true, maxRetries: 5, retryDelay: 100 }); } catch { /* best effort */ } }
  process.exit(code);
}
process.on('unhandledRejection', (e) => { console.error(e instanceof Error ? e.message : e); void shutdown(1); });

async function waitFor(fn, timeoutMs, what) {
  const end = Date.now() + timeoutMs;
  for (;;) {
    const v = await fn().catch(() => undefined);
    if (v) return v;
    if (Date.now() > end) throw new Error(`Timed out waiting for ${what}`);
    await sleep(100);
  }
}

// ---- Demo server ----
let baseUrl = args.url;
if (!baseUrl) {
  const port = 5198;
  const vite = spawn(process.execPath, [join(root, 'node_modules/vite/bin/vite.js'), '--config', 'vite.demo.config.ts', '--port', String(port), '--strictPort'], { cwd: root, stdio: 'ignore' });
  children.push(vite);
  baseUrl = `http://localhost:${port}/`;
  await waitFor(async () => (await fetch(baseUrl)).ok, 30_000, 'the demo server');
}

// ---- Browser + DevTools protocol ----
profile = mkdtempSync(join(tmpdir(), 'slate-grid-perf-'));
const browser = spawn(browserPath, [
  '--headless=new', '--remote-debugging-port=0', `--user-data-dir=${profile}`, '--window-size=1440,1000',
  '--no-first-run', '--no-default-browser-check', '--disable-extensions', 'about:blank',
], { stdio: 'ignore' });
children.push(browser);
const port = await waitFor(async () => readFileSync(join(profile, 'DevToolsActivePort'), 'utf8').split('\n')[0], 20_000, 'the browser');
const targets = await waitFor(async () => (await fetch(`http://127.0.0.1:${port}/json/list`)).json(), 10_000, 'targets');
const page = targets.find((t) => t.type === 'page');

const ws = new WebSocket(page.webSocketDebuggerUrl);
await new Promise((r, j) => { ws.onopen = r; ws.onerror = j; });
let nextId = 1;
const pending = new Map();
ws.onmessage = (m) => {
  const msg = JSON.parse(m.data);
  if (msg.id && pending.has(msg.id)) { pending.get(msg.id)(msg); pending.delete(msg.id); }
};
const send = (method, params = {}) => new Promise((resolve, reject) => {
  const id = nextId++;
  pending.set(id, (msg) => (msg.error ? reject(new Error(msg.error.message)) : resolve(msg.result)));
  ws.send(JSON.stringify({ id, method, params }));
});
const evaluate = async (expression) => {
  const r = await send('Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true });
  if (r.exceptionDetails) throw new Error(r.exceptionDetails.exception?.description ?? r.exceptionDetails.text);
  return r.result.value;
};

await send('Runtime.enable');
await send('Page.enable');
// Keep the page "visible and focused" so animation frames are not throttled like a background tab.
await send('Page.bringToFront');
await send('Emulation.setFocusEmulationEnabled', { enabled: true });
const url = new URL(baseUrl);
url.searchParams.set('grid', 'perf');
url.searchParams.set('grid-rows', String(rows));
url.hash = 'data-grid';
process.stderr.write(`Loading ${url.href} in ${browserPath.split('/').pop()}…\n`);
const t0 = Date.now();
await send('Page.navigate', { url: url.href });
await waitFor(() => evaluate('!!(window.__grid && window.__grid.view && window.__grid.view.viewRowCount > 0)'), 60_000, 'the grid');
const loadMs = Date.now() - t0;
process.stderr.write('Measuring…\n');

// ---- Measurement (runs in the page) ----
const result = await evaluate(`(async () => {
  const grid = window.__grid;
  grid.scrollIntoView();
  await grid.updateComplete;
  const viewport = grid.shadowRoot.querySelector('.sl-data-grid__viewport');
  const raf = () => new Promise((r) => requestAnimationFrame(r));
  const updates = [];
  const original = grid.performUpdate;
  grid.performUpdate = function () { const t = performance.now(); const out = original.call(this); updates.push(performance.now() - t); return out; };
  let longTasks = 0;
  try { new PerformanceObserver((l) => { longTasks += l.getEntries().length; }).observe({ type: 'longtask' }); } catch {}

  async function run(name, step, count) {
    updates.length = 0;
    const intervals = [];
    await raf();
    let last = performance.now();
    for (let i = 0; i < count; i++) {
      step(i);
      await raf();
      const now = performance.now();
      intervals.push(now - last);
      last = now;
    }
    await raf(); await raf();
    const stats = (xs) => {
      const s = [...xs].sort((a, b) => a - b);
      const q = (p) => s.length ? s[Math.min(s.length - 1, Math.floor(p * s.length))] : 0;
      return { mean: s.reduce((a, b) => a + b, 0) / Math.max(1, s.length), p50: q(0.5), p95: q(0.95), max: s[s.length - 1] ?? 0 };
    };
    const domRows = grid.shadowRoot.querySelectorAll('.sl-data-grid__row').length;
    return { name, frames: count, frame: stats(intervals), dropped: intervals.filter((x) => x > 20).length, update: stats(updates), updates: updates.length, domRows };
  }

  const max = () => viewport.scrollHeight - viewport.clientHeight;
  const out = [];
  out.push(await run('flick (900px/frame)', () => { viewport.scrollTop = Math.min(max(), viewport.scrollTop + 900); }, ${frames}));
  let seed = 1;
  const rnd = () => ((seed = (seed * 16807) % 2147483647) / 2147483647);
  out.push(await run('random jumps', () => { viewport.scrollTop = rnd() * max(); }, ${frames}));
  viewport.scrollTop = 0;
  out.push(await run('horizontal sweep', (i) => { viewport.scrollLeft = (i % 40) * 40; }, ${Math.min(frames, 120)}));
  grid.performUpdate = original;
  return { results: out, longTasks, rowCount: grid.view.viewRowCount, ariaRowCount: viewport.getAttribute('aria-rowcount'), ua: navigator.userAgent };
})()`);

const f = (n) => n.toFixed(1).padStart(6);
console.log(`sl-data-grid scroll performance — ${result.rowCount.toLocaleString()} rows (aria-rowcount ${result.ariaRowCount}), load ${loadMs} ms`);
console.log(`${result.ua.match(/(Edg|Chrome)\/[\d.]+/)?.[0] ?? 'browser'} headless, 1440×1000\n`);
console.log('scenario              frames   frame ms: mean    p50    p95    max  >20ms   update ms: mean    p95    max  DOM rows');
for (const r of result.results) {
  console.log(`${r.name.padEnd(22)}${String(r.frames).padStart(6)}            ${f(r.frame.mean)} ${f(r.frame.p50)} ${f(r.frame.p95)} ${f(r.frame.max)} ${String(r.dropped).padStart(6)}             ${f(r.update.mean)} ${f(r.update.p95)} ${f(r.update.max)} ${String(r.domRows).padStart(9)}`);
}
console.log(`\nlong tasks (>50 ms) during scrolling: ${result.longTasks}`);
ws.close();
await shutdown(0);
