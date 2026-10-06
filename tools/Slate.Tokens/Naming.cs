using System.Text;

namespace Slate.Tokens;

/// <summary>How a token path is spelled on each platform. Kept in one place so names never drift.</summary>
public static class Naming
{
    /// <summary>color.background.canvas → --sl-color-background-canvas; space.0_5 → --sl-space-0-5.</summary>
    public static string CssVariable(string prefix, string path) =>
        $"--{prefix}-{string.Join('-', path.Split('.').Select(Kebab))}";

    /// <summary>color.text.onAccent → Sl.Color.Text.OnAccent.</summary>
    public static string XamlKey(string prefix, string path) =>
        $"{Pascal(prefix)}.{string.Join('.', path.Split('.').Select(Pascal))}";

    /// <summary>Replaces the first segment, e.g. a colour's brush: Sl.Color.Text.Primary → Sl.Brush.Text.Primary.</summary>
    public static string XamlKey(string prefix, string path, string firstSegment)
    {
        var segments = path.Split('.');
        segments[0] = firstSegment;
        return XamlKey(prefix, string.Join('.', segments));
    }

    public static string Kebab(string segment)
    {
        var sb = new StringBuilder(segment.Length + 4);
        for (var i = 0; i < segment.Length; i++)
        {
            var c = segment[i];
            if (c == '_')
                sb.Append('-');
            else if (char.IsUpper(c))
            {
                if (i > 0 && segment[i - 1] != '_')
                    sb.Append('-');
                sb.Append(char.ToLowerInvariant(c));
            }
            else
                sb.Append(c);
        }
        return sb.ToString();
    }

    public static string Pascal(string segment) =>
        segment.Length == 0 ? segment : char.ToUpperInvariant(segment[0]) + segment[1..];

    /// <summary>A valid C# identifier: PascalCase, leading digits prefixed with '_'.</summary>
    public static string CSharpIdentifier(string segment)
    {
        var sb = new StringBuilder();
        foreach (var c in Pascal(segment))
            sb.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');
        var id = sb.ToString();
        return id.Length == 0 || char.IsDigit(id[0]) ? "_" + id : id;
    }
}
