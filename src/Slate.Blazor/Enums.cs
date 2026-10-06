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

/// <summary>Presence dot on an <c>SlAvatar</c>.</summary>
public enum AvatarStatus { Online, Away, Busy, Offline }

public enum SkeletonShape { Text, Rect, Circle }

/// <summary>One crumb of <c>SlBreadcrumbs</c>. The last item is the current page.</summary>
public sealed record BreadcrumbItem(string Label, string? Href = null, string? Icon = null);

/// <summary><c>SlBreadcrumbs.ItemClick</c> payload.</summary>
public sealed record BreadcrumbClickEventArgs(BreadcrumbItem Item, int Index);

/// <summary><c>SlMenuItem</c> activation payload (also raised on the owning <c>SlMenu.ItemSelected</c>).</summary>
public sealed record MenuSelectEventArgs(string Label, string? Value, bool? Checked);
