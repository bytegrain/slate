using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Slate.Tokens.Model;

namespace Slate.Tokens.Loading;

/// <summary>
/// Resolves <c>{path.to.token}</c> references and converts raw JSON into typed values.
/// References may point at whole tokens or be used inside composite values (shadow colours,
/// typography fields). Cycles and missing targets are reported with the full chain.
/// </summary>
public sealed partial class TokenResolver
{
    [GeneratedRegex(@"^\{([^{}]+)\}$")]
    private static partial Regex ReferencePattern();

    private readonly Dictionary<string, RawToken> _raw;
    private readonly Dictionary<string, Token> _resolved = new();
    private readonly Stack<string> _inProgress = new();

    public TokenResolver(IEnumerable<RawToken> tokens)
    {
        _raw = new Dictionary<string, RawToken>(StringComparer.Ordinal);
        foreach (var token in tokens)
        {
            if (!_raw.TryAdd(token.Path, token))
                throw new TokenException(
                    $"Defined twice ({_raw[token.Path].SourceFile} and {token.SourceFile}).", token.Path);
        }
    }

    /// <summary>Resolves every token, preserving source order.</summary>
    public IReadOnlyList<Token> ResolveAll() => _raw.Keys.Select(Resolve).ToList();

    public Token Resolve(string path)
    {
        if (_resolved.TryGetValue(path, out var done))
            return done;

        if (!_raw.TryGetValue(path, out var raw))
            throw new TokenException($"Reference to unknown token '{path}'.", _inProgress.TryPeek(out var from) ? from : null);

        if (_inProgress.Contains(path))
        {
            var chain = string.Join(" -> ", _inProgress.Reverse().SkipWhile(p => p != path).Append(path));
            throw new TokenException($"Circular reference: {chain}.", path);
        }

        _inProgress.Push(path);
        try
        {
            var value = ResolveValue(raw.Value, raw.Type, path);
            var token = new Token(path, raw.Type, value, raw.Description, TryReference(raw.Value, out var alias) ? alias : null);
            _resolved[path] = token;
            return token;
        }
        finally
        {
            _inProgress.Pop();
        }
    }

    private object ResolveValue(JsonNode node, TokenType type, string path)
    {
        if (TryReference(node, out var target))
        {
            var referenced = Resolve(target);
            if (referenced.Type != type)
                throw new TokenException($"Is {type} but references '{target}' which is {referenced.Type}.", path);
            return referenced.Value;
        }

        return type switch
        {
            TokenType.Color => ColorValue.Parse(String(node, path), path),
            TokenType.Dimension => Dimension(node, path),
            TokenType.Duration => DurationValue.Parse(String(node, path), path),
            TokenType.Number => Number(node, path),
            TokenType.FontWeight => FontWeight(node, path),
            TokenType.FontFamily => FontFamily(node, path),
            TokenType.CubicBezier => CubicBezier(node, path),
            TokenType.Shadow => Shadow(node, path),
            TokenType.Typography => Typography(node, path),
            _ => throw new TokenException($"Unsupported type {type}.", path),
        };
    }

    /// <summary>Resolves a field inside a composite: either a reference to a token of the expected type, or a literal.</summary>
    private object Field(JsonObject obj, string name, TokenType type, string path, out string? referencedPath)
    {
        referencedPath = null;
        var node = obj[name] ?? throw new TokenException($"Missing '{name}'.", path);
        if (TryReference(node, out var target))
        {
            referencedPath = target;
            var referenced = Resolve(target);
            if (referenced.Type != type)
                throw new TokenException($"'{name}' references '{target}' which is {referenced.Type}, expected {type}.", path);
            return referenced.Value;
        }

        return ResolveValue(node, type, $"{path}.{name}");
    }

    private T Field<T>(JsonObject obj, string name, TokenType type, string path) => (T)Field(obj, name, type, path, out _);

    private static bool TryReference(JsonNode node, out string target)
    {
        target = "";
        if (node is not JsonValue v || !v.TryGetValue<string>(out var s))
            return false;

        var match = ReferencePattern().Match(s.Trim());
        if (!match.Success)
            return false;

        target = match.Groups[1].Value;
        return true;
    }

    private static string String(JsonNode node, string path) =>
        node is JsonValue v && v.TryGetValue<string>(out var s) ? s : throw new TokenException("Expected a string value.", path);

    private static DimensionValue Dimension(JsonNode node, string path)
    {
        if (node is JsonValue v && v.TryGetValue<double>(out var number))
            return number == 0 ? new DimensionValue(0, "px") : throw new TokenException("Dimensions need a unit (only 0 may be unitless).", path);
        return DimensionValue.Parse(String(node, path), path);
    }

    private static double Number(JsonNode node, string path) =>
        node is JsonValue v && v.TryGetValue<double>(out var d) ? d : throw new TokenException("Expected a number.", path);

    private static double FontWeight(JsonNode node, string path)
    {
        if (node is JsonValue v && v.TryGetValue<double>(out var d))
            return d is >= 1 and <= 1000 ? d : throw new TokenException($"Font weight {d} must be 1-1000.", path);

        return String(node, path) switch
        {
            "thin" => 100, "extra-light" => 200, "light" => 300, "normal" or "regular" => 400,
            "medium" => 500, "semi-bold" => 600, "bold" => 700, "extra-bold" => 800, "black" => 900,
            var other => throw new TokenException($"Unknown font weight '{other}'.", path),
        };
    }

    private static FontFamilyValue FontFamily(JsonNode node, string path) => node switch
    {
        JsonArray arr when arr.Count > 0 => new FontFamilyValue(arr.Select(n => String(n!, path)).ToList()),
        JsonValue => new FontFamilyValue([String(node, path)]),
        _ => throw new TokenException("Font family must be a string or a non-empty array of strings.", path),
    };

    private static CubicBezierValue CubicBezier(JsonNode node, string path)
    {
        if (node is not JsonArray { Count: 4 } arr)
            throw new TokenException("Cubic bezier must be an array of four numbers.", path);

        var p = arr.Select(n => Number(n!, path)).ToArray();
        if (p[0] is < 0 or > 1 || p[2] is < 0 or > 1)
            throw new TokenException("Cubic bezier x values must be within 0-1.", path);

        return new CubicBezierValue(p[0], p[1], p[2], p[3]);
    }

    private ShadowValue Shadow(JsonNode node, string path)
    {
        var layers = node switch
        {
            JsonArray arr => arr.Select(n => n as JsonObject ?? throw new TokenException("Shadow layers must be objects.", path)).ToList(),
            JsonObject obj => [obj],
            _ => throw new TokenException("Shadow must be an object or array of objects.", path),
        };

        return new ShadowValue(layers.Select((layer, i) =>
        {
            var p = $"{path}[{i}]";
            var inset = layer["inset"] is JsonValue iv && iv.GetValue<bool>();
            return new ShadowLayer(
                Field<ColorValue>(layer, "color", TokenType.Color, p),
                Field<DimensionValue>(layer, "offsetX", TokenType.Dimension, p),
                Field<DimensionValue>(layer, "offsetY", TokenType.Dimension, p),
                Field<DimensionValue>(layer, "blur", TokenType.Dimension, p),
                layer.ContainsKey("spread") ? Field<DimensionValue>(layer, "spread", TokenType.Dimension, p) : new DimensionValue(0, "px"),
                inset);
        }).ToList());
    }

    private TypographyValue Typography(JsonNode node, string path)
    {
        if (node is not JsonObject obj)
            throw new TokenException("Typography must be an object.", path);

        var family = (FontFamilyValue)Field(obj, "fontFamily", TokenType.FontFamily, path, out var familyRef);
        var size = Field<DimensionValue>(obj, "fontSize", TokenType.Dimension, path);
        var weight = Field<double>(obj, "fontWeight", TokenType.FontWeight, path);
        var lineHeight = Field<double>(obj, "lineHeight", TokenType.Number, path);
        var tracking = obj.ContainsKey("letterSpacing")
            ? Field<DimensionValue>(obj, "letterSpacing", TokenType.Dimension, path)
            : new DimensionValue(0, "em");

        if (!size.IsPixels)
            throw new TokenException("Typography fontSize must be in px.", path);
        if (lineHeight is <= 0 or > 4)
            throw new TokenException(string.Create(CultureInfo.InvariantCulture, $"Line height {lineHeight} should be a unitless multiplier."), path);

        return new TypographyValue(family, familyRef, size, weight, lineHeight, tracking);
    }
}
