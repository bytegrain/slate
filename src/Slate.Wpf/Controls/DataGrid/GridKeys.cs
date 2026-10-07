namespace Slate.Wpf;

/// <summary>Resource keys the grid's code-built views use (checked against the token dictionaries by ResourceTests).</summary>
internal static class GridKeys
{
    public const string Background = "Sl.Component.Grid.Background.Brush";
    public const string HeaderBackground = "Sl.Component.Grid.HeaderBackground.Brush";
    public const string HeaderText = "Sl.Component.Grid.HeaderText.Brush";
    public const string Border = "Sl.Component.Grid.Border.Brush";
    public const string RowHover = "Sl.Component.Grid.RowHover.Brush";
    public const string RowSelected = "Sl.Component.Grid.RowSelected.Brush";
    public const string SelectionIndicator = "Sl.Component.Grid.SelectionIndicator.Brush";
    public const string ActiveCell = "Sl.Component.Grid.ActiveCell.Brush";
    public const string GroupBackground = "Sl.Component.Grid.GroupBackground.Brush";
    public const string CellPadding = "Sl.Component.Grid.CellPaddingX";
    public const string Radius = "Sl.Component.Grid.Radius.Corner";
    public const string Stripe = "Sl.Brush.Background.Subtle";
    public const string Text = "Sl.Brush.Text.Primary";
    public const string TextSecondary = "Sl.Brush.Text.Secondary";
    public const string TextTertiary = "Sl.Brush.Text.Tertiary";
    public const string Accent = "Sl.Brush.Accent.Default";
    public const string AccentText = "Sl.Brush.Accent.Text";
    public const string Track = "Sl.Component.Progress.Track.Brush";
    public const string Fill = "Sl.Component.Progress.Fill.Brush";
    public const string Danger = "Sl.Brush.Status.Danger.Fg";
    public const string DangerBg = "Sl.Brush.Status.Danger.Bg";
    public const string Warning = "Sl.Brush.Status.Warning.Fg";
    public const string Skeleton = "Sl.Component.Skeleton.Background.Brush";
    public const string Surface = "Sl.Brush.Background.Surface";
    public const string Raised = "Sl.Brush.Background.Raised";
    public const string Muted = "Sl.Brush.Background.Muted";
    public const string Scrim = "Sl.Brush.Scrim";
    public const string UiFont = "Sl.Font.Family.Ui";
    public const string MonoFont = "Sl.Font.Family.Mono";
    public const string FontSize = "Sl.Font.Size.Sm";
    public const string SmallFontSize = "Sl.Font.Size.Xs";
    public const string RowHeight = "Sl.Size.Control.Md";
    public const string HeaderHeight = "Sl.Size.Control.Lg";
    public const string PopoverRadius = "Sl.Component.Popover.Radius.Corner";
    public const string PopoverShadow = "Sl.Effect.E2";

    /// <summary>Background tint key for a row/cell tone.</summary>
    public static string ToneBackground(Tone tone)
    {
        var name = tone.ToString();
        return tone switch
        {
            Tone.Accent => "Sl.Brush.Accent.Subtle",
            Tone.Neutral => "Sl.Brush.Background.Muted",
            _ => $"Sl.Brush.Status.{name}.Bg",
        };
    }

    public static string ToneForeground(Tone tone)
    {
        var name = tone.ToString();
        return tone switch
        {
            Tone.Accent => "Sl.Brush.Accent.Text",
            Tone.Neutral => "Sl.Brush.Text.Primary",
            _ => $"Sl.Brush.Status.{name}.Fg",
        };
    }

    /// <summary>Solid fill for a tone (progress bars).</summary>
    public static string ToneSolid(Tone tone)
    {
        var name = tone.ToString();
        return tone switch
        {
            Tone.Accent => Accent,
            Tone.Neutral => TextTertiary,
            _ => $"Sl.Brush.Status.{name}.Solid",
        };
    }
}
