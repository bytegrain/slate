import { describe, expect, it } from 'vitest';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { positionAtPoint, positionPopover, type OverlayRect, type PopoverPlacement } from '../src/core/overlay/positioning';
import {
  addMonths,
  buildMonth,
  dayOfWeek,
  firstDayOfWeek,
  formatDate,
  isoWeek,
  navigateCalendar,
  parseDate,
  pickRange,
  resolvePreset,
  shortPattern,
  type CalendarDay,
  type CalendarKey,
  type CalendarOptions,
  type DatePresetKind,
  type DateRange,
  type Weekday,
} from '../src/core/date/calendar';
import { Typeahead, filterOptions, foldText, groupIndices, moveIndex, type ListKey } from '../src/core/collections/list';
import { TreeModel, type TreeKey } from '../src/core/collections/tree';
import {
  avatarInitials,
  avatarTone,
  fractionToValue,
  pageCount,
  pageForFirstItem,
  paginationRange,
  setRangeThumb,
  sliderKey,
  snapValue,
  valueToFraction,
  type SliderKey,
} from '../src/core/collections/widgets';

const repo = resolve(import.meta.dirname, '../../..');
// eslint-disable-next-line @typescript-eslint/no-explicit-any
const fx: any = JSON.parse(readFileSync(resolve(repo, 'tests/fixtures/components.json'), 'utf8'));

const sorted = (s: Iterable<string>) => [...s].sort((a, b) => (a < b ? -1 : a > b ? 1 : 0));

function dayFlags(d: CalendarDay): string {
  return `${d.date}:${d.inMonth ? 'm' : ''}${d.isToday ? 't' : ''}${d.isDisabled ? 'x' : ''}${d.isSelected ? 's' : ''}${d.isRangeStart ? 'a' : ''}${d.isRangeEnd ? 'b' : ''}${d.inRange ? 'r' : ''}${d.inPreview ? 'p' : ''}`;
}

function disabledPredicate(c: { disabled: string[]; disableWeekends: boolean }) {
  const set = new Set(c.disabled);
  return (d: string) => set.has(d) || (c.disableWeekends && (dayOfWeek(d) === 0 || dayOfWeek(d) === 6));
}

describe('fixture parity with Slate.Core (tests/fixtures/components.json)', () => {
  it('popover positioning', () => {
    for (const c of fx.positioning) {
      const r = positionPopover({
        anchor: c.anchor, popupWidth: c.popupWidth, popupHeight: c.popupHeight, viewport: c.viewport, placement: c.placement as PopoverPlacement,
        offset: c.offset, padding: c.padding, flip: c.flip, shift: c.shift, arrowPadding: c.arrowPadding,
      });
      expect(r, c.id).toEqual(c.result);
    }
  });

  it('context-menu placement', () => {
    for (const c of fx.atPoint) expect(positionAtPoint(c.x, c.y, c.popupWidth, c.popupHeight, c.viewport as OverlayRect, c.padding), c.id).toEqual(c.result);
  });

  it('calendar month grids', () => {
    for (const c of fx.calendar) {
      const options: CalendarOptions = {
        firstDayOfWeek: c.firstDayOfWeek as Weekday, today: c.today, min: c.min, max: c.max, isDateDisabled: disabledPredicate(c),
        selected: c.selected, range: c.range as DateRange | null, hover: c.hover,
      };
      const m = buildMonth(c.year, c.month, options);
      expect(m.weekdays, c.id).toEqual(c.result.weekdays);
      expect(m.weeks.map((w) => ({ isoWeek: w.isoWeek, days: w.days.map(dayFlags) })), c.id).toEqual(c.result.weeks);
    }
  });

  it('calendar keyboard navigation', () => {
    for (const c of fx.calendarNav) {
      const options: CalendarOptions = { firstDayOfWeek: c.firstDayOfWeek as Weekday, min: c.min, max: c.max, isDateDisabled: disabledPredicate(c) };
      expect(navigateCalendar(c.focused, c.key as CalendarKey, c.shift, options), c.id).toBe(c.result);
    }
  });

  it('range picking, presets and typed dates', () => {
    for (const c of fx.dates.pickRange) expect(pickRange(c.current, c.clicked)).toEqual(c.result);
    for (const c of fx.dates.presets) expect(resolvePreset(c.kind as DatePresetKind, c.today), `${c.kind}@${c.today}`).toEqual({ start: c.start, end: c.end });
    for (const c of fx.dates.text) {
      const actual = c.op === 'format' ? formatDate(c.input, c.pattern) : parseDate(c.input, c.pattern);
      expect(actual, `${c.op} ${c.pattern} ${JSON.stringify(c.input)}`).toBe(c.result);
    }
  });

  it('list navigation', () => {
    for (const c of fx.listNav) expect(moveIndex(c.current, c.key as ListKey, c.disabled, { wrap: c.wrap, pageSize: c.pageSize }), c.id).toBe(c.result);
  });

  it('typeahead', () => {
    for (const c of fx.typeahead) {
      const t = new Typeahead(c.timeout);
      let current: number = c.current;
      const results = c.keys.map((k: { key: string; t: number }) => {
        const r = t.search(k.key, k.t, c.labels, current, c.disabled ?? undefined);
        if (r >= 0) current = r;
        return r;
      });
      expect(results, c.id).toEqual(c.results);
    }
  });

  it('folding, filtering and grouping', () => {
    for (const c of fx.filtering.fold) expect(foldText(c.input), c.input).toBe(c.result);
    for (const c of fx.filtering.filter) {
      const actual = filterOptions(fx.filtering.labels, c.query).map((m) => ({ index: m.index, score: m.score, ranges: m.ranges.map((r) => [r.start, r.length]) }));
      expect(actual, JSON.stringify(c.query)).toEqual(c.result);
    }
    expect(groupIndices(fx.filtering.group.keys)).toEqual(fx.filtering.group.result);
  });

  describe('tree', () => {
    interface N { id: string; label: string; lazy: boolean; children: N[] | null }
    const model = () => new TreeModel<N>(fx.tree.nodes as N[], { id: (n) => n.id, children: (n) => n.children, hasChildren: (n) => n.lazy });
    const rows = (r: ReturnType<TreeModel<N>['flatten']>) =>
      r.map((x) => ({ id: x.id, parentId: x.parentId, depth: x.depth, hasChildren: x.hasChildren, expanded: x.expanded, pos: x.positionInSet, size: x.setSize }));

    it('flatten', () => {
      for (const c of fx.tree.flatten) expect(rows(model().flatten(new Set(c.expanded)))).toEqual(c.rows);
    });

    it('filter', () => {
      for (const c of fx.tree.filter) {
        const q = foldText(c.query);
        const m = model();
        const { visible, expand } = m.filter((n) => foldText(n.label).includes(q));
        expect(sorted(visible), c.query).toEqual(c.visible);
        expect(sorted(expand), c.query).toEqual(c.expand);
        expect(rows(m.flatten(expand, visible)), c.query).toEqual(c.rows);
      }
    });

    it('keyboard', () => {
      const m = model();
      for (const c of fx.tree.nav) {
        const r = m.navigate(c.focus, c.key as TreeKey, new Set(c.expanded));
        expect({ focus: r.focusId, expanded: sorted(r.expanded) }, `${c.focus} ${c.key}`).toEqual(c.result);
      }
    });

    it('checkboxes', () => {
      const m = model();
      for (const c of fx.tree.check) {
        const result = m.toggleCheck(c.toggle, new Set(c.checked));
        expect(sorted(result), c.toggle).toEqual(c.result);
        for (const [id, state] of Object.entries(c.states)) expect(m.checkState(id, result), `${c.toggle}:${id}`).toBe(state);
      }
    });
  });

  it('pagination', () => {
    for (const c of fx.paging.ranges) expect(paginationRange(c.page, c.pageCount, c.siblings, c.boundaries), JSON.stringify(c)).toEqual(c.result);
    for (const c of fx.paging.pageForFirstItem) expect(pageForFirstItem(c.page, c.oldSize, c.newSize)).toBe(c.result);
    for (const c of fx.paging.pageCount) expect(pageCount(c.total, c.size)).toBe(c.result);
  });

  it('avatars', () => {
    for (const c of fx.avatar) {
      expect(avatarInitials(c.name), String(c.name)).toBe(c.initials);
      expect(avatarTone(c.name), String(c.name)).toBe(c.tone);
    }
  });

  it('slider', () => {
    for (const c of fx.slider.snap) expect(snapValue(c.value, c.min, c.max, c.step), JSON.stringify(c)).toBe(c.result);
    for (const c of fx.slider.keys) expect(sliderKey(c.value, c.key as SliderKey, c.min, c.max, c.step), JSON.stringify(c)).toBe(c.result);
    for (const c of fx.slider.fractions) {
      expect(fractionToValue(c.fraction, c.min, c.max, c.step)).toBe(c.value);
      expect(valueToFraction(c.value, c.min, c.max)).toBe(c.back);
    }
    for (const c of fx.slider.range) expect(setRangeThumb([c.start, c.end], c.thumb, c.value, c.min, c.max, c.step)).toEqual(c.result);
  });
});

describe('positioning behaviour', () => {
  const vp = { x: 0, y: 0, width: 1000, height: 700 };

  it('flips and reports it', () => {
    const r = positionPopover({ anchor: { x: 400, y: 600, width: 120, height: 32 }, popupWidth: 200, popupHeight: 150, viewport: vp });
    expect(r.placement).toBe('top');
    expect(r.flipped).toBe(true);
  });

  it('defaults to bottom with a 6px gap', () => {
    const r = positionPopover({ anchor: { x: 0, y: 0, width: 10, height: 10 }, popupWidth: 10, popupHeight: 10, viewport: vp, shift: false });
    expect(r.rect.y).toBe(16);
  });

  it('rejects negative sizes', () => {
    expect(() => positionPopover({ anchor: vp, popupWidth: -1, popupHeight: 1, viewport: vp })).toThrow(RangeError);
  });
});

describe('date helpers', () => {
  it('ISO weeks across year boundaries', () => {
    expect(isoWeek('2026-12-31')).toBe(53);
    expect(isoWeek('2027-01-01')).toBe(53);
    expect(isoWeek('2027-01-04')).toBe(1);
    expect(isoWeek('2024-12-30')).toBe(1);
  });

  it('month arithmetic clamps like .NET', () => {
    expect(addMonths('2026-01-31', 1)).toBe('2026-02-28');
    expect(addMonths('2028-01-31', 1)).toBe('2028-02-29');
    expect(addMonths('2026-03-15', -14)).toBe('2025-01-15');
  });

  it('locale helpers', () => {
    expect(shortPattern('en-US')).toBe('M/d/yyyy');
    expect(parseDate('6.10.2026', shortPattern('de-DE'))).toBe('2026-10-06');
    expect(firstDayOfWeek('en-US')).toBe(0);
    expect(firstDayOfWeek('en-GB')).toBe(1);
  });

  it('rejects garbage', () => {
    expect(parseDate('31/31/2026', 'M/d/yyyy')).toBeNull();
    expect(parseDate('12345/1/1', 'yyyy/M/d')).toBeNull();
  });
});

describe('list behaviour', () => {
  it('typeahead cycles the same letter and resets after the timeout', () => {
    const labels = ['Apple', 'Apricot', 'Banana', 'Blueberry'];
    const t = new Typeahead(500);
    expect(t.search('b', 0, labels, -1)).toBe(2);
    expect(t.search('b', 100, labels, 2)).toBe(3);
    expect(t.search('a', 2000, labels, 3)).toBe(0);
    t.reset();
    expect(t.currentBuffer).toBe('');
  });

  it('filter ranges point into the original text', () => {
    const [m] = filterOptions(['Café Crème'], 'creme');
    expect(m.ranges).toEqual([{ start: 5, length: 5 }]);
  });

  it('empty list navigation is -1', () => {
    expect(moveIndex(0, 'next', [])).toBe(-1);
  });
});

describe('tree behaviour', () => {
  interface N { id: string; kids?: N[]; lazy?: boolean }
  const make = () => new TreeModel<N>([{ id: 'a', kids: [{ id: 'a1' }, { id: 'a2' }] }, { id: 'b', lazy: true }], { id: (n) => n.id, children: (n) => n.kids, hasChildren: (n) => !!n.lazy });

  it('lazy loading lifecycle', () => {
    const t = make();
    expect(t.hasChildren('b')).toBe(true);
    expect(t.beginLoad('b')).toBe(true);
    expect(t.beginLoad('b')).toBe(false);
    t.failLoad('b');
    expect(t.beginLoad('b')).toBe(true);
    t.completeLoad('b', [{ id: 'b1' }]);
    expect(t.loadState('b')).toBe('loaded');
    expect(t.flatten(new Set(['b'])).map((r) => r.id)).toEqual(['a', 'b', 'b1']);
  });

  it('rejects duplicate ids', () => {
    expect(() => new TreeModel<N>([{ id: 'x' }, { id: 'x' }], { id: (n) => n.id, children: () => null })).toThrow();
  });
});

describe('widget maths', () => {
  it('pagination is constant width', () => {
    const widths = new Set(Array.from({ length: 40 }, (_, i) => paginationRange(i + 1, 40).length));
    expect([...widths]).toEqual([7]);
  });

  it('avatar tones spread evenly', () => {
    const counts = new Map<string, number>();
    for (let i = 0; i < 500; i++) counts.set(avatarTone(`user ${i}`), (counts.get(avatarTone(`user ${i}`)) ?? 0) + 1);
    expect(counts.size).toBe(5);
    for (const n of counts.values()) expect(n).toBeGreaterThan(60);
  });

  it('slider snaps decimal steps cleanly', () => {
    expect(snapValue(0.35, 0, 1, 0.1)).toBe(0.4);
    expect(sliderKey(0.5, 'increase', 0, 1, 0.01)).toBe(0.51);
  });
});
