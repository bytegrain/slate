using System.Globalization;
using System.Text;

namespace Slate.Blazor.Internal;

/// <summary>Builds class lists: <c>Css.Of("sl-button").Add("sl-button--solid").Add(userClass)</c>.</summary>
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

/// <summary>Class names from docs/design/css-classes.md, in one place.</summary>
internal static class Names
{
    /// <summary>Size modifier, only for non-default sizes: <c>sl-button--small</c> / <c>sl-button--large</c>.</summary>
    public static string? Size(ControlSize size, string block) => size switch
    {
        ControlSize.Small => $"{block}--small",
        ControlSize.Large => $"{block}--large",
        _ => null,
    };

    /// <summary>Icons keep their short size modifiers (<c>sl-icon--sm</c> / <c>sl-icon--lg</c>).</summary>
    public static string? IconSize(ControlSize size) => size switch
    {
        ControlSize.Small => "sl-icon--sm",
        ControlSize.Large => "sl-icon--lg",
        _ => null,
    };

    /// <summary>Tone class, always emitted (<c>sl-tone-neutral</c> …).</summary>
    public static string Tone(Tone tone) => $"sl-tone-{Kebab(tone.ToString())}";

    /// <summary>Radius override class, omitted for <see cref="Radius.Default"/>.</summary>
    public static string? Radius(Radius radius) => radius == Slate.Radius.Default ? null : $"sl-radius-{Kebab(radius.ToString())}";

    public static string Variant(Enum variant, string block) => $"{block}--{Kebab(variant.ToString())}";

    public static string Spacing(double step) => step switch
    {
        0.5 => "0-5",
        1.5 => "1-5",
        _ => step.ToString(CultureInfo.InvariantCulture),
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

    /// <summary>Token path → CSS custom property, matching the generator: <c>color.text.onAccent</c> → <c>--sl-color-text-on-accent</c>.</summary>
    public static string CssVariable(string path) =>
        "--sl-" + string.Join('-', path.Split('.').Select(segment =>
        {
            var sb = new StringBuilder();
            for (var i = 0; i < segment.Length; i++)
            {
                var c = segment[i];
                if (c == '_') sb.Append('-');
                else if (char.IsUpper(c))
                {
                    if (i > 0 && segment[i - 1] != '_') sb.Append('-');
                    sb.Append(char.ToLowerInvariant(c));
                }
                else sb.Append(c);
            }
            return sb.ToString();
        }));

    public static string Number(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
