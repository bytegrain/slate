namespace Slate;

/// <summary>A named text style from the design tokens (see <see cref="SlateTokens.Typography"/>).</summary>
/// <param name="FontFamily">Comma-separated family list, most preferred first.</param>
/// <param name="FontSize">Pixels.</param>
/// <param name="FontWeight">100-900.</param>
/// <param name="LineHeight">Unitless multiplier of <paramref name="FontSize"/>.</param>
/// <param name="LetterSpacingEm">Tracking in em.</param>
public sealed record SlateTextStyle(string FontFamily, double FontSize, double FontWeight, double LineHeight, double LetterSpacingEm)
{
    /// <summary>Line height in pixels.</summary>
    public double LineHeightPixels => Math.Round(FontSize * LineHeight, 2);

    /// <summary>Letter spacing in pixels.</summary>
    public double LetterSpacingPixels => Math.Round(FontSize * LetterSpacingEm, 3);
}

/// <summary>Which colour theme an application or region uses.</summary>
public enum ThemeMode
{
    /// <summary>Follow the operating system / browser preference.</summary>
    System,
    Light,
    Dark,
}

/// <summary>Control sizing. Compact is Alloy's default; comfortable is better for touch and data entry.</summary>
public enum Density
{
    Compact,
    Comfortable,
}

/// <summary>Visual intent shared by alerts, snackbars, badges and dialogs.</summary>
public enum Severity
{
    Normal,
    Info,
    Success,
    Warning,
    Error,
}

/// <summary>Component size, mapped to the control height tokens of the active density.</summary>
public enum ControlSize
{
    Small,
    Medium,
    Large,
}

public static class ThemeNames
{
    public const string Light = "light";
    public const string Dark = "dark";

    /// <summary>Resolves <see cref="ThemeMode.System"/> using the platform's current preference.</summary>
    public static string Resolve(ThemeMode mode, bool systemPrefersDark) => mode switch
    {
        ThemeMode.Light => Light,
        ThemeMode.Dark => Dark,
        _ => systemPrefersDark ? Dark : Light,
    };
}
