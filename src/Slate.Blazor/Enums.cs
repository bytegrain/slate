namespace Slate.Blazor;

// Blazor-only vocabulary. Everything shared across platforms (Tone, ButtonVariant, FieldVariant, CardVariant,
// BadgeVariant, AlertVariant, Radius, Placement, DrawerVariant, Direction, ContainerWidth, StackAlign,
// StackJustify, ControlSize, Severity…) comes from Slate.Core.

/// <summary>Text styles from the typography tokens.</summary>
public enum Typo { Display, H1, H2, H3, Title, Body, BodyStrong, Label, Caption, Overline, Mono }

public enum TextTone { Primary, Secondary, Tertiary, Accent, Success, Warning, Danger, Info }

public enum ButtonType { Button, Submit, Reset }

/// <summary>Badge sizes (badges have no large size).</summary>
public enum BadgeSize { Small, Medium }
