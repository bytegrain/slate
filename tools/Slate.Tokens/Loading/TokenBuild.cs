using System.Text.Json;
using System.Text.Json.Serialization;
using Slate.Tokens.Model;

namespace Slate.Tokens.Loading;

/// <summary>slate.tokens.config.json — the entry point of the design source.</summary>
public sealed record TokenConfig
{
    public string Prefix { get; init; } = "sl";
    public List<string> Primitives { get; init; } = [];
    public Dictionary<string, string> Themes { get; init; } = new();
    public string DefaultTheme { get; init; } = "light";
    public DensityConfig? Density { get; init; }
    public Dictionary<string, string> Outputs { get; init; } = new();

    /// <summary>Optional path (relative to the config) of the shared icon set.</summary>
    public string? Icons { get; init; }

    public static TokenConfig Load(string file)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };
        return JsonSerializer.Deserialize<TokenConfig>(File.ReadAllText(file), options)
               ?? throw new TokenException($"Could not read {file}.");
    }
}

/// <summary>Density modes are alternative sets of the same sizes (e.g. size.control.compact.md / comfortable.md).</summary>
public sealed record DensityConfig
{
    /// <summary>Group that contains one sub-group per mode, e.g. "size.control".</summary>
    public string Group { get; init; } = "";
    public List<string> Modes { get; init; } = [];
    public string Default { get; init; } = "";
}

public sealed record ThemeTokens(string Name, IReadOnlyList<Token> Tokens);

/// <summary>
/// The complete, validated design source: shared (theme-independent) tokens plus one token set per theme.
/// Every theme defines exactly the same paths, so platforms can swap themes at runtime.
/// </summary>
public sealed class TokenBuild
{
    public required TokenConfig Config { get; init; }
    public required IReadOnlyList<Token> Shared { get; init; }
    public required IReadOnlyList<ThemeTokens> Themes { get; init; }
    public IconSet Icons { get; init; } = IconSet.Empty;

    public string Prefix => Config.Prefix;

    public ThemeTokens Theme(string name) =>
        Themes.FirstOrDefault(t => t.Name == name) ?? throw new TokenException($"No theme named '{name}'.");

    public ThemeTokens DefaultTheme => Theme(Config.DefaultTheme);

    /// <summary>Tokens of a density mode, keyed by the remainder of their path (e.g. "md").</summary>
    public IReadOnlyList<(string Key, Token Token)> DensityTokens(string mode)
    {
        if (Config.Density is not { } d)
            return [];
        var prefix = $"{d.Group}.{mode}.";
        return Shared.Where(t => t.Path.StartsWith(prefix, StringComparison.Ordinal))
                     .Select(t => (t.Path[prefix.Length..], t))
                     .ToList();
    }

    public static TokenBuild Load(string configFile)
    {
        var config = TokenConfig.Load(configFile);
        var root = Path.GetDirectoryName(Path.GetFullPath(configFile))!;
        var build = Create(
            config,
            config.Primitives.SelectMany(f => TokenFileReader.ReadFile(Path.Combine(root, f))).ToList(),
            config.Themes.ToDictionary(kv => kv.Key, kv => TokenFileReader.ReadFile(Path.Combine(root, kv.Value))));
        return config.Icons is { } icons
            ? new TokenBuild { Config = build.Config, Shared = build.Shared, Themes = build.Themes, Icons = IconSet.Load(Path.Combine(root, icons)) }
            : build;
    }

    public static TokenBuild Create(TokenConfig config, IReadOnlyList<RawToken> primitives, IReadOnlyDictionary<string, IReadOnlyList<RawToken>> themes)
    {
        if (themes.Count == 0)
            throw new TokenException("At least one theme is required.");
        if (!themes.ContainsKey(config.DefaultTheme))
            throw new TokenException($"Default theme '{config.DefaultTheme}' is not defined.");

        // Primitives must stand on their own: they may not reference theme tokens.
        var shared = new TokenResolver(primitives).ResolveAll();
        var primitivePaths = primitives.Select(p => p.Path).ToHashSet(StringComparer.Ordinal);

        var resolvedThemes = new List<ThemeTokens>();
        foreach (var (name, raw) in themes)
        {
            foreach (var t in raw.Where(t => primitivePaths.Contains(t.Path)))
                throw new TokenException($"Theme '{name}' redefines a shared token; themes may only add semantic tokens.", t.Path);

            var resolver = new TokenResolver(primitives.Concat(raw));
            resolvedThemes.Add(new ThemeTokens(name, raw.Select(t => resolver.Resolve(t.Path)).ToList()));
        }

        ValidateParity(resolvedThemes);
        var build = new TokenBuild { Config = config, Shared = shared, Themes = resolvedThemes };
        ValidateDensity(build);
        return build;
    }

    private static void ValidateParity(IReadOnlyList<ThemeTokens> themes)
    {
        var reference = themes[0];
        var referenceTypes = reference.Tokens.ToDictionary(t => t.Path, t => t.Type);

        foreach (var theme in themes.Skip(1))
        {
            var types = theme.Tokens.ToDictionary(t => t.Path, t => t.Type);
            var missing = referenceTypes.Keys.Except(types.Keys).ToList();
            var extra = types.Keys.Except(referenceTypes.Keys).ToList();
            if (missing.Count > 0)
                throw new TokenException($"Theme '{theme.Name}' is missing tokens defined in '{reference.Name}': {string.Join(", ", missing)}.");
            if (extra.Count > 0)
                throw new TokenException($"Theme '{theme.Name}' defines tokens not in '{reference.Name}': {string.Join(", ", extra)}.");

            foreach (var (path, type) in types.Where(kv => referenceTypes[kv.Key] != kv.Value))
                throw new TokenException($"Is {type} in '{theme.Name}' but {referenceTypes[path]} in '{reference.Name}'.", path);
        }
    }

    private static void ValidateDensity(TokenBuild build)
    {
        if (build.Config.Density is not { } d)
            return;
        if (!d.Modes.Contains(d.Default))
            throw new TokenException($"Default density '{d.Default}' is not one of the modes.");

        var keysets = d.Modes.Select(m => build.DensityTokens(m).Select(x => x.Key).Order().ToList()).ToList();
        if (keysets[0].Count == 0)
            throw new TokenException($"Density group '{d.Group}.{d.Modes[0]}' has no tokens.");
        if (keysets.Any(k => !k.SequenceEqual(keysets[0])))
            throw new TokenException($"Every density mode under '{d.Group}' must define the same sizes.");
    }
}
