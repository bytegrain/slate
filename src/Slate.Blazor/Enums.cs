namespace Slate.Blazor;

/// <summary>Text styles from the typography tokens.</summary>
public enum Typo { Display, H1, H2, H3, Title, Body, BodyStrong, Label, Caption, Overline, Mono }

public enum TextTone { Primary, Secondary, Tertiary, Accent, Success, Warning, Danger, Info }

public enum BadgeTone { Neutral, Info, Success, Warning, Danger, Accent }

public enum DrawerVariant
{
    /// <summary>Persistent from 900px up, temporary (overlay) below. The default.</summary>
    Responsive,
    /// <summary>Pushes content; closing collapses it.</summary>
    Persistent,
    /// <summary>Overlays content with a scrim.</summary>
    Temporary,
    /// <summary>56px icon rail that expands when open.</summary>
    Mini,
}

public enum ContainerWidth { Sm, Md, Lg, Xl, Fluid }

public enum StackDirection { Column, Row }

public enum StackAlign { Start, Center, End, Stretch, Baseline }

public enum StackJustify { Start, Center, End, Between }

public enum LabelPosition { Start, End }

public enum ButtonType { Button, Submit, Reset }
