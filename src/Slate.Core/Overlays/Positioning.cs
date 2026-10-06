namespace Slate.Overlays;

/// <summary>An axis-aligned rectangle in device-independent pixels.</summary>
public readonly record struct OverlayRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public double CenterX => X + Width / 2;
    public double CenterY => Y + Height / 2;
}

/// <summary>Input for <see cref="PopoverPositioner.Position"/>.</summary>
public sealed record PositionRequest
{
    public required OverlayRect Anchor { get; init; }
    public required double PopupWidth { get; init; }
    public required double PopupHeight { get; init; }
    public required OverlayRect Viewport { get; init; }
    public PopoverPlacement Placement { get; init; } = PopoverPlacement.Bottom;

    /// <summary>Gap between anchor and popup.</summary>
    public double Offset { get; init; } = 6;

    /// <summary>Minimum distance kept from the viewport edges.</summary>
    public double Padding { get; init; } = 8;

    /// <summary>Move to the opposite side when the preferred side doesn't fit.</summary>
    public bool Flip { get; init; } = true;

    /// <summary>Slide along the anchor's edge to stay inside the viewport.</summary>
    public bool Shift { get; init; } = true;

    /// <summary>Closest the arrow may get to the popup's corners.</summary>
    public double ArrowPadding { get; init; } = 12;
}

/// <summary>
/// The computed popup rectangle. <see cref="MaxHeight"/> is set when the popup had to be shortened to fit
/// (render it scrollable). <see cref="ArrowOffset"/> is the arrow's distance from the popup's start edge
/// along the cross axis (x for top/bottom placements, y for left/right).
/// </summary>
public sealed record PositionResult(OverlayRect Rect, PopoverPlacement Placement, double? MaxHeight, double ArrowOffset, bool Flipped);

/// <summary>
/// Platform-independent popover/menu/tooltip placement (flip, shift, size constraint). Every Slate platform
/// uses this so overlays land in the same place everywhere; @slate/web has an identical TypeScript port.
/// </summary>
public static class PopoverPositioner
{
    private enum Side { Top, Bottom, Left, Right }

    private enum Align { Center, Start, End }

    public static PositionResult Position(PositionRequest r)
    {
        ArgumentNullException.ThrowIfNull(r);
        if (r.PopupWidth < 0 || r.PopupHeight < 0)
            throw new ArgumentOutOfRangeException(nameof(r), "Popup size must be non-negative.");

        var (side, align) = Split(r.Placement);
        var vp = r.Viewport;
        var w = r.PopupWidth;
        var h = r.PopupHeight;
        double? maxHeight = null;

        var chosen = side;
        if (r.Flip && Space(r, side) < Extent(side, w, h))
        {
            var opposite = Opposite(side);
            if (Space(r, opposite) >= Extent(opposite, w, h))
                chosen = opposite;
            else if (Space(r, opposite) > Space(r, side))
                chosen = opposite;
        }

        // Shorten vertical popups that still don't fit on their side.
        if (chosen is Side.Top or Side.Bottom)
        {
            var available = Math.Max(0, Space(r, chosen));
            if (h > available)
            {
                h = available;
                maxHeight = available;
            }
        }
        else
        {
            var available = Math.Max(0, vp.Height - 2 * r.Padding);
            if (h > available)
            {
                h = available;
                maxHeight = available;
            }
        }

        var (x, y) = Place(r.Anchor, chosen, align, w, h, r.Offset);

        if (r.Shift)
        {
            if (chosen is Side.Top or Side.Bottom)
                x = Clamp(x, vp.X + r.Padding, vp.Right - r.Padding - w);
            else
                y = Clamp(y, vp.Y + r.Padding, vp.Bottom - r.Padding - h);
        }

        var arrow = chosen is Side.Top or Side.Bottom
            ? Clamp(r.Anchor.CenterX - x, r.ArrowPadding, w - r.ArrowPadding)
            : Clamp(r.Anchor.CenterY - y, r.ArrowPadding, h - r.ArrowPadding);

        return new PositionResult(new OverlayRect(x, y, w, h), Join(chosen, align), maxHeight, arrow, chosen != side);
    }

    /// <summary>
    /// Context-menu placement at a pointer position: opens down-right from the point, flipping left/up where
    /// it would overflow, then clamps inside the viewport.
    /// </summary>
    public static PositionResult PositionAtPoint(double pointX, double pointY, double popupWidth, double popupHeight, OverlayRect viewport, double padding = 8)
    {
        var x = pointX;
        var y = pointY;
        var h = popupHeight;
        double? maxHeight = null;

        var flippedX = x + popupWidth > viewport.Right - padding;
        if (flippedX) x = pointX - popupWidth;

        var flippedY = y + h > viewport.Bottom - padding;
        if (flippedY) y = pointY - h;

        var availableHeight = Math.Max(0, viewport.Height - 2 * padding);
        if (h > availableHeight)
        {
            h = availableHeight;
            maxHeight = availableHeight;
        }

        x = Clamp(x, viewport.X + padding, viewport.Right - padding - popupWidth);
        y = Clamp(y, viewport.Y + padding, viewport.Bottom - padding - h);

        var placement = (flippedY, flippedX) switch
        {
            (false, false) => PopoverPlacement.BottomStart,
            (false, true) => PopoverPlacement.BottomEnd,
            (true, false) => PopoverPlacement.TopStart,
            _ => PopoverPlacement.TopEnd,
        };
        return new PositionResult(new OverlayRect(x, y, popupWidth, h), placement, maxHeight, 0, flippedX || flippedY);
    }

    private static double Clamp(double v, double min, double max) => max < min ? min : Math.Min(Math.Max(v, min), max);

    /// <summary>Room between the anchor (plus offset) and the viewport edge on a side.</summary>
    private static double Space(PositionRequest r, Side side) => side switch
    {
        Side.Bottom => r.Viewport.Bottom - r.Padding - (r.Anchor.Bottom + r.Offset),
        Side.Top => r.Anchor.Y - r.Offset - (r.Viewport.Y + r.Padding),
        Side.Right => r.Viewport.Right - r.Padding - (r.Anchor.Right + r.Offset),
        _ => r.Anchor.X - r.Offset - (r.Viewport.X + r.Padding),
    };

    private static double Extent(Side side, double w, double h) => side is Side.Top or Side.Bottom ? h : w;

    private static Side Opposite(Side s) => s switch
    {
        Side.Top => Side.Bottom,
        Side.Bottom => Side.Top,
        Side.Left => Side.Right,
        _ => Side.Left,
    };

    private static (double X, double Y) Place(OverlayRect a, Side side, Align align, double w, double h, double offset)
    {
        double CrossX() => align switch { Align.Start => a.X, Align.End => a.Right - w, _ => a.X + (a.Width - w) / 2 };
        double CrossY() => align switch { Align.Start => a.Y, Align.End => a.Bottom - h, _ => a.Y + (a.Height - h) / 2 };
        return side switch
        {
            Side.Bottom => (CrossX(), a.Bottom + offset),
            Side.Top => (CrossX(), a.Y - offset - h),
            Side.Right => (a.Right + offset, CrossY()),
            _ => (a.X - offset - w, CrossY()),
        };
    }

    private static (Side, Align) Split(PopoverPlacement p) => p switch
    {
        PopoverPlacement.Top => (Side.Top, Align.Center),
        PopoverPlacement.TopStart => (Side.Top, Align.Start),
        PopoverPlacement.TopEnd => (Side.Top, Align.End),
        PopoverPlacement.Bottom => (Side.Bottom, Align.Center),
        PopoverPlacement.BottomStart => (Side.Bottom, Align.Start),
        PopoverPlacement.BottomEnd => (Side.Bottom, Align.End),
        PopoverPlacement.Left => (Side.Left, Align.Center),
        PopoverPlacement.LeftStart => (Side.Left, Align.Start),
        PopoverPlacement.LeftEnd => (Side.Left, Align.End),
        PopoverPlacement.Right => (Side.Right, Align.Center),
        PopoverPlacement.RightStart => (Side.Right, Align.Start),
        PopoverPlacement.RightEnd => (Side.Right, Align.End),
        _ => throw new ArgumentOutOfRangeException(nameof(p)),
    };

    private static PopoverPlacement Join(Side s, Align a) => (s, a) switch
    {
        (Side.Top, Align.Center) => PopoverPlacement.Top,
        (Side.Top, Align.Start) => PopoverPlacement.TopStart,
        (Side.Top, Align.End) => PopoverPlacement.TopEnd,
        (Side.Bottom, Align.Center) => PopoverPlacement.Bottom,
        (Side.Bottom, Align.Start) => PopoverPlacement.BottomStart,
        (Side.Bottom, Align.End) => PopoverPlacement.BottomEnd,
        (Side.Left, Align.Center) => PopoverPlacement.Left,
        (Side.Left, Align.Start) => PopoverPlacement.LeftStart,
        (Side.Left, Align.End) => PopoverPlacement.LeftEnd,
        (Side.Right, Align.Center) => PopoverPlacement.Right,
        (Side.Right, Align.Start) => PopoverPlacement.RightStart,
        _ => PopoverPlacement.RightEnd,
    };
}
