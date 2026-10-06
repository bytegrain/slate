using System.Text;

namespace Slate.Collections;

/// <summary>A pagination slot: a page number, or an ellipsis standing for a gap (Page = 0).</summary>
public readonly record struct PaginationItem(bool IsEllipsis, int Page)
{
    public static PaginationItem Ellipsis => new(true, 0);
    public static PaginationItem Of(int page) => new(false, page);
    public override string ToString() => IsEllipsis ? "…" : Page.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

public static class PaginationRange
{
    /// <summary>
    /// Page slots for a pager: <paramref name="boundaries"/> pages at each end, <paramref name="siblings"/> on each
    /// side of the current page, and ellipses for gaps. The slot count is constant while pages exceed it, so the
    /// pager never changes width as the page changes.
    /// </summary>
    public static IReadOnlyList<PaginationItem> Compute(int page, int pageCount, int siblings = 1, int boundaries = 1)
    {
        if (pageCount < 1) return [];
        if (siblings < 0 || boundaries < 0) throw new ArgumentOutOfRangeException(nameof(siblings));
        page = Math.Clamp(page, 1, pageCount);

        static IEnumerable<int> R(int from, int to) => from > to ? [] : Enumerable.Range(from, to - from + 1);

        var startPages = R(1, Math.Min(boundaries, pageCount)).ToList();
        var endPages = R(Math.Max(pageCount - boundaries + 1, boundaries + 1), pageCount).ToList();

        var siblingsStart = Math.Max(Math.Min(page - siblings, pageCount - boundaries - siblings * 2 - 1), boundaries + 2);
        var siblingsEnd = Math.Min(Math.Max(page + siblings, boundaries + siblings * 2 + 2), endPages.Count > 0 ? endPages[0] - 2 : pageCount - 1);

        var items = new List<PaginationItem>();
        items.AddRange(startPages.Select(PaginationItem.Of));

        if (siblingsStart > boundaries + 2) items.Add(PaginationItem.Ellipsis);
        else if (boundaries + 1 < pageCount - boundaries) items.Add(PaginationItem.Of(boundaries + 1));

        items.AddRange(R(siblingsStart, siblingsEnd).Select(PaginationItem.Of));

        if (siblingsEnd < pageCount - boundaries - 1) items.Add(PaginationItem.Ellipsis);
        else if (pageCount - boundaries > boundaries) items.Add(PaginationItem.Of(pageCount - boundaries));

        items.AddRange(endPages.Select(PaginationItem.Of));

        // Small page counts can produce duplicates/out-of-order pages from the formula above; normalise.
        var seen = new HashSet<int>();
        return items.Where(i => i.IsEllipsis || (i.Page >= 1 && i.Page <= pageCount && seen.Add(i.Page))).ToList();
    }

    public static int PageCount(int totalCount, int pageSize)
    {
        if (pageSize < 1) throw new ArgumentOutOfRangeException(nameof(pageSize));
        return Math.Max(1, (int)Math.Ceiling(Math.Max(0, totalCount) / (double)pageSize));
    }

    /// <summary>After a page-size change, the page that still shows the first item of the current page (1-based).</summary>
    public static int PageForFirstItem(int page, int oldPageSize, int newPageSize)
    {
        if (oldPageSize < 1 || newPageSize < 1) throw new ArgumentOutOfRangeException(nameof(newPageSize));
        var firstIndex = (Math.Max(page, 1) - 1) * oldPageSize;
        return firstIndex / newPageSize + 1;
    }
}

public static class AvatarText
{
    private static readonly Tone[] Palette = [Tone.Accent, Tone.Info, Tone.Success, Tone.Warning, Tone.Danger];

    /// <summary>
    /// Initials for a name: first letters of the first and last words ("Aaron Griffin" → "AG"), one letter for a
    /// single word, the local part for emails ("jo.marsh@x.io" → "JM"), "?" when empty.
    /// </summary>
    public static string Initials(string? name)
    {
        var s = (name ?? "").Trim().TrimStart('@');
        var at = s.IndexOf('@');
        if (at > 0) s = s[..at];
        // Real names split on whitespace only ("Jo Marsh-Smith" → JM); handles and emails also on . _ -
        char[] separators = s.Any(char.IsWhiteSpace) ? [' ', '\t', '\n', '\r'] : ['.', '_', '-'];
        var words = s.Split(separators, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return "?";
        var first = FirstLetter(words[0]);
        return words.Length == 1 ? first : first + FirstLetter(words[^1]);
    }

    /// <summary>
    /// A stable tone for a name (FNV-1a over the folded name's UTF-16 units, then the MurmurHash3 finaliser for an even
    /// spread), so the same person is always the same colour on every platform.
    /// </summary>
    public static Tone ToneFor(string? name)
    {
        var folded = TextFolding.Fold((name ?? "").Trim());
        var hash = 2166136261u;
        foreach (var c in folded)
        {
            hash ^= c;
            hash *= 16777619u;
        }
        hash ^= hash >> 16;
        hash *= 0x85EBCA6Bu;
        hash ^= hash >> 13;
        hash *= 0xC2B2AE35u;
        hash ^= hash >> 16;
        return Palette[hash % (uint)Palette.Length];
    }

    private static string FirstLetter(string word)
    {
        var rune = word.EnumerateRunes().First();
        return Rune.ToUpperInvariant(rune).ToString();
    }
}

public enum SliderKey
{
    Increase,
    Decrease,
    PageIncrease,
    PageDecrease,
    Home,
    End,
}

/// <summary>Slider value maths: step snapping, clamping, keyboard increments, value ↔ position, range thumbs.</summary>
public static class SliderMath
{
    public static double Snap(double value, double min, double max, double step)
    {
        if (max < min) throw new ArgumentOutOfRangeException(nameof(max));
        if (step <= 0) return Clamp(value, min, max);
        // Tidy first so 0.35 / 0.1 (= 3.4999…) rounds like the decimal value it represents.
        var steps = Math.Floor(Tidy((value - min) / step) + 0.5);
        var snapped = Tidy(min + steps * step);
        // Stay on the step grid inside [min, max] (when the range isn't a multiple of the step, the last
        // reachable stepped value is below max; Home/End still jump to min/max exactly).
        while (snapped > max && snapped - step >= min) snapped = Tidy(snapped - step);
        while (snapped < min) snapped = Tidy(snapped + step);
        return Clamp(snapped, min, max);
    }

    public static double Key(double value, SliderKey key, double min, double max, double step)
    {
        var page = Math.Max(step, Snap(min + (max - min) / 10, min, max, step) - min);
        return key switch
        {
            SliderKey.Increase => Snap(value + step, min, max, step),
            SliderKey.Decrease => Snap(value - step, min, max, step),
            SliderKey.PageIncrease => Snap(value + page, min, max, step),
            SliderKey.PageDecrease => Snap(value - page, min, max, step),
            SliderKey.Home => min,
            SliderKey.End => max,
            _ => throw new ArgumentOutOfRangeException(nameof(key)),
        };
    }

    /// <summary>Fraction 0–1 along the track.</summary>
    public static double ToFraction(double value, double min, double max) =>
        max == min ? 0 : Clamp((value - min) / (max - min), 0, 1);

    public static double FromFraction(double fraction, double min, double max, double step) =>
        Snap(min + Clamp(fraction, 0, 1) * (max - min), min, max, step);

    /// <summary>Moves one thumb of a range slider without letting it cross the other.</summary>
    public static (double Start, double End) SetRangeThumb((double Start, double End) range, int thumb, double value, double min, double max, double step)
    {
        var v = Snap(value, min, max, step);
        return thumb == 0 ? (Math.Min(v, range.End), range.End) : (range.Start, Math.Max(v, range.Start));
    }

    private static double Clamp(double v, double min, double max) => Math.Min(Math.Max(v, min), max);

    /// <summary>Removes binary floating-point noise (0.30000000000000004 → 0.3) at 10 decimal places.</summary>
    private static double Tidy(double v) => Math.Floor(v * 1e10 + 0.5) / 1e10;
}
