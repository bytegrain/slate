using Slate.Tokens.Loading;
using Slate.Tokens.Model;

namespace Slate.Tokens.Emitters;

/// <summary>
/// tokens.css — custom properties for the web and Blazor.
/// Theme: light by default; <c>data-sl-theme="dark"</c> on any element switches its subtree;
/// <c>data-sl-theme="auto"</c> follows the OS. Density works the same way with <c>data-sl-density</c>.
/// </summary>
public sealed class CssEmitter : IEmitter
{
    public string Name => "css";

    public IReadOnlyList<GeneratedFile> Emit(TokenBuild build)
    {
        var p = build.Prefix;
        var w = new Writer();
        w.Line($"/* {EmitterRegistry.Banner} */").Line();

        w.Line(":root {").Indent();
        foreach (var token in build.Shared)
            Declare(w, p, token);

        if (build.Config.Density is { } density)
        {
            w.Line().Line($"/* density: {density.Default} (default) */");
            DensityAliases(w, build, density, density.Default);
        }
        w.Outdent().Line("}").Line();

        if (build.Config.Density is { } d)
        {
            foreach (var mode in d.Modes)
            {
                w.Line($"[data-{p}-density=\"{mode}\"] {{").Indent();
                DensityAliases(w, build, d, mode);
                w.Outdent().Line("}").Line();
            }
        }

        var defaultTheme = build.DefaultTheme;
        w.Line($":root,\n[data-{p}-theme=\"{defaultTheme.Name}\"] {{").Indent();
        ThemeBody(w, p, defaultTheme);
        w.Outdent().Line("}").Line();

        foreach (var theme in build.Themes.Where(t => t != defaultTheme))
        {
            w.Line($"[data-{p}-theme=\"{theme.Name}\"] {{").Indent();
            ThemeBody(w, p, theme);
            w.Outdent().Line("}").Line();

            if (theme.Name == "dark")
            {
                w.Line("@media (prefers-color-scheme: dark) {").Indent();
                w.Line($"[data-{p}-theme=\"auto\"] {{").Indent();
                ThemeBody(w, p, theme);
                w.Outdent().Line("}");
                w.Outdent().Line("}").Line();
            }
        }

        return [new GeneratedFile("tokens.css", w.ToString().TrimEnd('\n') + "\n")];
    }

    private static void ThemeBody(Writer w, string prefix, ThemeTokens theme)
    {
        w.Line($"color-scheme: {(theme.Name == "dark" ? "dark" : "light")};");
        foreach (var token in theme.Tokens)
            Declare(w, prefix, token);
    }

    private static void DensityAliases(Writer w, TokenBuild build, DensityConfig density, string mode)
    {
        foreach (var (key, token) in build.DensityTokens(mode))
        {
            var alias = Naming.CssVariable(build.Prefix, $"{density.Group}.{key}");
            w.Line($"{alias}: var({Naming.CssVariable(build.Prefix, token.Path)});");
        }
    }

    private static void Declare(Writer w, string prefix, Token token)
    {
        var name = Naming.CssVariable(prefix, token.Path);
        if (token.Value is TypographyValue t)
        {
            var family = t.FontFamilyRef is { } r ? $"var({Naming.CssVariable(prefix, r)})" : t.FontFamily.ToCss();
            w.Line($"{name}-font-family: {family};");
            w.Line($"{name}-font-size: {t.FontSize};");
            w.Line($"{name}-font-weight: {Format.Number(t.FontWeight)};");
            w.Line($"{name}-line-height: {Format.Number(t.LineHeight)};");
            w.Line($"{name}-letter-spacing: {t.LetterSpacing};");
            w.Line($"{name}: {Format.Number(t.FontWeight)} {t.FontSize}/{Format.Number(t.LineHeight)} {family};");
            return;
        }

        // Component tokens stay linked to what they alias, so overriding e.g. --sl-radius-md at runtime
        // also restyles every component that uses it.
        if (token.AliasOf is { } alias && token.Path.StartsWith("component.", StringComparison.Ordinal))
        {
            w.Line($"{name}: var({Naming.CssVariable(prefix, alias)});");
            return;
        }

        w.Line($"{name}: {Value(token)};");
    }

    public static string Value(Token token) => token.Value switch
    {
        ColorValue c => c.ToCssHex(),
        DimensionValue d => d.ToString(),
        DurationValue d => d.ToString(),
        CubicBezierValue b => b.ToString(),
        FontFamilyValue f => f.ToCss(),
        ShadowValue s => s.ToCss(),
        double n => Format.Number(n),
        _ => throw new TokenException($"Cannot express {token.Type} in CSS.", token.Path),
    };
}
