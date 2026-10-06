using System.Globalization;

namespace Slate.Dates;

/// <summary>An inclusive date range. <see cref="End"/> is null while a range is being picked.</summary>
public readonly record struct DateRange(DateOnly Start, DateOnly? End)
{
    public bool Contains(DateOnly d) => End is { } end && d >= Start && d <= end;
}

/// <summary>Inputs that decide each day's state in a month grid.</summary>
public sealed record CalendarOptions
{
    public DayOfWeek FirstDayOfWeek { get; init; } = DayOfWeek.Monday;
    public DateOnly? Today { get; init; }
    public DateOnly? Min { get; init; }
    public DateOnly? Max { get; init; }
    public Func<DateOnly, bool>? IsDateDisabled { get; init; }

    /// <summary>Single-selection value.</summary>
    public DateOnly? Selected { get; init; }

    /// <summary>Range-selection value (End null while the second date is pending).</summary>
    public DateRange? Range { get; init; }

    /// <summary>Hovered/focused day: previews the range while its end is pending.</summary>
    public DateOnly? Hover { get; init; }

    public bool IsDisabled(DateOnly d) =>
        (Min is { } min && d < min) || (Max is { } max && d > max) || (IsDateDisabled?.Invoke(d) ?? false);
}

public sealed record CalendarDay(
    DateOnly Date,
    bool InMonth,
    bool IsToday,
    bool IsDisabled,
    bool IsSelected,
    bool IsRangeStart,
    bool IsRangeEnd,
    bool InRange,
    bool InPreview);

/// <summary>One row of the month grid. <see cref="IsoWeek"/> is the ISO-8601 week of the row's Thursday.</summary>
public sealed record CalendarWeek(int IsoWeek, IReadOnlyList<CalendarDay> Days);

public sealed record CalendarMonth(int Year, int Month, IReadOnlyList<DayOfWeek> Weekdays, IReadOnlyList<CalendarWeek> Weeks);

public enum CalendarKey
{
    Left,
    Right,
    Up,
    Down,
    Home,
    End,
    PageUp,
    PageDown,
}

/// <summary>Built-in date-range presets, resolved relative to a supplied "today".</summary>
public enum DatePresetKind
{
    Today,
    Yesterday,
    Last7Days,
    Last30Days,
    ThisMonth,
    LastMonth,
    ThisYear,
}

public sealed record DatePreset(string Label, DatePresetKind Kind);

/// <summary>
/// The calendar model behind Slate's date picker: a fixed 6×7 month grid with per-day state, keyboard
/// navigation that skips disabled days, range picking and presets. Mirrored exactly by @slate/web.
/// </summary>
public static class CalendarModel
{
    public const int Rows = 6;

    public static CalendarMonth Build(int year, int month, CalendarOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var first = new DateOnly(year, month, 1);
        var lead = ((int)first.DayOfWeek - (int)options.FirstDayOfWeek + 7) % 7;
        var start = first.AddDays(-lead);

        var weekdays = Enumerable.Range(0, 7).Select(i => (DayOfWeek)(((int)options.FirstDayOfWeek + i) % 7)).ToList();

        DateRange? effective = options.Range;
        DateRange? preview = null;
        if (options.Range is { End: null } pending && options.Hover is { } hover)
            preview = hover < pending.Start ? new DateRange(hover, pending.Start) : new DateRange(pending.Start, hover);

        var weeks = new List<CalendarWeek>(Rows);
        for (var row = 0; row < Rows; row++)
        {
            var days = new List<CalendarDay>(7);
            DateOnly? thursday = null;
            for (var col = 0; col < 7; col++)
            {
                var d = start.AddDays(row * 7 + col);
                if (d.DayOfWeek == DayOfWeek.Thursday) thursday = d;
                var range = effective;
                days.Add(new CalendarDay(
                    d,
                    InMonth: d.Month == month && d.Year == year,
                    IsToday: options.Today == d,
                    IsDisabled: options.IsDisabled(d),
                    IsSelected: options.Selected == d || (range is { } rs && (rs.Start == d || rs.End == d)),
                    IsRangeStart: range is { } r1 && r1.Start == d,
                    IsRangeEnd: range is { End: { } e } && e == d,
                    InRange: range is { } r2 && r2.Contains(d),
                    InPreview: preview is { } p && p.Contains(d)));
            }
            weeks.Add(new CalendarWeek(IsoWeek(thursday!.Value), days));
        }
        return new CalendarMonth(year, month, weekdays, weeks);
    }

    /// <summary>ISO-8601 week number (weeks start Monday; week 1 contains the year's first Thursday).</summary>
    public static int IsoWeek(DateOnly d) => ISOWeek.GetWeekOfYear(d.ToDateTime(TimeOnly.MinValue));

    /// <summary>The culture's first day of week (e.g. Sunday for en-US, Monday for en-GB).</summary>
    public static DayOfWeek FirstDayOfWeek(CultureInfo culture) => culture.DateTimeFormat.FirstDayOfWeek;

    /// <summary>
    /// Moves the focused day for a key. Shift with PageUp/PageDown moves by a year. Lands on the nearest enabled
    /// day in the direction of travel (arrows keep their step); stays put if nothing is reachable.
    /// </summary>
    public static DateOnly Navigate(DateOnly focused, CalendarKey key, bool shift, CalendarOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var (target, step) = key switch
        {
            CalendarKey.Left => (focused.AddDays(-1), -1),
            CalendarKey.Right => (focused.AddDays(1), 1),
            CalendarKey.Up => (focused.AddDays(-7), -7),
            CalendarKey.Down => (focused.AddDays(7), 7),
            CalendarKey.Home => (focused.AddDays(-(((int)focused.DayOfWeek - (int)options.FirstDayOfWeek + 7) % 7)), 1),
            CalendarKey.End => (focused.AddDays(6 - (((int)focused.DayOfWeek - (int)options.FirstDayOfWeek + 7) % 7)), -1),
            CalendarKey.PageUp => (shift ? focused.AddYears(-1) : focused.AddMonths(-1), -1),
            CalendarKey.PageDown => (shift ? focused.AddYears(1) : focused.AddMonths(1), 1),
            _ => throw new ArgumentOutOfRangeException(nameof(key)),
        };

        if (options.Min is { } min && target < min) target = min;
        if (options.Max is { } max && target > max) target = max;

        for (var i = 0; i < 400; i++)
        {
            if (!options.IsDisabled(target))
                return target;
            var next = target.AddDays(step);
            if ((options.Min is { } lo && next < lo) || (options.Max is { } hi && next > hi))
                break;
            target = next;
        }
        return focused;
    }

    /// <summary>
    /// Range picking: the first click starts a range, the second completes it (in either order), a click after a
    /// complete range starts a new one.
    /// </summary>
    public static DateRange PickRange(DateRange? current, DateOnly clicked) => current switch
    {
        { End: null } pending when clicked < pending.Start => new DateRange(clicked, pending.Start),
        { End: null } pending => new DateRange(pending.Start, clicked),
        _ => new DateRange(clicked, null),
    };

    /// <summary>Default presets in display order.</summary>
    public static IReadOnlyList<DatePreset> DefaultPresets { get; } =
    [
        new("Today", DatePresetKind.Today),
        new("Yesterday", DatePresetKind.Yesterday),
        new("Last 7 days", DatePresetKind.Last7Days),
        new("Last 30 days", DatePresetKind.Last30Days),
        new("This month", DatePresetKind.ThisMonth),
        new("Last month", DatePresetKind.LastMonth),
        new("This year", DatePresetKind.ThisYear),
    ];

    /// <summary>Resolves a preset to an inclusive range relative to <paramref name="today"/>.</summary>
    public static DateRange Resolve(DatePresetKind kind, DateOnly today) => kind switch
    {
        DatePresetKind.Today => new(today, today),
        DatePresetKind.Yesterday => new(today.AddDays(-1), today.AddDays(-1)),
        DatePresetKind.Last7Days => new(today.AddDays(-6), today),
        DatePresetKind.Last30Days => new(today.AddDays(-29), today),
        DatePresetKind.ThisMonth => new(new DateOnly(today.Year, today.Month, 1), new DateOnly(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month))),
        DatePresetKind.LastMonth => LastMonth(today),
        DatePresetKind.ThisYear => new(new DateOnly(today.Year, 1, 1), new DateOnly(today.Year, 12, 31)),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static DateRange LastMonth(DateOnly today)
    {
        var first = new DateOnly(today.Year, today.Month, 1).AddMonths(-1);
        return new(first, new DateOnly(first.Year, first.Month, DateTime.DaysInMonth(first.Year, first.Month)));
    }
}

/// <summary>
/// Date text in a small, culture-free pattern language (tokens yyyy, yy, MM, M, dd, d; anything else is literal),
/// so typed dates parse identically on every platform. Use <see cref="ShortPattern"/> to derive a pattern from a culture.
/// </summary>
public static class DateText
{
    public const string Iso = "yyyy-MM-dd";

    public static string Format(DateOnly d, string pattern)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var (token, literal) in Tokenize(pattern))
        {
            sb.Append(token switch
            {
                "yyyy" => d.Year.ToString("D4", CultureInfo.InvariantCulture),
                "yy" => (d.Year % 100).ToString("D2", CultureInfo.InvariantCulture),
                "MM" => d.Month.ToString("D2", CultureInfo.InvariantCulture),
                "M" => d.Month.ToString(CultureInfo.InvariantCulture),
                "dd" => d.Day.ToString("D2", CultureInfo.InvariantCulture),
                "d" => d.Day.ToString(CultureInfo.InvariantCulture),
                _ => literal,
            });
        }
        return sb.ToString();
    }

    /// <summary>
    /// Parses lenient input: ISO (yyyy-M-d) always works; otherwise numbers are read in the pattern's
    /// year/month/day order with any separators. Two-digit years pivot at 50 (00–49 → 2000s). Null if invalid.
    /// </summary>
    public static DateOnly? Parse(string? text, string pattern)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var s = text.Trim();

        var iso = System.Text.RegularExpressions.Regex.Match(s, @"^(\d{4})-(\d{1,2})-(\d{1,2})$");
        if (iso.Success)
            return Make(int.Parse(iso.Groups[1].Value, CultureInfo.InvariantCulture), int.Parse(iso.Groups[2].Value, CultureInfo.InvariantCulture), int.Parse(iso.Groups[3].Value, CultureInfo.InvariantCulture));

        var numbers = System.Text.RegularExpressions.Regex.Matches(s, @"\d+").Select(m => m.Value).ToList();
        var order = Tokenize(pattern).Select(t => t.Token).Where(t => t is not null).ToList();
        if (numbers.Count != 3 || order.Count != 3) return null;

        int year = 0, month = 0, day = 0;
        for (var i = 0; i < 3; i++)
        {
            var n = numbers[i];
            if (n.Length > 4) return null;
            var v = int.Parse(n, CultureInfo.InvariantCulture);
            switch (order[i])
            {
                case "yyyy" or "yy":
                    year = n.Length <= 2 ? (v <= 49 ? 2000 + v : 1900 + v) : v;
                    break;
                case "MM" or "M":
                    month = v;
                    break;
                default:
                    day = v;
                    break;
            }
        }
        return Make(year, month, day);
    }

    /// <summary>The culture's short date pattern reduced to DateText tokens (e.g. en-US → "M/d/yyyy").</summary>
    public static string ShortPattern(CultureInfo culture)
    {
        var raw = culture.DateTimeFormat.ShortDatePattern;
        var sb = new System.Text.StringBuilder();
        foreach (var c in raw)
        {
            if (c is 'y' or 'M' or 'd' or '/' or '-' or '.' or ' ') sb.Append(c == '/' ? culture.DateTimeFormat.DateSeparator : c.ToString());
        }
        var p = sb.ToString();
        p = System.Text.RegularExpressions.Regex.Replace(p, "y+", m => m.Length <= 2 ? "yy" : "yyyy");
        return p;
    }

    private static DateOnly? Make(int y, int m, int d) =>
        y is >= 1 and <= 9999 && m is >= 1 and <= 12 && d >= 1 && d <= DateTime.DaysInMonth(y, m) ? new DateOnly(y, m, d) : null;

    private static IEnumerable<(string? Token, string Literal)> Tokenize(string pattern)
    {
        var i = 0;
        while (i < pattern.Length)
        {
            var c = pattern[i];
            if (c is 'y' or 'M' or 'd')
            {
                var j = i;
                while (j < pattern.Length && pattern[j] == c) j++;
                var run = pattern[i..j];
                yield return (c switch
                {
                    'y' => run.Length <= 2 ? "yy" : "yyyy",
                    'M' => run.Length == 1 ? "M" : "MM",
                    _ => run.Length == 1 ? "d" : "dd",
                }, run);
                i = j;
            }
            else
            {
                yield return (null, c.ToString());
                i++;
            }
        }
    }
}
