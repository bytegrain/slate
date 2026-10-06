using Slate.Dialogs;
using Slate.Snackbars;

namespace Slate;

// The shared configuration vocabulary (docs/design/configurability.md). Every platform exposes these
// with the same names and values; web uses the lower-case kebab spelling of the same words.

/// <summary>Colour role of a component. Messages use <see cref="Severity"/>; <see cref="SeverityExtensions.ToTone"/> maps between them.</summary>
public enum Tone
{
    Neutral,
    Accent,
    Success,
    Warning,
    Danger,
    Info,
}

/// <summary>Button chrome. Combine with <see cref="Tone"/>: the primary action is <c>Solid + Accent</c>.</summary>
public enum ButtonVariant
{
    /// <summary>Surface fill, hairline border and the milled edge. The default.</summary>
    Outlined,
    /// <summary>Filled with the tone colour.</summary>
    Solid,
    /// <summary>Tinted with the tone's subtle colour, no border.</summary>
    Soft,
    /// <summary>No chrome until hovered.</summary>
    Ghost,
    /// <summary>Inline text link.</summary>
    Link,
}

/// <summary>Text-field chrome.</summary>
public enum FieldVariant
{
    /// <summary>Recessed box with a border (Alloy default).</summary>
    Outlined,
    /// <summary>Muted fill, border only on hover/focus.</summary>
    Filled,
    /// <summary>Bottom border only — for dense forms and inline editing.</summary>
    Underlined,
}

public enum CardVariant
{
    /// <summary>Surface + shadow (Alloy default).</summary>
    Elevated,
    /// <summary>Surface + hairline border, no shadow.</summary>
    Outlined,
    /// <summary>No border or shadow — groups content on a subtle background.</summary>
    Flat,
}

/// <summary>Per-component corner radius override. <see cref="Default"/> uses the component token.</summary>
public enum Radius
{
    Default,
    None,
    Small,
    Medium,
    Large,
    Full,
}

/// <summary>Which side something sits on, in reading order (labels next to switches, icons in buttons…).</summary>
public enum Placement
{
    Start,
    End,
}

public static class SeverityExtensions
{
    public static Tone ToTone(this Severity severity) => severity switch
    {
        Severity.Info => Tone.Info,
        Severity.Success => Tone.Success,
        Severity.Warning => Tone.Warning,
        Severity.Error => Tone.Danger,
        _ => Tone.Neutral,
    };
}

public static class RadiusExtensions
{
    /// <summary>The radius token path for an override, or null for <see cref="Radius.Default"/>.</summary>
    public static string? TokenPath(this Radius radius) => radius switch
    {
        Radius.None => null,
        Radius.Small => "radius.sm",
        Radius.Medium => "radius.md",
        Radius.Large => "radius.lg",
        Radius.Full => "radius.full",
        _ => null,
    };

    /// <summary>Pixel value for an override (None = 0), or null for <see cref="Radius.Default"/>.</summary>
    public static double? Pixels(this Radius radius) => radius switch
    {
        Radius.None => 0,
        Radius.Small => SlateTokens.Radius.Sm,
        Radius.Medium => SlateTokens.Radius.Md,
        Radius.Large => SlateTokens.Radius.Lg,
        Radius.Full => SlateTokens.Radius.Full,
        _ => null,
    };
}

public sealed class ButtonDefaults
{
    public ButtonVariant Variant { get; set; } = ButtonVariant.Outlined;
    public Tone Tone { get; set; } = Tone.Neutral;
    public ControlSize Size { get; set; } = ControlSize.Medium;
    public Radius Radius { get; set; } = Radius.Default;
}

public sealed class FieldDefaults
{
    public FieldVariant Variant { get; set; } = FieldVariant.Outlined;
    public ControlSize Size { get; set; } = ControlSize.Medium;
    public Radius Radius { get; set; } = Radius.Default;
}

public sealed class CardDefaults
{
    public CardVariant Variant { get; set; } = CardVariant.Elevated;
    public Radius Radius { get; set; } = Radius.Default;
}

public sealed class SelectionDefaults
{
    /// <summary>Label side for checkboxes, radios and switches.</summary>
    public Placement LabelPlacement { get; set; } = Placement.End;
}

/// <summary>
/// App-wide component defaults (like MudBlazor's global settings). Set once at startup via each platform's
/// <c>AddSlate(o => o.Defaults…)</c>; any parameter set on an individual component still wins.
/// </summary>
public sealed class SlateDefaults
{
    public ButtonDefaults Button { get; } = new();
    public FieldDefaults Field { get; } = new();
    public CardDefaults Card { get; } = new();
    public SelectionDefaults Selection { get; } = new();

    /// <summary>Defaults for every dialog opened through the dialog service.</summary>
    public DialogOptions Dialog { get; set; } = new();

    /// <summary>Snackbar host behaviour (position, limits, durations).</summary>
    public SnackbarConfiguration Snackbar { get; set; } = new();
}

public enum BadgeVariant
{
    Soft,
    Solid,
    Outlined,
}

public enum AlertVariant
{
    /// <summary>Tinted background and border (Alloy default).</summary>
    Soft,
    /// <summary>Surface background with a tone border.</summary>
    Outlined,
    /// <summary>Filled with the tone's solid colour.</summary>
    Solid,
}

public enum DrawerVariant
{
    /// <summary>Persistent from the responsive breakpoint up, temporary below it.</summary>
    Responsive,
    /// <summary>Docked beside the content, pushing it.</summary>
    Persistent,
    /// <summary>Overlays the content with a scrim; closes on scrim click or Escape.</summary>
    Temporary,
    /// <summary>A narrow icon rail; labels become tooltips.</summary>
    Mini,
}

public enum Direction
{
    Row,
    Column,
}

public enum ContainerWidth
{
    Sm,
    Md,
    Lg,
    Xl,
    /// <summary>No max width.</summary>
    Fluid,
}

public enum StackAlign
{
    Stretch,
    Start,
    Center,
    End,
    Baseline,
}

public enum StackJustify
{
    Start,
    Center,
    End,
    Between,
}

// ---- Data grid (docs/design/data-grid.md) ----

public enum GridSelectionMode { None, Single, Multi }

public enum GridColumnType { Text, Number, Date, Boolean, Enum, Progress, Sparkline, Actions, Custom }

public enum SortDirection { Ascending, Descending }

public enum GridPin { None, Start, End }

public enum GridAggregate { None, Sum, Avg, Min, Max, Count }

public enum GridPagination { None, Pages, Infinite }

// ---- Overlays & wave-2 components ----

public enum PopoverPlacement
{
    Top, TopStart, TopEnd,
    Bottom, BottomStart, BottomEnd,
    Left, LeftStart, LeftEnd,
    Right, RightStart, RightEnd,
}

public enum TabsVariant { Line, Pills, Enclosed }

public enum SelectionKind { Single, Multiple }

public enum DateSelection { Single, Range }

public enum TreeSelectionMode { None, Single, Multi, Checkbox }
