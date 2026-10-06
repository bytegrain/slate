// Port of tests/Slate.Core.Tests/SnackbarQueueTests.cs — the TS engine must behave identically.
import { beforeEach, describe, expect, it } from 'vitest';
import { FakeClock } from '../src/core/clock';
import { SnackbarQueue, type SnackbarClosedEvent, type SnackbarConfiguration, type SnackbarOptions } from '../src/core/snackbar-queue';
import { tokenNumber } from '../src/core/tokens';

let clock: FakeClock;
const queue = (config: Partial<SnackbarConfiguration> = {}) => new SnackbarQueue(config, clock);
const msg = (message: string, severity: SnackbarOptions['severity'] = 'normal'): SnackbarOptions => ({ message, severity });

beforeEach(() => {
  clock = new FakeClock();
});

describe('SnackbarQueue', () => {
  it('shows immediately while there is room', () => {
    const q = queue();
    const s = q.add('Saved');
    expect(s.state).toBe('visible');
    expect(q.visible).toEqual([s]);
    expect(q.queuedCount).toBe(0);
  });

  it('queues beyond max visible and promotes in order', () => {
    const q = queue({ maxVisible: 2 });
    const a = q.add('a');
    const b = q.add('b');
    const c = q.add('c');
    const d = q.add('d');
    expect(q.visible).toEqual([a, b]);
    expect(c.state).toBe('queued');
    expect(q.queuedCount).toBe(2);

    q.dismiss(a);
    expect(q.visible).toEqual([b, c]);
    expect(c.state).toBe('visible');
    expect(d.state).toBe('queued');
  });

  it('newest on top reverses display order', () => {
    const q = queue({ newestOnTop: true });
    const a = q.add('a');
    const b = q.add('b');
    expect(q.visible).toEqual([b, a]);
  });

  it('auto closes after the default duration from the tokens', () => {
    const q = queue();
    const s = q.add('Saved');
    const duration = tokenNumber('--sl-snackbar-duration-default');
    expect(duration).toBe(5000);

    clock.advance(duration - 1);
    expect(s.state).toBe('visible');
    clock.advance(1);
    expect(s.state).toBe('closed');
    expect(s.closeReason).toBe('timeout');
    expect(q.visible).toEqual([]);
  });

  it('errors linger longer than other severities', () => {
    const q = queue();
    const info = q.add(msg('i', 'info'));
    const error = q.add(msg('e', 'error'));
    expect(error.duration!).toBeGreaterThan(info.duration!);
    clock.advance(info.duration!);
    expect(info.state).toBe('closed');
    expect(error.state).toBe('visible');
  });

  it('snackbars with actions get at least the minimum action duration', () => {
    const q = queue();
    const s = q.add({ message: 'Deleted', duration: 1000, action: { label: 'Undo' } });
    expect(s.duration).toBe(q.configuration.minimumDurationWithAction);
  });

  it('explicit duration wins when there is no action', () => {
    const q = queue();
    const s = q.add({ message: 'Quick', duration: 2000 });
    clock.advance(2000);
    expect(s.state).toBe('closed');
  });

  it('require interaction never times out', () => {
    const q = queue();
    const s = q.add({ message: 'Connection lost', requireInteraction: true });
    expect(s.duration).toBeNull();
    clock.advance(3_600_000);
    expect(s.state).toBe('visible');
  });

  it('timeout promotes the next queued snackbar and starts its timer', () => {
    const q = queue({ maxVisible: 1 });
    const a = q.add('a');
    const b = q.add('b');
    clock.advance(a.duration!);
    expect(b.state).toBe('visible');
    clock.advance(b.duration!);
    expect(b.state).toBe('closed');
  });

  it('queued time does not count against duration', () => {
    const q = queue({ maxVisible: 1 });
    const a = q.add({ message: 'a', requireInteraction: true });
    const b = q.add('b');
    clock.advance(300_000);
    q.dismiss(a);
    expect(b.state).toBe('visible');
    expect(b.remaining).toBe(b.duration);
  });

  it('pause freezes and resume continues with remaining time', () => {
    const q = queue();
    const s = q.add({ message: 'x', duration: 5000 });
    clock.advance(3000);
    q.pause(s);
    expect(s.isPaused).toBe(true);
    expect(s.remaining).toBe(2000);

    clock.advance(600_000);
    expect(s.state).toBe('visible');

    q.resume(s);
    clock.advance(1999);
    expect(s.state).toBe('visible');
    clock.advance(1);
    expect(s.state).toBe('closed');
  });

  it('pause and resume are idempotent', () => {
    const q = queue();
    const s = q.add({ message: 'x', duration: 5000 });
    q.pause(s);
    q.pause(s);
    q.resume(s);
    q.resume(s);
    clock.advance(5000);
    expect(s.state).toBe('closed');
  });

  it('pause all and resume all cover every visible snackbar', () => {
    const q = queue();
    const a = q.add('a');
    const b = q.add('b');
    q.pauseAll();
    clock.advance(60_000);
    expect([a.state, b.state]).toEqual(['visible', 'visible']);
    q.resumeAll();
    clock.advance(60_000);
    expect([a.state, b.state]).toEqual(['closed', 'closed']);
  });

  it('duplicates return the existing snackbar', () => {
    const q = queue();
    const a = q.add('Saved');
    const b = q.add('Saved');
    expect(b).toBe(a);
    expect(q.visible).toHaveLength(1);
  });

  it('same message with different severity is not a duplicate', () => {
    const q = queue();
    q.add(msg('Sync', 'info'));
    q.add(msg('Sync', 'error'));
    expect(q.visible).toHaveLength(2);
  });

  it('duplicates allowed when disabled or after close', () => {
    const q = queue({ preventDuplicates: false });
    expect(q.add('x')).not.toBe(q.add('x'));

    const q2 = queue();
    const first = q2.add('x');
    q2.dismiss(first);
    expect(q2.add('x')).not.toBe(first);
  });

  it('custom key controls duplicate detection', () => {
    const q = queue();
    const a = q.add({ message: 'Uploading 1 file', key: 'upload' });
    const b = q.add({ message: 'Uploading 2 files', key: 'upload' });
    expect(b).toBe(a);
  });

  it('queue overflow drops the oldest queued', () => {
    const q = queue({ maxVisible: 1, maxQueued: 2 });
    const closed: SnackbarClosedEvent[] = [];
    q.onClosed((e) => closed.push(e));

    q.add('visible');
    const oldest = q.add('q1');
    q.add('q2');
    q.add('q3');

    expect(q.queuedCount).toBe(2);
    expect(oldest.state).toBe('closed');
    expect(closed).toHaveLength(1);
    expect(closed[0].reason).toBe('cleared');
  });

  it('dismissing a queued snackbar removes it without showing it', () => {
    const q = queue({ maxVisible: 1 });
    q.add('a');
    const b = q.add('b');
    expect(q.dismiss(b, 'user')).toBe(true);
    expect(q.queuedCount).toBe(0);
    expect(q.dismiss(b)).toBe(false);
  });

  it('closed fires once with the reason and changed fires after', () => {
    const q = queue();
    const events: string[] = [];
    q.onClosed((e) => events.push(`closed:${e.reason}`));
    q.onChanged(() => events.push('changed'));

    const s = q.add('x');
    events.length = 0;
    q.dismiss(s, 'user');
    q.dismiss(s, 'user');
    expect(events).toEqual(['closed:user', 'changed']);
  });

  it('invoking the action closes first then runs it once', async () => {
    const q = queue();
    let runs = 0;
    let stateDuringRun: string | undefined;
    const s = q.add({
      message: 'Deleted',
      action: {
        label: 'Undo',
        onInvoke: () => {
          runs++;
          stateDuringRun = s.state;
        },
      },
    });

    await q.invokeAction(s);
    await q.invokeAction(s);

    expect(runs).toBe(1);
    expect(stateDuringRun).toBe('closed');
    expect(s.closeReason).toBe('action');
  });

  it('clear closes visible and queued', () => {
    const q = queue({ maxVisible: 1 });
    const a = q.add('a');
    const b = q.add('b');
    q.clear();
    expect(q.visible).toEqual([]);
    expect(q.queuedCount).toBe(0);
    expect([a.closeReason, b.closeReason]).toEqual(['cleared', 'cleared']);

    clock.advance(60_000);
    expect(a.closeReason).toBe('cleared');
    expect(clock.pendingTimers).toBe(0);
  });

  it.each(['', '   '])('rejects empty messages (%j)', (message) => {
    expect(() => queue().add(message)).toThrow(TypeError);
  });

  it('rejects non-positive durations and invalid configuration', () => {
    expect(() => queue().add({ message: 'x', duration: 0 })).toThrow(RangeError);
    expect(() => queue({ maxVisible: 0 })).toThrow(RangeError);
  });

  it('disposed queue rejects new snackbars and stops timers', () => {
    const q = queue();
    const s = q.add('x');
    q.dispose();
    clock.advance(60_000);
    expect(s.state).toBe('visible');
    expect(() => q.add('y')).toThrow();
  });

  it('defaults come from the design tokens', () => {
    const c = queue().configuration;
    expect(c.maxVisible).toBe(3);
    expect(c.errorDuration).toBe(8000);
    expect(c.minimumDurationWithAction).toBe(8000);
    expect(c.position).toBe('bottom-right');
  });

  it('many adds and dismissals keep invariants', () => {
    const q = queue({ maxVisible: 3, maxQueued: 1000, preventDuplicates: false });
    const all = [];
    for (let i = 0; i < 400; i++) {
      const s = q.add(`m${i}`);
      all.push(s);
      if (i % 2 === 0) q.dismiss(s);
      if (i % 7 === 0) clock.advance(1000);
    }
    expect(q.visible.length).toBeLessThanOrEqual(3);
    expect(all.filter((s) => s.state !== 'closed')).toHaveLength(q.visible.length + q.queuedCount);
  });
});
