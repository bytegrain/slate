namespace Slate.Layout;

/// <summary>Responsive breakpoints from the design tokens. Each applies from its minimum width upwards.</summary>
public enum Breakpoint
{
    Xs,
    Sm,
    Md,
    Lg,
    Xl,
}

public static class Breakpoints
{
    /// <summary>Minimum width (px) of each breakpoint, from <see cref="SlateTokens.Breakpoint"/>.</summary>
    public static double MinWidth(Breakpoint bp) => bp switch
    {
        Breakpoint.Xs => SlateTokens.Breakpoint.Xs,
        Breakpoint.Sm => SlateTokens.Breakpoint.Sm,
        Breakpoint.Md => SlateTokens.Breakpoint.Md,
        Breakpoint.Lg => SlateTokens.Breakpoint.Lg,
        Breakpoint.Xl => SlateTokens.Breakpoint.Xl,
        _ => throw new ArgumentOutOfRangeException(nameof(bp)),
    };

    /// <summary>The breakpoint active at a given viewport/window width.</summary>
    public static Breakpoint FromWidth(double width)
    {
        if (double.IsNaN(width) || width < 0)
            throw new ArgumentOutOfRangeException(nameof(width), width, "Width must be a non-negative number.");

        for (var bp = Breakpoint.Xl; bp > Breakpoint.Xs; bp--)
        {
            if (width >= MinWidth(bp))
                return bp;
        }
        return Breakpoint.Xs;
    }

    /// <summary>Max content width for a Container at this breakpoint, or null for full width.</summary>
    public static double? ContainerMaxWidth(Breakpoint bp) => bp switch
    {
        Breakpoint.Sm => SlateTokens.Container.Sm,
        Breakpoint.Md => SlateTokens.Container.Md,
        Breakpoint.Lg => SlateTokens.Container.Lg,
        Breakpoint.Xl => SlateTokens.Container.Xl,
        _ => null,
    };
}

/// <summary>
/// Per-breakpoint column spans for a 12-column grid item. Unset breakpoints inherit from the
/// next smaller one that is set (mobile-first), and Xs defaults to the full row.
/// </summary>
public readonly record struct GridSpan(int? Xs = null, int? Sm = null, int? Md = null, int? Lg = null, int? Xl = null)
{
    public const int Columns = 12;

    public int Resolve(Breakpoint bp)
    {
        int? value = bp switch
        {
            Breakpoint.Xl => Xl ?? Lg ?? Md ?? Sm ?? Xs,
            Breakpoint.Lg => Lg ?? Md ?? Sm ?? Xs,
            Breakpoint.Md => Md ?? Sm ?? Xs,
            Breakpoint.Sm => Sm ?? Xs,
            _ => Xs,
        };
        var span = value ?? Columns;
        if (span is < 1 or > Columns)
            throw new ArgumentOutOfRangeException(nameof(bp), span, $"Grid spans must be between 1 and {Columns}.");
        return span;
    }
}
