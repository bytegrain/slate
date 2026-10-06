using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Slate.Layout;

namespace Slate.Avalonia.Controls;

/// <summary>Converts space-token steps (1 step = 4px; 0.5 and 1.5 are the half steps) to pixels.</summary>
public static class SpaceScale
{
    public const double Step = 4;

    public static double ToPixels(double steps) => steps * Step;
}

public enum StackAlign
{
    Stretch,
    Start,
    Center,
    End,
}

public enum StackJustify
{
    Start,
    Center,
    End,
    SpaceBetween,
}

/// <summary>
/// One-dimensional flex layout (docs/design/layout.md#stack). <see cref="Spacing"/> is in space-token steps
/// (default 2 = 8px). <see cref="Spacer"/> children share leftover main-axis space; with <see cref="Wrap"/>,
/// children flow onto new lines instead.
/// </summary>
public class Stack : Panel
{
    public static readonly StyledProperty<Orientation> OrientationProperty =
        AvaloniaProperty.Register<Stack, Orientation>(nameof(Orientation), Orientation.Vertical);

    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<Stack, double>(nameof(Spacing), 2);

    public static readonly StyledProperty<StackAlign> AlignItemsProperty =
        AvaloniaProperty.Register<Stack, StackAlign>(nameof(AlignItems));

    public static readonly StyledProperty<StackJustify> JustifyProperty =
        AvaloniaProperty.Register<Stack, StackJustify>(nameof(Justify));

    public static readonly StyledProperty<bool> WrapProperty =
        AvaloniaProperty.Register<Stack, bool>(nameof(Wrap));

    static Stack()
    {
        AffectsMeasure<Stack>(OrientationProperty, SpacingProperty, WrapProperty);
        AffectsArrange<Stack>(AlignItemsProperty, JustifyProperty);
    }

    public Orientation Orientation { get => GetValue(OrientationProperty); set => SetValue(OrientationProperty, value); }

    /// <summary>Gap between children in space-token steps (×4px).</summary>
    public double Spacing { get => GetValue(SpacingProperty); set => SetValue(SpacingProperty, value); }

    public StackAlign AlignItems { get => GetValue(AlignItemsProperty); set => SetValue(AlignItemsProperty, value); }
    public StackJustify Justify { get => GetValue(JustifyProperty); set => SetValue(JustifyProperty, value); }
    public bool Wrap { get => GetValue(WrapProperty); set => SetValue(WrapProperty, value); }

    private bool Horizontal => Orientation == Orientation.Horizontal;
    private double Gap => SpaceScale.ToPixels(Spacing);

    private IEnumerable<Control> Visible => Children.Where(c => c.IsVisible);

    // Helpers that map (main, cross) <-> (x, y).
    private double Main(Size s) => Horizontal ? s.Width : s.Height;
    private double Cross(Size s) => Horizontal ? s.Height : s.Width;
    private Size Make(double main, double cross) => Horizontal ? new Size(main, cross) : new Size(cross, main);
    private Rect MakeRect(double main, double cross, double mainLen, double crossLen) =>
        Horizontal ? new Rect(main, cross, mainLen, crossLen) : new Rect(cross, main, crossLen, mainLen);

    protected override Size MeasureOverride(Size availableSize)
    {
        var children = Visible.ToList();
        var availMain = Main(availableSize);
        var childAvail = Make(Wrap ? availMain : double.PositiveInfinity, Cross(availableSize));

        foreach (var c in children)
            c.Measure(c is Spacer ? default : childAvail);

        if (Wrap)
        {
            var lines = Lines(children.Where(c => c is not Spacer).ToList(), availMain);
            var main = lines.Count == 0 ? 0 : lines.Max(l => l.Main);
            var cross = lines.Sum(l => l.Cross) + Gap * Math.Max(0, lines.Count - 1);
            return Make(main, cross);
        }

        var used = children.Where(c => c is not Spacer).Sum(c => Main(c.DesiredSize)) + Gap * Math.Max(0, children.Count - 1);
        var crossMax = children.Count == 0 ? 0 : children.Max(c => Cross(c.DesiredSize));
        return Make(used, crossMax);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var children = Visible.ToList();
        var finalMain = Main(finalSize);
        var finalCross = Cross(finalSize);

        if (Wrap)
        {
            var crossPos = 0.0;
            foreach (var line in Lines(children.Where(c => c is not Spacer).ToList(), finalMain))
            {
                ArrangeLine(line.Items, finalMain, crossPos, line.Cross, spacers: 0);
                crossPos += line.Cross + Gap;
            }
            foreach (var s in children.OfType<Spacer>())
                s.Arrange(default);
            return finalSize;
        }

        ArrangeLine(children, finalMain, 0, finalCross, children.Count(c => c is Spacer));
        return finalSize;
    }

    private void ArrangeLine(List<Control> items, double lineMain, double crossPos, double lineCross, int spacers)
    {
        var fixedMain = items.Where(c => c is not Spacer).Sum(c => Main(c.DesiredSize));
        var gaps = Gap * Math.Max(0, items.Count - 1);
        var free = Math.Max(0, lineMain - fixedMain - gaps);
        var spacerSize = spacers > 0 ? free / spacers : 0;

        double pos = 0, extraGap = 0;
        if (spacers == 0)
        {
            switch (Justify)
            {
                case StackJustify.Center: pos = free / 2; break;
                case StackJustify.End: pos = free; break;
                case StackJustify.SpaceBetween when items.Count > 1: extraGap = free / (items.Count - 1); break;
            }
        }

        foreach (var c in items)
        {
            var len = c is Spacer ? spacerSize : Main(c.DesiredSize);
            var childCross = Cross(c.DesiredSize);
            var (cpos, clen) = AlignItems switch
            {
                StackAlign.Start => (crossPos, childCross),
                StackAlign.Center => (crossPos + (lineCross - childCross) / 2, childCross),
                StackAlign.End => (crossPos + lineCross - childCross, childCross),
                _ => (crossPos, lineCross),
            };
            c.Arrange(MakeRect(pos, cpos, len, clen));
            pos += len + Gap + extraGap;
        }
    }

    private sealed record Line(List<Control> Items, double Main, double Cross);

    private List<Line> Lines(List<Control> items, double maxMain)
    {
        var lines = new List<Line>();
        var current = new List<Control>();
        double main = 0, cross = 0;
        foreach (var c in items)
        {
            var m = Main(c.DesiredSize);
            var next = current.Count == 0 ? m : main + Gap + m;
            if (current.Count > 0 && next > maxMain)
            {
                lines.Add(new Line(current, main, cross));
                current = [];
                main = 0;
                cross = 0;
                next = m;
            }
            current.Add(c);
            main = next;
            cross = Math.Max(cross, Cross(c.DesiredSize));
        }
        if (current.Count > 0)
            lines.Add(new Line(current, main, cross));
        return lines;
    }
}

/// <summary>A horizontal <see cref="Stack"/> for compact control rows. Use <see cref="Divider"/> (vertical) as a separator.</summary>
public class Toolbar : Stack
{
    static Toolbar()
    {
        OrientationProperty.OverrideDefaultValue<Toolbar>(Orientation.Horizontal);
        SpacingProperty.OverrideDefaultValue<Toolbar>(1);
        AlignItemsProperty.OverrideDefaultValue<Toolbar>(StackAlign.Center);
    }
}

public enum ContainerSize
{
    Sm,
    Md,
    Lg,
    Xl,
    /// <summary>No max width.</summary>
    Fluid,
}

/// <summary>
/// Centres its child with a max width from the container tokens (default Lg = 1200px) and responsive
/// side padding: 16px, or 24px from the Sm breakpoint.
/// </summary>
public class Container : Decorator
{
    public static readonly StyledProperty<ContainerSize> SizeProperty =
        AvaloniaProperty.Register<Container, ContainerSize>(nameof(Size), ContainerSize.Lg);

    static Container() => AffectsMeasure<Container>(SizeProperty);

    public ContainerSize Size { get => GetValue(SizeProperty); set => SetValue(SizeProperty, value); }

    public double MaxContentWidth => Size switch
    {
        ContainerSize.Sm => SlateTokens.Container.Sm,
        ContainerSize.Md => SlateTokens.Container.Md,
        ContainerSize.Lg => SlateTokens.Container.Lg,
        ContainerSize.Xl => SlateTokens.Container.Xl,
        _ => double.PositiveInfinity,
    };

    internal static double GutterFor(double width) =>
        width >= SlateTokens.Breakpoint.Sm ? SlateTokens.Space._6 : SlateTokens.Space._4;

    private (double Width, double Gutter) Layout(double available)
    {
        var gutter = double.IsInfinity(available) ? SlateTokens.Space._6 : GutterFor(available);
        var width = Math.Min(MaxContentWidth, Math.Max(0, available - 2 * gutter));
        return (width, gutter);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Child is null)
            return default;
        var (width, gutter) = Layout(availableSize.Width);
        Child.Measure(new Size(width, availableSize.Height));
        return new Size(Math.Min(availableSize.Width, Child.DesiredSize.Width + 2 * gutter), Child.DesiredSize.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Child is null)
            return finalSize;
        var (width, _) = Layout(finalSize.Width);
        Child.Arrange(new Rect((finalSize.Width - width) / 2, 0, width, finalSize.Height));
        return finalSize;
    }
}

/// <summary>
/// 12-column responsive grid. Set spans on children with <c>sl:ResponsiveGrid.Md="6"</c> etc.; unset
/// breakpoints inherit from smaller ones (mobile-first) and Xs defaults to 12. The breakpoint is chosen from
/// the grid's own width, so grids respond to the panel they live in, not just the window.
/// </summary>
public class ResponsiveGrid : Panel
{
    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<ResponsiveGrid, double>(nameof(Spacing), 4);

    public static readonly AttachedProperty<int?> XsProperty = AvaloniaProperty.RegisterAttached<ResponsiveGrid, Control, int?>("Xs");
    public static readonly AttachedProperty<int?> SmProperty = AvaloniaProperty.RegisterAttached<ResponsiveGrid, Control, int?>("Sm");
    public static readonly AttachedProperty<int?> MdProperty = AvaloniaProperty.RegisterAttached<ResponsiveGrid, Control, int?>("Md");
    public static readonly AttachedProperty<int?> LgProperty = AvaloniaProperty.RegisterAttached<ResponsiveGrid, Control, int?>("Lg");
    public static readonly AttachedProperty<int?> XlProperty = AvaloniaProperty.RegisterAttached<ResponsiveGrid, Control, int?>("Xl");

    static ResponsiveGrid()
    {
        AffectsMeasure<ResponsiveGrid>(SpacingProperty);
        AffectsParentMeasure<ResponsiveGrid>(XsProperty, SmProperty, MdProperty, LgProperty, XlProperty);
    }

    /// <summary>Gap in space-token steps (default 4 = 16px).</summary>
    public double Spacing { get => GetValue(SpacingProperty); set => SetValue(SpacingProperty, value); }

    public static int? GetXs(Control c) => c.GetValue(XsProperty);
    public static void SetXs(Control c, int? v) => c.SetValue(XsProperty, v);
    public static int? GetSm(Control c) => c.GetValue(SmProperty);
    public static void SetSm(Control c, int? v) => c.SetValue(SmProperty, v);
    public static int? GetMd(Control c) => c.GetValue(MdProperty);
    public static void SetMd(Control c, int? v) => c.SetValue(MdProperty, v);
    public static int? GetLg(Control c) => c.GetValue(LgProperty);
    public static void SetLg(Control c, int? v) => c.SetValue(LgProperty, v);
    public static int? GetXl(Control c) => c.GetValue(XlProperty);
    public static void SetXl(Control c, int? v) => c.SetValue(XlProperty, v);

    public static GridSpan GetSpan(Control c) => new(GetXs(c), GetSm(c), GetMd(c), GetLg(c), GetXl(c));

    /// <summary>The breakpoint used for the last layout pass.</summary>
    public Breakpoint CurrentBreakpoint { get; private set; }

    private double Gap => SpaceScale.ToPixels(Spacing);

    /// <summary>Computes the cell of every visible child for a given width: (child, column, span, row).</summary>
    internal List<(Control Child, int Column, int Span, int Row)> Place(double width)
    {
        CurrentBreakpoint = Breakpoints.FromWidth(double.IsInfinity(width) ? 0 : width);
        var result = new List<(Control, int, int, int)>();
        int row = 0, col = 0;
        foreach (var c in Children.Where(c => c.IsVisible))
        {
            var span = GetSpan(c).Resolve(CurrentBreakpoint);
            if (col + span > GridSpan.Columns)
            {
                row++;
                col = 0;
            }
            result.Add((c, col, span, row));
            col += span;
        }
        return result;
    }

    private double ColumnWidth(double width) => Math.Max(0, (width - Gap * (GridSpan.Columns - 1)) / GridSpan.Columns);

    private double CellWidth(double width, int span) => ColumnWidth(width) * span + Gap * (span - 1);

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width;
        var cells = Place(width);
        var rowHeights = new Dictionary<int, double>();
        foreach (var (child, _, span, row) in cells)
        {
            child.Measure(new Size(width > 0 ? CellWidth(width, span) : double.PositiveInfinity, double.PositiveInfinity));
            rowHeights[row] = Math.Max(rowHeights.GetValueOrDefault(row), child.DesiredSize.Height);
        }
        var height = rowHeights.Values.Sum() + Gap * Math.Max(0, rowHeights.Count - 1);
        var desiredWidth = width > 0 ? width : cells.Count == 0 ? 0 : cells.Max(c => c.Child.DesiredSize.Width);
        return new Size(desiredWidth, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var cells = Place(finalSize.Width);
        var rowHeights = cells.GroupBy(c => c.Row).ToDictionary(g => g.Key, g => g.Max(c => c.Child.DesiredSize.Height));
        var colWidth = ColumnWidth(finalSize.Width);

        var y = 0.0;
        for (var row = 0; row < rowHeights.Count; row++)
        {
            foreach (var (child, col, span, _) in cells.Where(c => c.Row == row))
                child.Arrange(new Rect(col * (colWidth + Gap), y, CellWidth(finalSize.Width, span), rowHeights[row]));
            y += rowHeights[row] + Gap;
        }
        return finalSize;
    }
}
