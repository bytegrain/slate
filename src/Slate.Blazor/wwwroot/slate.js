// Slate.Blazor interop. Loaded lazily (never during prerender). Keep this small: everything that can be
// done with markup + CSS is done there.

const focusable = 'a[href], button:not([disabled]), input:not([disabled]):not([type="hidden"]), select:not([disabled]), ' +
  'textarea:not([disabled]), [tabindex]:not([tabindex="-1"]), [contenteditable="true"]';

function visible(el) {
  return !el.closest('[inert]') && (el.offsetWidth > 0 || el.offsetHeight > 0 || el.getClientRects().length > 0);
}

/** Traps Tab inside `panel`, focuses the [autofocus] element / first focusable / the panel, and on release
 *  restores focus to what was focused before. */
export function trapFocus(panel) {
  const previous = document.activeElement;
  const items = () => [...panel.querySelectorAll(focusable)].filter(visible);

  const initial = panel.querySelector('[autofocus]') ?? items().find(el => !el.classList.contains('sl-dialog__close')) ?? panel;
  initial.focus({ preventScroll: true });

  const onKeyDown = (e) => {
    if (e.key !== 'Tab') return;
    const list = items();
    if (list.length === 0) { e.preventDefault(); panel.focus(); return; }
    const first = list[0], last = list[list.length - 1];
    if (e.shiftKey && (document.activeElement === first || document.activeElement === panel)) { e.preventDefault(); last.focus(); }
    else if (!e.shiftKey && document.activeElement === last) { e.preventDefault(); first.focus(); }
  };
  panel.addEventListener('keydown', onKeyDown);

  return {
    release(restore = true) {
      panel.removeEventListener('keydown', onKeyDown);
      if (restore && previous instanceof HTMLElement && previous.isConnected) previous.focus({ preventScroll: true });
    },
  };
}

/** Calls dotnet.invokeMethodAsync('OnMediaChanged', matches) whenever the query changes; returns a disposer. */
export function watchMedia(query, dotnet) {
  const mq = window.matchMedia(query);
  const handler = () => dotnet.invokeMethodAsync('OnMediaChanged', mq.matches);
  mq.addEventListener('change', handler);
  return {
    matches: mq.matches,
    dispose() { mq.removeEventListener('change', handler); },
  };
}

export function mediaMatches(query) {
  return window.matchMedia(query).matches;
}

export function focusElement(el) {
  el?.focus?.({ preventScroll: true });
}

/** `indeterminate` is a property only, so it needs script. */
export function setIndeterminate(el, value) {
  if (el) el.indeterminate = !!value;
}

/** Switches must not submit their form on Enter (some browsers do for checkboxes). */
export function preventEnter(el) {
  el?.addEventListener('keydown', (e) => { if (e.key === 'Enter') e.preventDefault(); });
}

// ---- Overlays (docs/design/css-classes.md#overlays) -------------------------------------------------------
// .NET owns the rules (Slate.Core positioning, the dismissal stack in OverlayManager); this file only measures,
// shows/hides panels in the top layer and reports raw Escape / pointer-down events.

const overlays = new Map(); // id -> { handle, anchor, panel, matchWidth, frame }
let overlayManager = null;
let overlayListening = false;

/** The element to measure for an anchor wrapper: wrappers are `display: contents`, so use their first child. */
function measurable(el) {
  if (!el) return null;
  if (getComputedStyle(el).display === 'contents') return el.firstElementChild ?? el;
  return el;
}

function rectOf(el) {
  const r = (measurable(el) ?? el).getBoundingClientRect();
  return { x: r.left, y: r.top, width: r.width, height: r.height };
}

function viewport() {
  const d = document.documentElement;
  return { x: 0, y: 0, width: d.clientWidth || window.innerWidth, height: d.clientHeight || window.innerHeight };
}

function onOverlayKeyDown(e) {
  if (e.key !== 'Escape' || overlays.size === 0 || !overlayManager) return;
  e.preventDefault();
  e.stopPropagation();
  overlayManager.invokeMethodAsync('OnEscape');
}

function onOverlayPointerDown(e) {
  if (overlays.size === 0 || !overlayManager) return;
  const path = e.composedPath();
  const containing = [];
  for (const [id, o] of overlays) {
    if ((o.anchor && path.includes(o.anchor)) || (o.anchor && path.includes(measurable(o.anchor))) || (o.panel && path.includes(o.panel))) containing.push(id);
  }
  overlayManager.invokeMethodAsync('OnPointerDown', containing);
}

function overlayListen(on) {
  if (on === overlayListening) return;
  overlayListening = on;
  const m = on ? 'addEventListener' : 'removeEventListener';
  document[m]('keydown', onOverlayKeyDown, true);
  document[m]('pointerdown', onOverlayPointerDown, true);
}

function schedule(id) {
  const o = overlays.get(id);
  if (!o) return;
  cancelAnimationFrame(o.frame);
  o.frame = requestAnimationFrame(() => overlayPosition(id));
}

async function overlayPosition(id) {
  const o = overlays.get(id);
  if (!o || !o.panel.isConnected) return;
  const anchor = o.anchor ? rectOf(o.anchor) : { x: 0, y: 0, width: 0, height: 0 };
  o.panel.style.maxHeight = '';
  if (o.matchWidth) o.panel.style.minWidth = `${anchor.width}px`;
  let p;
  try {
    p = await o.handle.invokeMethodAsync('Compute', anchor, o.panel.offsetWidth, o.panel.offsetHeight, viewport());
  } catch {
    return;
  }
  if (!overlays.has(id)) return;
  o.panel.style.left = `${p.left}px`;
  o.panel.style.top = `${p.top}px`;
  if (p.maxHeight != null) o.panel.style.maxHeight = `${p.maxHeight}px`;
  o.panel.dataset.side = p.side;
  o.panel.dataset.actualPlacement = p.placement;
  o.panel.style.setProperty('--_arrow', `${p.arrow}px`);
}

export async function overlayOpen(id, handle, manager, anchor, panel, matchWidth) {
  if (!panel) return;
  overlayManager = manager;
  let o = overlays.get(id);
  if (!o) {
    o = { handle, anchor, panel, matchWidth, frame: 0, onMove: () => schedule(id) };
    overlays.set(id, o);
    window.addEventListener('scroll', o.onMove, true);
    window.addEventListener('resize', o.onMove);
  } else {
    Object.assign(o, { anchor, panel, matchWidth });
  }
  overlayListen(true);
  panel.hidden = false;
  if (typeof panel.showPopover === 'function') {
    if (!panel.hasAttribute('popover')) panel.setAttribute('popover', 'manual');
    try { if (!panel.matches(':popover-open')) panel.showPopover(); } catch { /* not connected / already open */ }
  }
  await overlayPosition(id);
}

export function overlayReposition(id) {
  schedule(id);
}

export function overlayClose(id) {
  const o = overlays.get(id);
  if (!o) return;
  overlays.delete(id);
  window.removeEventListener('scroll', o.onMove, true);
  window.removeEventListener('resize', o.onMove);
  cancelAnimationFrame(o.frame);
  if (typeof o.panel.hidePopover === 'function' && o.panel.hasAttribute('popover')) {
    try { o.panel.hidePopover(); } catch { /* already hidden */ }
  }
  o.panel.hidden = true;
  if (overlays.size === 0) overlayListen(false);
}

// ---- Small DOM helpers used by wave-2 components ------------------------------------------------------------

/** Sets ARIA attributes on the anchor wrapper's first element (wrappers are display: contents). */
export function setAnchorAria(wrapper, attributes) {
  const el = wrapper?.firstElementChild;
  if (!el) return;
  for (const [name, value] of Object.entries(attributes ?? {})) {
    if (value === null || value === undefined) el.removeAttribute(name);
    else el.setAttribute(name, String(value));
  }
}

/** Focuses the wrapper's first element child (popover/menu anchors). */
export function focusFirstChild(wrapper) {
  wrapper?.firstElementChild?.focus?.({ preventScroll: true });
}

/** Focuses the first element matching `selector` inside `root` (roving tabindex targets, calendar days…). */
export function focusSelector(root, selector) {
  const el = root?.querySelector(selector);
  el?.focus?.({ preventScroll: true });
  return !!el;
}

/** Focuses an element by id (tree items, tabs, menu items). */
export function focusId(id) {
  document.getElementById(id)?.focus?.({ preventScroll: true });
}

/** Prevents the default action for the given keys on an element (arrows scrolling the page, Space clicking…). */
export function preventKeys(el, keys) {
  if (!el || el.__slPrevent) { if (el) el.__slPrevent.keys = new Set(keys); return; }
  const state = { keys: new Set(keys) };
  el.__slPrevent = state;
  el.addEventListener('keydown', (e) => {
    const key = e.key === ' ' ? 'Space' : e.key;
    if (state.keys.has(key) || state.keys.has(`${e.shiftKey ? 'Shift+' : ''}${key}`)) e.preventDefault();
  });
}

/** Tabs: moves the line indicator under the selected tab and toggles the overflow buttons. */
export function tabsMeasure(root) {
  if (!root) return;
  const list = root.querySelector('.sl-tabs__list');
  const indicator = root.querySelector('.sl-tabs__indicator');
  const selected = list?.querySelector('.sl-tab.is-selected');
  if (indicator && selected && list) {
    indicator.style.setProperty('--_x', `${selected.offsetLeft}px`);
    indicator.style.setProperty('--_y', `${selected.offsetTop}px`);
    indicator.style.setProperty('--_w', `${selected.offsetWidth}px`);
    indicator.style.setProperty('--_h', `${selected.offsetHeight}px`);
  }
  const overflow = root.querySelector('.sl-tabs__overflow');
  if (overflow && list && !root.classList.contains('sl-tabs--column')) overflow.hidden = list.scrollWidth <= list.clientWidth + 1;
  if (!root.__slTabsObserver && typeof ResizeObserver !== 'undefined' && list) {
    root.__slTabsObserver = new ResizeObserver(() => tabsMeasure(root));
    root.__slTabsObserver.observe(list);
  }
}

export function tabsScroll(root, direction) {
  const list = root?.querySelector('.sl-tabs__list');
  list?.scrollBy({ left: direction * list.clientWidth * 0.6, behavior: 'smooth' });
}

/** Scrolls the active option of a listbox into view. */
export function scrollActiveIntoView(root, selector) {
  root?.querySelector(selector)?.scrollIntoView?.({ block: 'nearest' });
}

/** Slider dragging: reports the pointer's fraction along the track (0..1) with a phase (down/move/up). */
export function sliderTrack(track, dotnet) {
  if (!track || track.__slSlider) return;
  const fraction = (e) => {
    const r = track.getBoundingClientRect();
    return r.width > 0 ? Math.min(1, Math.max(0, (e.clientX - r.left) / r.width)) : 0;
  };
  let dragging = false;
  const down = (e) => {
    if (e.button !== 0 || track.closest('.is-disabled')) return;
    dragging = true;
    track.setPointerCapture?.(e.pointerId);
    e.preventDefault();
    dotnet.invokeMethodAsync('OnTrackPointer', fraction(e), 'down');
  };
  const move = (e) => { if (dragging) dotnet.invokeMethodAsync('OnTrackPointer', fraction(e), 'move'); };
  const up = (e) => {
    if (!dragging) return;
    dragging = false;
    dotnet.invokeMethodAsync('OnTrackPointer', fraction(e), 'up');
  };
  track.addEventListener('pointerdown', down);
  track.addEventListener('pointermove', move);
  track.addEventListener('pointerup', up);
  track.addEventListener('pointercancel', up);
  track.__slSlider = { dispose() { track.removeEventListener('pointerdown', down); track.removeEventListener('pointermove', move); track.removeEventListener('pointerup', up); track.removeEventListener('pointercancel', up); delete track.__slSlider; } };
}

export function sliderRelease(track) {
  track?.__slSlider?.dispose();
}

// ---- Data grid (SlDataGrid) ------------------------------------------------------------------------------------
// The grid's logic lives in .NET; this only measures, reports scrolling when the rendered window no longer covers the
// viewport (rAF-throttled), tracks resize/reorder pointer drags, and blocks the browser defaults for grid keys.

const GRID_NAV_KEYS = new Set(['ArrowUp', 'ArrowDown', 'ArrowLeft', 'ArrowRight', 'Home', 'End', 'PageUp', 'PageDown', '+', '-', ' ', 'Enter', 'F2']);

export function gridInit(viewport, dotnet) {
  if (!viewport || viewport.__slGrid) return;
  const root = viewport.closest('.sl-data-grid') ?? viewport.parentElement;
  const s = { dotnet, band: null, pending: false, raf: 0, idle: 0, flags: '', measured: '', reported: '', resize: null, drag: null, noReorder: new Set(), disposed: false };
  viewport.__slGrid = s;
  const call = (method, ...args) => (s.disposed ? Promise.resolve(false) : dotnet.invokeMethodAsync(method, ...args).catch(() => false));

  s.measure = () => {
    const probe = root.querySelector('.sl-data-grid__probe');
    const head = root.querySelector('.sl-data-grid__probe--head');
    const header = viewport.querySelector('.sl-data-grid__header');
    const density = root.parentElement?.closest('[data-sl-density]')?.getAttribute('data-sl-density');
    const m = [probe?.offsetHeight || 0, header?.offsetHeight || head?.offsetHeight || 0, viewport.clientWidth, viewport.clientHeight, density === 'comfortable'];
    const key = m.join(':');
    if (key === s.measured) return;
    s.measured = key;
    call('OnGridMeasure', ...m);
  };

  const position = () => {
    const top = viewport.scrollTop, left = viewport.scrollLeft;
    return { top, left, scrolledX: left > 0, scrollableEnd: left + viewport.clientWidth < viewport.scrollWidth - 1 };
  };
  const outside = (p) => {
    const b = s.band;
    if (!b) return false;
    if (b.top !== null && p.top < b.top) return true;
    if (b.bottom !== null && p.top + b.body > b.bottom) return true;
    if (b.left !== null && p.left < b.left) return true;
    if (b.right !== null && p.left > b.right) return true;
    return false;
  };
  s.report = () => {
    s.raf = 0;
    if (s.pending) return;
    const p = position();
    const key = `${p.top}:${p.left}`;
    if (key === s.reported && `${p.scrolledX}${p.scrollableEnd}` === s.flags) return;
    s.reported = key;
    s.flags = `${p.scrolledX}${p.scrollableEnd}`;
    s.pending = true;
    // A render answers with gridSync (new band); the timeout only guards against a lost answer.
    clearTimeout(s.pendingTimer);
    s.pendingTimer = setTimeout(() => { s.pending = false; s.check(); }, 1000);
    call('OnGridScroll', p.top, p.left, p.scrolledX, p.scrollableEnd).then((willRender) => {
      if (willRender) return;
      s.pending = false;
      clearTimeout(s.pendingTimer);
      s.check();
    });
  };
  s.check = () => {
    const p = position();
    if (!s.raf && (outside(p) || `${p.scrolledX}${p.scrollableEnd}` !== s.flags)) s.raf = requestAnimationFrame(s.report);
  };
  s.onScroll = () => {
    const p = position();
    root.classList.toggle('is-scrolled-x', p.scrolledX);
    root.classList.toggle('is-scrollable-end', p.scrollableEnd);
    s.check();
    // A trailing report keeps .NET's scroll position exact for keyboard navigation once scrolling settles.
    clearTimeout(s.idle);
    s.idle = setTimeout(() => { if (!s.raf) s.raf = requestAnimationFrame(s.report); }, 150);
  };

  s.onKeyDown = (e) => {
    const t = e.target;
    const mod = e.ctrlKey || e.metaKey;
    if (t === viewport) {
      if (GRID_NAV_KEYS.has(e.key) && !(mod && (e.key === '+' || e.key === '-'))) e.preventDefault();
      else if (mod && (e.key === 'a' || e.key === 'A' || e.key === 'c' || e.key === 'C')) e.preventDefault();
    } else if (t?.classList?.contains('sl-data-grid__editor')) {
      if (e.key === 'Enter' || e.key === 'Tab' || e.key === 'Escape') e.preventDefault();
    } else if (t?.classList?.contains('sl-data-grid__sort') && e.altKey && (e.key === 'ArrowLeft' || e.key === 'ArrowRight')) {
      e.preventDefault();
    }
  };

  const frame = (fn) => { if (!s.dragRaf) s.dragRaf = requestAnimationFrame(() => { s.dragRaf = 0; fn(); }); };
  s.onPointerDown = (e) => {
    if (e.button !== 0) return;
    const handle = e.target.closest?.('.sl-data-grid__resize');
    if (handle && viewport.contains(handle)) {
      const field = handle.closest('[data-field]')?.dataset.field;
      if (!field) return;
      e.preventDefault();
      e.stopPropagation();
      s.resize = { field, x: e.clientX, dx: 0 };
      handle.setPointerCapture?.(e.pointerId);
      return;
    }
    const sort = e.target.closest?.('.sl-data-grid__sort');
    if (sort && viewport.contains(sort)) {
      const field = sort.closest('[data-field]')?.dataset.field;
      if (!field || s.noReorder.has(field) || sort.disabled) return;
      s.drag = { field, x: e.clientX, moved: false, over: null, after: false, bar: false, key: '' };
      sort.setPointerCapture?.(e.pointerId);
    }
  };
  s.onPointerMove = (e) => {
    if (s.resize) {
      s.resize.dx = e.clientX - s.resize.x;
      const { field, dx } = s.resize;
      frame(() => call('OnGridResize', field, dx, false));
      return;
    }
    const d = s.drag;
    if (!d) return;
    if (!d.moved && Math.abs(e.clientX - d.x) < 5) return;
    d.moved = true;
    const bar = root.querySelector('.sl-data-grid__group-bar')?.getBoundingClientRect();
    d.bar = !!bar && e.clientY >= bar.top && e.clientY <= bar.bottom && e.clientX >= bar.left && e.clientX <= bar.right;
    d.over = null;
    if (!d.bar) {
      for (const cell of viewport.querySelectorAll('.sl-data-grid__header-cell[data-field]')) {
        const r = cell.getBoundingClientRect();
        if (e.clientX >= r.left && e.clientX <= r.right) {
          d.over = cell.dataset.field;
          d.after = e.clientX > r.left + r.width / 2;
        }
      }
    }
    const key = `${d.over}|${d.after}|${d.bar}`;
    if (key !== d.key) {
      d.key = key;
      call('OnGridDrag', d.field, d.over, d.after, d.bar, false);
    }
  };
  const suppressClick = () => {
    const stop = (ev) => { ev.stopPropagation(); ev.preventDefault(); };
    viewport.addEventListener('click', stop, true);
    setTimeout(() => viewport.removeEventListener('click', stop, true));
  };
  s.onPointerUp = (e) => {
    if (s.resize) {
      const { field } = s.resize;
      const dx = e.type === 'pointercancel' ? 0 : s.resize.dx;
      s.resize = null;
      cancelAnimationFrame(s.dragRaf);
      s.dragRaf = 0;
      call('OnGridResize', field, dx, true);
      return;
    }
    const d = s.drag;
    s.drag = null;
    if (!d || !d.moved) return;
    suppressClick();
    const cancel = e.type === 'pointercancel';
    call('OnGridDrag', d.field, cancel ? null : d.over, d.after, !cancel && d.bar, true);
  };

  // Row/cell clicks (delegated: rows carry no per-cell handlers). Buttons, inputs and links inside cells handle themselves.
  const rowOf = (el) => {
    const ref = el.closest('.sl-data-grid__row')?.querySelector('[id]');
    const m = ref?.id.match(/-r(\d+)-c(-?\d+)$/);
    return m ? Number(m[1]) : null;
  };
  s.onClick = (e) => {
    const t = e.target;
    if (!(t instanceof Element) || !t.closest('.sl-data-grid__rows')) return;
    const dbl = e.type === 'dblclick';
    // Commands on the plain markup of rows: checkbox, expanders, the closed row menu.
    const check = t.closest('.sl-data-grid__check input');
    const expander = t.closest('.sl-data-grid__expander');
    const action = t.closest('.sl-data-grid__row-action');
    if (check || expander || (action && action.getAttribute('aria-expanded') !== 'true')) {
      if (check) e.preventDefault(); // the checked state comes back from .NET
      const row = rowOf(t);
      if (row === null || dbl) return;
      const command = check ? 'check' : expander ? (expander.closest('.sl-data-grid__cell--select') ? 'detail' : 'toggle') : 'actions';
      call('OnGridRowCommand', row, command, e.shiftKey);
      return;
    }
    if (t.closest('button, a, input, select, textarea, label, .sl-menu, .sl-data-grid__row--detail')) return;
    const row = rowOf(t);
    if (row === null) return;
    const cell = t.closest('.sl-data-grid__cell[id]');
    const cm = cell?.id.match(/-c(-?\d+)$/);
    const column = cell?.classList.contains('sl-data-grid__cell--wide') ? 0 : cm ? Number(cm[1]) : -1;
    call('OnGridCellClick', row, column, e.ctrlKey || e.metaKey, e.shiftKey, dbl);
  };

  viewport.addEventListener('scroll', s.onScroll, { passive: true });
  viewport.addEventListener('click', s.onClick);
  viewport.addEventListener('dblclick', s.onClick);
  viewport.addEventListener('keydown', s.onKeyDown, true);
  viewport.addEventListener('pointerdown', s.onPointerDown, true);
  viewport.addEventListener('pointermove', s.onPointerMove);
  viewport.addEventListener('pointerup', s.onPointerUp);
  viewport.addEventListener('pointercancel', s.onPointerUp);
  if (typeof ResizeObserver !== 'undefined') {
    s.observer = new ResizeObserver(() => s.measure());
    s.observer.observe(viewport);
  }
  s.measure();
}

/** After each grid render: the scroll band the rendered window covers, scroll/focus requests, non-reorderable fields. */
export function gridSync(viewport, o) {
  const s = viewport?.__slGrid;
  if (!s) return;
  s.band = { top: o.top ?? null, bottom: o.bottom ?? null, body: o.body ?? 0, left: o.left ?? null, right: o.right ?? null };
  if (o.noReorder) s.noReorder = new Set(o.noReorder);
  if (o.scrollTop !== null && o.scrollTop !== undefined) viewport.scrollTop = o.scrollTop;
  if (o.scrollLeft !== null && o.scrollLeft !== undefined) viewport.scrollLeft = o.scrollLeft;
  if (o.focus === 'editor') {
    const editor = viewport.querySelector('.sl-data-grid__editor');
    if (editor && document.activeElement !== editor) {
      editor.focus({ preventScroll: true });
      if (editor.type === 'text') { const n = editor.value.length; editor.setSelectionRange?.(n, n); }
    }
  } else if (o.focus === 'viewport' && document.activeElement !== viewport) {
    viewport.focus({ preventScroll: true });
  }
  if (o.measure) s.measure();
  s.pending = false;
  clearTimeout(s.pendingTimer);
  s.check();
}

export function gridDispose(viewport) {
  const s = viewport?.__slGrid;
  if (!s) return;
  s.disposed = true;
  cancelAnimationFrame(s.raf);
  clearTimeout(s.idle);
  s.observer?.disconnect();
  viewport.removeEventListener('scroll', s.onScroll);
  viewport.removeEventListener('click', s.onClick);
  viewport.removeEventListener('dblclick', s.onClick);
  viewport.removeEventListener('keydown', s.onKeyDown, true);
  viewport.removeEventListener('pointerdown', s.onPointerDown, true);
  viewport.removeEventListener('pointermove', s.onPointerMove);
  viewport.removeEventListener('pointerup', s.onPointerUp);
  viewport.removeEventListener('pointercancel', s.onPointerUp);
  delete viewport.__slGrid;
}

/** Writes text to the clipboard (falls back to a hidden textarea where the async API is unavailable). */
export async function copyText(text) {
  try {
    await navigator.clipboard.writeText(text);
    return true;
  } catch {
    const area = document.createElement('textarea');
    area.value = text;
    area.style.cssText = 'position:fixed;opacity:0;pointer-events:none';
    document.body.appendChild(area);
    area.select();
    let ok = false;
    try { ok = document.execCommand('copy'); } catch { /* unavailable */ }
    area.remove();
    return ok;
  }
}

/** Downloads text as a file. */
export function downloadText(fileName, mime, text) {
  const a = document.createElement('a');
  a.href = URL.createObjectURL(new Blob([text], { type: mime }));
  a.download = fileName;
  document.body.appendChild(a);
  a.click();
  a.remove();
  setTimeout(() => URL.revokeObjectURL(a.href), 1000);
}
