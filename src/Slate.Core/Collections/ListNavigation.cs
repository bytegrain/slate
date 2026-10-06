using System.Globalization;
using System.Text;

namespace Slate.Collections;

public enum ListKey
{
    Next,
    Previous,
    First,
    Last,
    PageDown,
    PageUp,
}

public sealed record ListNavigationOptions
{
    /// <summary>Next on the last item goes to the first (and vice versa). Menus wrap; listboxes usually don't.</summary>
    public bool Wrap { get; init; }

    /// <summary>Items moved by PageUp/PageDown.</summary>
    public int PageSize { get; init; } = 10;
}

/// <summary>
/// Roving active-index logic shared by Select, Menu, Tabs, Tree and Segmented controls: skips disabled items,
/// optional wrapping, paging. Index -1 means "nothing active".
/// </summary>
public static class ListNavigator
{
    public static int Move(int current, ListKey key, IReadOnlyList<bool> disabled, ListNavigationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(disabled);
        options ??= new ListNavigationOptions();
        var count = disabled.Count;
        if (count == 0) return -1;
        bool Enabled(int i) => i >= 0 && i < count && !disabled[i];

        int FirstFrom(int start, int step)
        {
            for (var i = start; i >= 0 && i < count; i += step)
                if (Enabled(i)) return i;
            return -1;
        }

        switch (key)
        {
            case ListKey.First:
                return FirstFrom(0, 1);
            case ListKey.Last:
                return FirstFrom(count - 1, -1);
            case ListKey.Next:
            {
                if (current < 0) return FirstFrom(0, 1);
                var next = FirstFrom(current + 1, 1);
                if (next >= 0) return next;
                if (options.Wrap)
                {
                    var wrapped = FirstFrom(0, 1);
                    if (wrapped >= 0) return wrapped;
                }
                return Enabled(current) ? current : FirstFrom(count - 1, -1);
            }
            case ListKey.Previous:
            {
                if (current < 0) return FirstFrom(count - 1, -1);
                var prev = FirstFrom(current - 1, -1);
                if (prev >= 0) return prev;
                if (options.Wrap)
                {
                    var wrapped = FirstFrom(count - 1, -1);
                    if (wrapped >= 0) return wrapped;
                }
                return Enabled(current) ? current : FirstFrom(0, 1);
            }
            case ListKey.PageDown:
            {
                var target = Math.Min(Math.Max(current, 0) + options.PageSize, count - 1);
                var found = FirstFrom(target, 1);
                return found >= 0 ? found : FirstFrom(target, -1);
            }
            case ListKey.PageUp:
            {
                var target = Math.Max((current < 0 ? count - 1 : current) - options.PageSize, 0);
                var found = FirstFrom(target, -1);
                return found >= 0 ? found : FirstFrom(target, 1);
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(key));
        }
    }
}

/// <summary>
/// Type-to-jump for lists. Characters typed within <see cref="Timeout"/> build a prefix; typing the same letter
/// repeatedly cycles through items starting with it. Timestamps are passed in so behaviour is deterministic.
/// </summary>
public sealed class Typeahead
{
    private readonly StringBuilder _buffer = new();
    private long _last = long.MinValue;

    public Typeahead(int timeoutMs = 500) => Timeout = timeoutMs;

    public int Timeout { get; }

    public string Buffer => _buffer.ToString();

    public void Reset()
    {
        _buffer.Clear();
        _last = long.MinValue;
    }

    /// <returns>The matching index, or -1 (the buffer is kept so further typing can still match).</returns>
    public int Search(string key, long timestampMs, IReadOnlyList<string> labels, int current, IReadOnlyList<bool>? disabled = null)
    {
        ArgumentNullException.ThrowIfNull(labels);
        if (string.IsNullOrEmpty(key)) return -1;
        if (_last == long.MinValue || timestampMs - _last > Timeout) _buffer.Clear();
        _last = timestampMs;
        _buffer.Append(TextFolding.Fold(key));

        var buffer = _buffer.ToString();
        var count = labels.Count;
        if (count == 0) return -1;

        var repeated = buffer.Length > 1 && buffer.All(c => c == buffer[0]);
        var prefix = repeated ? buffer[..1] : buffer;
        // A fresh single-letter search (or a cycling one) starts after the current item; extending a prefix
        // keeps the current item if it still matches.
        var start = buffer.Length == 1 || repeated ? current + 1 : Math.Max(current, 0);

        for (var n = 0; n < count; n++)
        {
            var i = ((start + n) % count + count) % count;
            if (disabled is not null && i < disabled.Count && disabled[i]) continue;
            if (TextFolding.Fold(labels[i]).StartsWith(prefix, StringComparison.Ordinal))
                return i;
        }
        return -1;
    }
}

/// <summary>Case- and diacritic-insensitive text folding (NFD, lower-case, combining marks removed).</summary>
public static class TextFolding
{
    public static string Fold(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var rune in text.EnumerateRunes())
            AppendFolded(sb, rune);
        return sb.ToString();
    }

    internal static void AppendFolded(StringBuilder sb, Rune rune)
    {
        var decomposed = rune.ToString().Normalize(NormalizationForm.FormD);
        foreach (var part in decomposed.EnumerateRunes())
        {
            var lower = Rune.ToLowerInvariant(part);
            if (Rune.GetUnicodeCategory(lower) != UnicodeCategory.NonSpacingMark)
                sb.Append(lower.ToString());
        }
    }

    /// <summary>Folded code points of <paramref name="text"/> with, for each, the UTF-16 start/length of the source character.</summary>
    internal static (int[] CodePoints, int[] SourceStart, int[] SourceLength) FoldWithMap(string text)
    {
        var cps = new List<int>();
        var starts = new List<int>();
        var lengths = new List<int>();
        var offset = 0;
        var sb = new StringBuilder();
        foreach (var rune in text.EnumerateRunes())
        {
            sb.Clear();
            AppendFolded(sb, rune);
            foreach (var r in sb.ToString().EnumerateRunes())
            {
                cps.Add(r.Value);
                starts.Add(offset);
                lengths.Add(rune.Utf16SequenceLength);
            }
            offset += rune.Utf16SequenceLength;
        }
        return (cps.ToArray(), starts.ToArray(), lengths.ToArray());
    }
}

/// <summary>A highlighted span in an option label, in UTF-16 code units.</summary>
public readonly record struct TextRange(int Start, int Length);

public sealed record OptionMatch(int Index, int Score, IReadOnlyList<TextRange> Ranges);

/// <summary>
/// Searchable-select filtering. Every whitespace-separated query term must appear in the label (case- and
/// diacritic-insensitive). Scores: label start 3, word start 2, elsewhere 1 (summed over terms); results sort by
/// score, then original order. Ranges give the matched spans in the original label for highlighting.
/// </summary>
public static class OptionFilter
{
    public static IReadOnlyList<OptionMatch> Filter(IReadOnlyList<string> labels, string? query)
    {
        ArgumentNullException.ThrowIfNull(labels);
        var terms = (query ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(t => TextFolding.FoldWithMap(t).CodePoints)
            .Where(t => t.Length > 0)
            .ToList();

        if (terms.Count == 0)
            return labels.Select((_, i) => new OptionMatch(i, 0, [])).ToList();

        var results = new List<OptionMatch>();
        for (var index = 0; index < labels.Count; index++)
        {
            var (cps, starts, lengths) = TextFolding.FoldWithMap(labels[index]);
            var score = 0;
            var ranges = new List<TextRange>();
            var ok = true;
            foreach (var term in terms)
            {
                var (pos, s) = BestOccurrence(cps, term);
                if (pos < 0) { ok = false; break; }
                score += s;
                var last = pos + term.Length - 1;
                var start = starts[pos];
                ranges.Add(new TextRange(start, starts[last] + lengths[last] - start));
            }
            if (ok) results.Add(new OptionMatch(index, score, Merge(ranges)));
        }

        return results.OrderByDescending(r => r.Score).ThenBy(r => r.Index).ToList();
    }

    /// <summary>Groups item indices by key, keeping the order in which each group first appears.</summary>
    public static IReadOnlyList<(string Key, IReadOnlyList<int> Indices)> Group(IReadOnlyList<string> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        var order = new List<string>();
        var map = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        for (var i = 0; i < keys.Count; i++)
        {
            if (!map.TryGetValue(keys[i], out var list))
            {
                map[keys[i]] = list = [];
                order.Add(keys[i]);
            }
            list.Add(i);
        }
        return order.Select(k => (k, (IReadOnlyList<int>)map[k])).ToList();
    }

    private static (int Position, int Score) BestOccurrence(int[] text, int[] term)
    {
        var first = -1;
        for (var i = 0; i + term.Length <= text.Length; i++)
        {
            var match = true;
            for (var j = 0; j < term.Length; j++)
                if (text[i + j] != term[j]) { match = false; break; }
            if (!match) continue;
            if (i == 0) return (0, 3);
            if (!IsWordChar(text[i - 1])) return (i, 2);
            if (first < 0) first = i;
        }
        return first < 0 ? (-1, 0) : (first, 1);
    }

    private static bool IsWordChar(int codePoint) =>
        Rune.IsValid(codePoint) && Rune.IsLetterOrDigit(new Rune(codePoint));

    private static IReadOnlyList<TextRange> Merge(List<TextRange> ranges)
    {
        if (ranges.Count < 2) return ranges;
        var sorted = ranges.OrderBy(r => r.Start).ToList();
        var merged = new List<TextRange> { sorted[0] };
        foreach (var r in sorted.Skip(1))
        {
            var last = merged[^1];
            if (r.Start <= last.Start + last.Length)
                merged[^1] = new TextRange(last.Start, Math.Max(last.Start + last.Length, r.Start + r.Length) - last.Start);
            else
                merged.Add(r);
        }
        return merged;
    }
}
