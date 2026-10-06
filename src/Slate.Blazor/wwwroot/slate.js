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
