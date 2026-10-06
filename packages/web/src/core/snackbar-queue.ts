import { systemClock, type Clock } from './clock';
import { tokenNumber } from './tokens';

/** Mirrors Slate.Severity. */
export type Severity = 'normal' | 'info' | 'success' | 'warning' | 'error';

export type SnackbarPosition = 'top-left' | 'top-center' | 'top-right' | 'bottom-left' | 'bottom-center' | 'bottom-right';

export type SnackbarState = 'queued' | 'visible' | 'closed';

export type SnackbarCloseReason = 'timeout' | 'user' | 'action' | 'programmatic' | 'cleared';

export interface SnackbarAction {
  label: string;
  onInvoke?: () => void | Promise<void>;
}

export interface SnackbarOptions {
  message: string;
  /** Optional bold first line. */
  title?: string;
  severity?: Severity;
  /** Milliseconds. Omit to use the configured default for the severity/action. */
  duration?: number;
  /** Stays until dismissed. */
  requireInteraction?: boolean;
  action?: SnackbarAction;
  showCloseButton?: boolean;
  /** Identity for duplicate suppression. Defaults to severity + title + message. */
  key?: string;
}

export interface SnackbarConfiguration {
  position: SnackbarPosition;
  maxVisible: number;
  /** Queued snackbars beyond this are dropped, oldest first. */
  maxQueued: number;
  defaultDuration: number;
  errorDuration: number;
  minimumDurationWithAction: number;
  preventDuplicates: boolean;
  newestOnTop: boolean;
}

/** Defaults from the snackbar.* design tokens — identical to Slate.Core's SnackbarConfiguration. */
export function defaultSnackbarConfiguration(): SnackbarConfiguration {
  return {
    position: 'bottom-right',
    maxVisible: tokenNumber('--sl-snackbar-max-visible'),
    maxQueued: 20,
    defaultDuration: tokenNumber('--sl-snackbar-duration-default'),
    errorDuration: tokenNumber('--sl-snackbar-duration-error'),
    minimumDurationWithAction: tokenNumber('--sl-snackbar-duration-with-action'),
    preventDuplicates: true,
    newestOnTop: false,
  };
}

/** A live snackbar. Hosts render SnackbarQueue.visible and call back into the queue. */
export interface Snackbar {
  readonly id: number;
  readonly options: Readonly<SnackbarOptions>;
  readonly severity: Severity;
  readonly message: string;
  /** Effective auto-close time in ms, or null when it waits for the user. */
  readonly duration: number | null;
  readonly createdAt: number;
  readonly state: SnackbarState;
  readonly closeReason: SnackbarCloseReason | null;
  readonly isPaused: boolean;
  /** Ms left before auto-close (frozen while paused); null for sticky snackbars or once closed. */
  readonly remaining: number | null;
}

interface MutableSnackbar extends Snackbar {
  state: SnackbarState;
  closeReason: SnackbarCloseReason | null;
  isPaused: boolean;
  remaining: number | null;
  timer: unknown;
  timerStartedAt: number | null;
  key: string;
}

export interface SnackbarClosedEvent {
  snackbar: Snackbar;
  reason: SnackbarCloseReason;
}

type Listener<T> = (value: T) => void;

/**
 * The snackbar engine: ordering, queueing, duplicate suppression, auto-close timers and pause.
 * Behaviour matches Slate.Core's SnackbarQueue case-for-case (see test/snackbar-queue.test.ts).
 */
export class SnackbarQueue {
  readonly configuration: SnackbarConfiguration;
  private readonly clock: Clock;
  private readonly visibleList: MutableSnackbar[] = [];
  private readonly queued: MutableSnackbar[] = [];
  private readonly changedListeners = new Set<Listener<void>>();
  private readonly closedListeners = new Set<Listener<SnackbarClosedEvent>>();
  private nextId = 0;
  private disposed = false;

  constructor(configuration: Partial<SnackbarConfiguration> = {}, clock: Clock = systemClock) {
    this.configuration = { ...defaultSnackbarConfiguration(), ...configuration };
    const c = this.configuration;
    if (c.maxVisible < 1) throw new RangeError('At least one snackbar must be visible.');
    if (c.maxQueued < 0) throw new RangeError('maxQueued cannot be negative.');
    if (c.defaultDuration <= 0 || c.errorDuration <= 0) {
      throw new RangeError('Durations must be positive; use requireInteraction for sticky snackbars.');
    }
    this.clock = clock;
  }

  /** Snackbars to render, in display order. */
  get visible(): readonly Snackbar[] {
    return this.configuration.newestOnTop ? [...this.visibleList].reverse() : [...this.visibleList];
  }

  get queuedCount(): number {
    return this.queued.length;
  }

  /** Subscribe to any change. Returns an unsubscribe function. */
  onChanged(listener: Listener<void>): () => void {
    this.changedListeners.add(listener);
    return () => this.changedListeners.delete(listener);
  }

  /** Subscribe to closes (once per snackbar, with the reason). */
  onClosed(listener: Listener<SnackbarClosedEvent>): () => void {
    this.closedListeners.add(listener);
    return () => this.closedListeners.delete(listener);
  }

  add(options: SnackbarOptions | string, severity: Severity = 'normal'): Snackbar {
    const o: SnackbarOptions = typeof options === 'string' ? { message: options, severity } : options;
    if (this.disposed) throw new Error('The snackbar queue has been disposed.');
    if (!o.message || !o.message.trim()) throw new TypeError('A snackbar needs a message.');
    if (o.duration !== undefined && !(o.duration > 0)) {
      throw new RangeError('Duration must be positive; use requireInteraction for sticky snackbars.');
    }

    const key = o.key ?? `${o.severity ?? 'normal'}|${o.title ?? ''}|${o.message}`;
    if (this.configuration.preventDuplicates) {
      const existing = [...this.visibleList, ...this.queued].find((s) => s.key === key);
      if (existing) return existing;
    }

    const snackbar: MutableSnackbar = {
      id: ++this.nextId,
      options: Object.freeze({ ...o }),
      severity: o.severity ?? 'normal',
      message: o.message,
      duration: this.effectiveDuration(o),
      createdAt: this.clock.now(),
      state: 'queued',
      closeReason: null,
      isPaused: false,
      remaining: null,
      timer: null,
      timerStartedAt: null,
      key,
    };

    const closed: SnackbarClosedEvent[] = [];
    if (this.visibleList.length < this.configuration.maxVisible) {
      this.show(snackbar);
    } else {
      this.queued.push(snackbar);
      while (this.queued.length > this.configuration.maxQueued) {
        const dropped = this.queued.shift()!;
        this.markClosed(dropped, 'cleared');
        closed.push({ snackbar: dropped, reason: 'cleared' });
      }
    }

    this.raise(closed);
    return snackbar;
  }

  /** Closes a visible snackbar (promoting the next queued one) or removes a queued one. False if already closed. */
  dismiss(snackbar: Snackbar, reason: SnackbarCloseReason = 'programmatic'): boolean {
    const s = snackbar as MutableSnackbar;
    if (s.state === 'closed') return false;

    if (s.state === 'queued') {
      this.queued.splice(this.queued.indexOf(s), 1);
    } else {
      this.stopTimer(s);
      this.visibleList.splice(this.visibleList.indexOf(s), 1);
      const next = this.queued.shift();
      if (next && !this.disposed) this.show(next);
    }

    this.markClosed(s, reason);
    this.raise([{ snackbar: s, reason }]);
    return true;
  }

  /** Closes the snackbar first (reason 'action'), then runs its action exactly once. */
  async invokeAction(snackbar: Snackbar): Promise<void> {
    const action = snackbar.options.action;
    if (!action || snackbar.state !== 'visible') return;
    if (!this.dismiss(snackbar, 'action')) return;
    await action.onInvoke?.();
  }

  /** Freezes the countdown (hover / focus). */
  pause(snackbar: Snackbar): void {
    const s = snackbar as MutableSnackbar;
    if (s.state !== 'visible' || s.isPaused) return;
    s.isPaused = true;
    if (s.timer !== null) {
      s.remaining = this.remainingNow(s);
      this.stopTimer(s);
    }
    this.emitChanged();
  }

  /** Restarts the countdown with the time left when paused. */
  resume(snackbar: Snackbar): void {
    const s = snackbar as MutableSnackbar;
    if (s.state !== 'visible' || !s.isPaused) return;
    s.isPaused = false;
    if (s.remaining !== null) this.startTimer(s, s.remaining);
    this.emitChanged();
  }

  pauseAll(): void {
    for (const s of this.visible) this.pause(s);
  }

  resumeAll(): void {
    for (const s of this.visible) this.resume(s);
  }

  /** Closes everything, visible and queued. */
  clear(): void {
    const all = [...this.queued, ...this.visibleList];
    this.queued.length = 0;
    for (const s of this.visibleList) this.stopTimer(s);
    this.visibleList.length = 0;
    for (const s of all) this.markClosed(s, 'cleared');
    if (all.length > 0) this.raise(all.map((snackbar) => ({ snackbar, reason: 'cleared' as const })));
  }

  dispose(): void {
    if (this.disposed) return;
    this.disposed = true;
    for (const s of this.visibleList) this.stopTimer(s);
  }

  /** Effective duration: null when sticky; errors use errorDuration; actions get at least the minimum. */
  effectiveDuration(o: SnackbarOptions): number | null {
    if (o.requireInteraction) return null;
    let duration = o.duration ?? (o.severity === 'error' ? this.configuration.errorDuration : this.configuration.defaultDuration);
    if (o.action && duration < this.configuration.minimumDurationWithAction) duration = this.configuration.minimumDurationWithAction;
    return duration;
  }

  private show(s: MutableSnackbar): void {
    s.state = 'visible';
    this.visibleList.push(s);
    if (s.duration !== null) this.startTimer(s, s.duration);
  }

  private markClosed(s: MutableSnackbar, reason: SnackbarCloseReason): void {
    s.state = 'closed';
    s.closeReason = reason;
    s.remaining = null;
  }

  private startTimer(s: MutableSnackbar, due: number): void {
    s.timerStartedAt = this.clock.now();
    s.remaining = due;
    s.timer = this.clock.setTimeout(() => {
      s.timer = null;
      this.dismiss(s, 'timeout');
    }, due);
  }

  private stopTimer(s: MutableSnackbar): void {
    if (s.timer !== null) this.clock.clearTimeout(s.timer);
    s.timer = null;
    s.timerStartedAt = null;
  }

  private remainingNow(s: MutableSnackbar): number {
    if (s.remaining === null || s.timerStartedAt === null) return 0;
    return Math.max(0, s.remaining - (this.clock.now() - s.timerStartedAt));
  }

  private raise(closed: SnackbarClosedEvent[]): void {
    for (const e of closed) for (const l of this.closedListeners) l(e);
    this.emitChanged();
  }

  private emitChanged(): void {
    for (const l of this.changedListeners) l();
  }
}
