using System.Text;

namespace Slate.Blazor.Internal;

/// <summary>Builds class lists: <c>Css.Of("sl-button").Add("sl-button--primary", isPrimary).Add(userClass)</c>.</summary>
public struct Css
{
    private StringBuilder? _sb;

    public static Css Of(string baseClass) => new Css().Add(baseClass);

    public Css Add(string? value, bool when = true)
    {
        if (!when || string.IsNullOrWhiteSpace(value)) return this;
        _sb ??= new StringBuilder();
        if (_sb.Length > 0) _sb.Append(' ');
        _sb.Append(value.Trim());
        return this;
    }

    public override string ToString() => _sb?.ToString() ?? "";

    public static implicit operator string(Css css) => css.ToString();
}

internal static class Names
{
    public static string Size(ControlSize size, string block) => size switch
    {
        ControlSize.Small => $"{block}--sm",
        ControlSize.Large => $"{block}--lg",
        _ => "",
    };

    public static string Spacing(double step) => step switch
    {
        0.5 => "0-5",
        1.5 => "1-5",
        _ => step.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };

    public static readonly double[] SpacingSteps = [0, 0.5, 1, 1.5, 2, 3, 4, 5, 6, 8, 10, 12, 16];

    public static string Theme(ThemeMode mode) => mode switch
    {
        ThemeMode.Light => "light",
        ThemeMode.Dark => "dark",
        _ => "auto",
    };

    public static string Kebab(string value) =>
        string.Concat(value.Select((c, i) => char.IsUpper(c) ? (i > 0 ? "-" : "") + char.ToLowerInvariant(c) : c.ToString()));
}
