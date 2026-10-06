using System.Text.Json;
using System.Text.Json.Nodes;
using Slate.Tokens.Model;

namespace Slate.Tokens.Loading;

/// <summary>
/// Reads a DTCG (Design Tokens Community Group) JSON document into a flat list of raw tokens.
/// Groups may set <c>$type</c> for their descendants; keys starting with <c>$</c> are metadata.
/// </summary>
public static class TokenFileReader
{
    private static readonly JsonDocumentOptions Options = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static IReadOnlyList<RawToken> ReadFile(string file) =>
        Read(File.ReadAllText(file), Path.GetFileName(file));

    public static IReadOnlyList<RawToken> Read(string json, string sourceName)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json, documentOptions: Options);
        }
        catch (JsonException ex)
        {
            throw new TokenException($"Invalid JSON in {sourceName}: {ex.Message}");
        }

        if (root is not JsonObject obj)
            throw new TokenException($"{sourceName} must contain a JSON object at the root.");

        var tokens = new List<RawToken>();
        Walk(obj, prefix: "", inheritedType: null, sourceName, tokens);
        return tokens;
    }

    private static void Walk(JsonObject group, string prefix, TokenType? inheritedType, string source, List<RawToken> output)
    {
        var groupType = inheritedType;
        if (group["$type"] is JsonValue typeNode)
            groupType = TokenTypes.Parse(typeNode.GetValue<string>(), prefix.Length == 0 ? source : prefix);

        foreach (var (key, node) in group)
        {
            if (key.StartsWith('$'))
                continue;

            if (key.Contains('.') || key.Contains('{') || key.Contains('}'))
                throw new TokenException($"Name '{key}' may not contain '.', '{{' or '}}'.", Join(prefix, key));

            var path = Join(prefix, key);
            if (node is not JsonObject child)
                throw new TokenException("Expected a group or token object.", path);

            if (child.ContainsKey("$value"))
            {
                var type = child["$type"] is JsonValue t ? TokenTypes.Parse(t.GetValue<string>(), path) : groupType;
                if (type is null)
                    throw new TokenException("Token has no $type (set it on the token or a parent group).", path);

                var description = child["$description"]?.GetValue<string>();
                output.Add(new RawToken(path, type.Value, child["$value"]!.DeepClone(), description, source));
            }
            else
            {
                Walk(child, path, groupType, source, output);
            }
        }
    }

    private static string Join(string prefix, string key) => prefix.Length == 0 ? key : $"{prefix}.{key}";
}
