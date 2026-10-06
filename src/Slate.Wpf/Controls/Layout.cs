using System.Windows;
using System.Windows.Controls;
using Slate.Layout;

namespace Slate.Wpf;

/// <summary>Spacing values are space-token steps: px = step × 4 (2 → 8px, 0.5 → 2px, 6 → 24px).</summary>
public static class Spacing
{
    public static double ToPixels(double step) => step < 0 ? throw new ArgumentOutOfRangeException(nameof(step)) : step * 4;
}

/// <summary>
/// One-dimensional layout with token spacing (docs/design/layout.md#stack). <see cref="Spacer"/> children share
/// leftover space along the main axis. With <see cref="Wrap"/>, rows/columns wrap when they run out of room.
/// </summary>
public class Stack : Panel
{
    public static readonly DependencyProperty DirectionProperty = DependencyProperty.Register(
        nameof(Direction), typeof(Direction), typeof(Stack),
        new FrameworkPropertyMetadata(Direction.Column, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty SpacingProperty = DependencyProperty.Register(
        nameof(Spacing), typeof(double), typeof(Stack),
        new FrameworkPropertyMetadata(2.0, FrameworkPropertyMetadataOptions.AffectsMeasure), v => (double)v >= 0);

    public static readonly DependencyProperty AlignProperty = DependencyProperty.Register(
        nameof(Align), typeof(StackAlign), typeof(Stack),
        new FrameworkPropertyMetadata(StackAlign.Stretch, FrameworkPropertyMetadataOptions.AffectsArrange));

    public static readonly DependencyProperty JustifyProperty = DependencyProperty.Register(
        nameof(Justify), typeof(StackJustify), typeof(Stack),
        new FrameworkPropertyMetadata(StackJustify.Start, FrameworkPropertyMetadataOptions.AffectsArrange));

    public static readonly DependencyProperty WrapProperty = DependencyProperty.Register(
        nameof(Wrap), typeof(bool), typeof(Stack),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>Column (default, vertical) or Row (horizontal).</summary>
    public Direction Direction { get => (Direction)GetValue(DirectionProperty); set => SetValue(DirectionProperty, value); }

    /// <summary>Gap between children as a space-token step (default 2 = 8px).</summary>
    public double Spacing { get => (double)GetValue(SpacingProperty); set => SetValue(SpacingProperty, value); }

    /// <summary>Cross-axis alignment (Baseline aligns like Start).</summary>
    public StackAlign Align { get => (StackAlign)GetValue(AlignProperty); set => SetValue(AlignProperty, value); }

    /// <summary>Main-axis distribution of leftover space (ignored when a Spacer is present).</summary>
    public StackJustify Justify { get => (StackJustify)GetValue(JustifyProperty); set => SetValue(JustifyProperty, value); }

    /// <summary>Wrap children onto further lines when the main axis runs out of space.</summary>
    public bool Wrap { get => (bool)GetValue(WrapProperty); set => SetValue(WrapProperty, value); }

    private bool Horizontal => Direction == Direction.Row;

    private List<UIElement> Visible() => InternalChildren.Cast<UIElement>().Where(c => c.Visibility != Visibility.Collapsed).ToList();

    private double MainOf(Size s) => Horizontal ? s.Width : s.Height;
    private double CrossOf(Size s) => Horizontal ? s.Height : s.Width;

    /// <summary>Splits children into lines (one line unless wrapping).</summary>
    private List<List<UIElement>> Lines(List<UIElement> children, double mainLimit, double gap)
    {
        var lines = new List<List<UIElement>> { new() };
        double used = 0;
        foreach (var child in children)
        {
            var size = MainOf(child.DesiredSize);
            var line = lines[^1];
            if (Wrap && line.Count > 0 && used + gap + size > mainLimit)
            {
                lines.Add(line = new());
                used = 0;
            }
            used += (line.Count > 0 ? gap : 0) + size;
            line.Add(child);
        }
        return lines;
    }

    protected override Size MeasureOverride(Size available)
    {
        var children = Visible();
        var gap = Wpf.Spacing.ToPixels(Spacing);
        var childConstraint = Horizontal
            ? new Size(Wrap ? available.Width : double.PositiveInfinity, available.Height)
            : new Size(available.Width, Wrap ? available.Height : double.PositiveInfinity);

        foreach (UIElement child in InternalChildren)
            child.Measure(childConstraint);

        var lines = Lines(children, MainOf(available), gap);
        double main = 0, cross = 0;
        foreach (var line in lines)
        {
            var lineMain = line.Sum(c => MainOf(c.DesiredSize)) + gap * Math.Max(0, line.Count - 1);
            main = Math.Max(main, lineMain);
            cross += line.Count == 0 ? 0 : line.Max(c => CrossOf(c.DesiredSize));
        }
        cross += gap * Math.Max(0, lines.Count(l => l.Count > 0) - 1);
        return Horizontal ? new Size(main, cross) : new Size(cross, main);
    }

    protected override Size ArrangeOverride(Size final)
    {
        var children = Visible();
        var gap = Wpf.Spacing.ToPixels(Spacing);
        var mainSize = MainOf(final);
        var lines = Lines(children, mainSize, gap);
        var single = lines.Count == 1;
        double crossOffsetOfLine = 0;

        foreach (var line in lines)
        {
            if (line.Count == 0) continue;
            var lineCross = single ? CrossOf(final) : line.Max(c => CrossOf(c.DesiredSize));
            ArrangeLine(line, gap, mainSize, crossOffsetOfLine, lineCross);
            crossOffsetOfLine += lineCross + gap;
        }
        return final;
    }

    private void ArrangeLine(List<UIElement> children, double gap, double mainSize, double crossStart, double crossSize)
    {
        double used = children.Sum(c => MainOf(c.DesiredSize)) + gap * Math.Max(0, children.Count - 1);
        var free = Math.Max(0, mainSize - used);
        var spacers = children.Count(c => c is Spacer);

        double position = 0, extraGap = 0;
        if (spacers == 0)
        {
            switch (Justify)
            {
                case StackJustify.Center: position = free / 2; break;
                case StackJustify.End: position = free; break;
                case StackJustify.Between when children.Count > 1: extraGap = free / (children.Count - 1); break;
            }
        }

        foreach (var child in children)
        {
            var d = child.DesiredSize;
            var length = MainOf(d) + (child is Spacer && spacers > 0 ? free / spacers : 0);
            var childCross = Align == StackAlign.Stretch ? crossSize : Math.Min(crossSize, CrossOf(d));
            var crossOffset = crossStart + Align switch
            {
                StackAlign.Center => (crossSize - childCross) / 2,
                StackAlign.End => crossSize - childCross,
                _ => 0,
            };

            child.Arrange(Horizontal
                ? new Rect(position, crossOffset, length, childCross)
                : new Rect(crossOffset, position, childCross, length));
            position += length + gap + extraGap;
        }
    }
}

/// <summary>Takes the leftover space in a <see cref="Stack"/> (pushes following items to the end).</summary>
public class Spacer : FrameworkElement
{
}

/// <summary>
/// Centres content with a token max width and responsive side gutters (16px, 24px from sm).
/// The contract's <c>MaxWidth</c> option is <see cref="ContainerMaxWidth"/> here because FrameworkElement.MaxWidth already exists.
/// </summary>
public class Container : ContentControl
{
    public static readonly DependencyProperty ContainerMaxWidthProperty = DependencyProperty.Register(
        nameof(ContainerMaxWidth), typeof(ContainerWidth), typeof(Container),
        new FrameworkPropertyMetadata(ContainerWidth.Lg, FrameworkPropertyMetadataOptions.AffectsMeasure,
            (d, e) => d.SetValue(ContentMaxWidthPropertyKey, MaxContentWidth((ContainerWidth)e.NewValue))));

    public static readonly DependencyProperty GuttersProperty = DependencyProperty.Register(
        nameof(Gutters), typeof(bool), typeof(Container), new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsMeasure));

    private static readonly DependencyPropertyKey ContentMaxWidthPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(ContentMaxWidth), typeof(double), typeof(Container), new FrameworkPropertyMetadata(SlateTokens.Container.Lg));

    public static readonly DependencyProperty ContentMaxWidthProperty = ContentMaxWidthPropertyKey.DependencyProperty;

    /// <summary>Max width of the content in px for the current <see cref="ContainerMaxWidth"/> (used by the template).</summary>
    public double ContentMaxWidth => (double)GetValue(ContentMaxWidthProperty);

    static Container()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(Container), new FrameworkPropertyMetadata(typeof(Container)));
        HorizontalContentAlignmentProperty.OverrideMetadata(typeof(Container), new FrameworkPropertyMetadata(HorizontalAlignment.Stretch));
        FocusableProperty.OverrideMetadata(typeof(Container), new FrameworkPropertyMetadata(false));
    }

    /// <summary>Sm, Md, Lg (default), Xl or Fluid (no max width).</summary>
    public ContainerWidth ContainerMaxWidth { get => (ContainerWidth)GetValue(ContainerMaxWidthProperty); set => SetValue(ContainerMaxWidthProperty, value); }

    /// <summary>Responsive side padding (default true).</summary>
    public bool Gutters { get => (bool)GetValue(GuttersProperty); set => SetValue(GuttersProperty, value); }

    public static double MaxContentWidth(ContainerWidth width) => width switch
    {
        ContainerWidth.Sm => SlateTokens.Container.Sm,
        ContainerWidth.Md => SlateTokens.Container.Md,
        ContainerWidth.Lg => SlateTokens.Container.Lg,
        ContainerWidth.Xl => SlateTokens.Container.Xl,
        _ => double.PositiveInfinity,
    };

    protected override Size MeasureOverride(Size constraint)
    {
        var pad = !Gutters ? 0 : constraint.Width >= SlateTokens.Breakpoint.Sm ? SlateTokens.Space._6 : SlateTokens.Space._4;
        Padding = new Thickness(pad, 0, pad, 0);
        return base.MeasureOverride(constraint);
    }
}

/// <summary>
/// 12-column responsive grid (docs/design/layout.md#grid). Spans are attached:
/// <c>&lt;Border sl:ResponsiveGrid.Xs="12" sl:ResponsiveGrid.Md="6" /&gt;</c>.
/// The breakpoint comes from the grid's own width (container-query style), which suits resizable desktop panes.
/// </summary>
public class ResponsiveGrid : Panel
{
    public static readonly DependencyProperty SpacingProperty = DependencyProperty.Register(
        nameof(Spacing), typeof(double), typeof(ResponsiveGrid),
        new FrameworkPropertyMetadata(4.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public double Spacing { get => (double)GetValue(SpacingProperty); set => SetValue(SpacingProperty, value); }

    private static DependencyProperty Span(string name) => DependencyProperty.RegisterAttached(
        name, typeof(int?), typeof(ResponsiveGrid),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsParentMeasure));

    public static readonly DependencyProperty XsProperty = Span("Xs");
    public static readonly DependencyProperty SmProperty = Span("Sm");
    public static readonly DependencyProperty MdProperty = Span("Md");
    public static readonly DependencyProperty LgProperty = Span("Lg");
    public static readonly DependencyProperty XlProperty = Span("Xl");

    public static int? GetXs(DependencyObject o) => (int?)o.GetValue(XsProperty);
    public static void SetXs(DependencyObject o, int? v) => o.SetValue(XsProperty, v);
    public static int? GetSm(DependencyObject o) => (int?)o.GetValue(SmProperty);
    public static void SetSm(DependencyObject o, int? v) => o.SetValue(SmProperty, v);
    public static int? GetMd(DependencyObject o) => (int?)o.GetValue(MdProperty);
    public static void SetMd(DependencyObject o, int? v) => o.SetValue(MdProperty, v);
    public static int? GetLg(DependencyObject o) => (int?)o.GetValue(LgProperty);
    public static void SetLg(DependencyObject o, int? v) => o.SetValue(LgProperty, v);
    public static int? GetXl(DependencyObject o) => (int?)o.GetValue(XlProperty);
    public static void SetXl(DependencyObject o, int? v) => o.SetValue(XlProperty, v);

    /// <summary>Breakpoint used by the last layout pass.</summary>
    public Breakpoint CurrentBreakpoint { get; private set; }

    public static GridSpan SpanOf(DependencyObject o) => new(GetXs(o), GetSm(o), GetMd(o), GetLg(o), GetXl(o));

    /// <summary>Row-major placement: each item gets (row, column, span); a row wraps when it would exceed 12.</summary>
    public static IReadOnlyList<(int Row, int Column, int Span)> Place(IEnumerable<int> spans)
    {
        var result = new List<(int, int, int)>();
        int row = 0, col = 0;
        foreach (var span in spans)
        {
            if (col + span > GridSpan.Columns)
            {
                row++;
                col = 0;
            }
            result.Add((row, col, span));
            col += span;
        }
        return result;
    }

    private List<UIElement> Items => InternalChildren.Cast<UIElement>().Where(c => c.Visibility != Visibility.Collapsed).ToList();

    private double ColumnWidth(double width, double gap) => Math.Max(0, (width - gap * (GridSpan.Columns - 1)) / GridSpan.Columns);

    protected override Size MeasureOverride(Size available)
    {
        var width = double.IsInfinity(available.Width) ? SlateTokens.Breakpoint.Lg : available.Width;
        CurrentBreakpoint = Breakpoints.FromWidth(width);
        var gap = Wpf.Spacing.ToPixels(Spacing);
        var col = ColumnWidth(width, gap);

        var items = Items;
        var placement = Place(items.Select(i => SpanOf(i).Resolve(CurrentBreakpoint)));
        var rowHeights = new Dictionary<int, double>();
        for (var i = 0; i < items.Count; i++)
        {
            var (row, _, span) = placement[i];
            items[i].Measure(new Size(col * span + gap * (span - 1), double.PositiveInfinity));
            rowHeights[row] = Math.Max(rowHeights.GetValueOrDefault(row), items[i].DesiredSize.Height);
        }
        var height = rowHeights.Values.Sum() + gap * Math.Max(0, rowHeights.Count - 1);
        return new Size(double.IsInfinity(available.Width) ? width : available.Width, height);
    }

    protected override Size ArrangeOverride(Size final)
    {
        var gap = Wpf.Spacing.ToPixels(Spacing);
        var col = ColumnWidth(final.Width, gap);
        var items = Items;
        var placement = Place(items.Select(i => SpanOf(i).Resolve(CurrentBreakpoint)));

        var rowHeights = new Dictionary<int, double>();
        for (var i = 0; i < items.Count; i++)
            rowHeights[placement[i].Row] = Math.Max(rowHeights.GetValueOrDefault(placement[i].Row), items[i].DesiredSize.Height);

        var rowTops = new Dictionary<int, double>();
        double y = 0;
        foreach (var row in rowHeights.Keys.Order())
        {
            rowTops[row] = y;
            y += rowHeights[row] + gap;
        }

        for (var i = 0; i < items.Count; i++)
        {
            var (row, column, span) = placement[i];
            items[i].Arrange(new Rect(column * (col + gap), rowTops[row], col * span + gap * (span - 1), rowHeights[row]));
        }
        return final;
    }
}

/// <summary>A hairline separator (border.default), horizontal by default.</summary>
public class Divider : Control
{
    public static readonly DependencyProperty OrientationProperty = DependencyProperty.Register(
        nameof(Orientation), typeof(Orientation), typeof(Divider), new FrameworkPropertyMetadata(Orientation.Horizontal));

    static Divider()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(Divider), new FrameworkPropertyMetadata(typeof(Divider)));
        FocusableProperty.OverrideMetadata(typeof(Divider), new FrameworkPropertyMetadata(false));
        IsTabStopProperty.OverrideMetadata(typeof(Divider), new FrameworkPropertyMetadata(false));
    }

    public Orientation Orientation { get => (Orientation)GetValue(OrientationProperty); set => SetValue(OrientationProperty, value); }
}
