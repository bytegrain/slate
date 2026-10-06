namespace Slate.Tokens.Model;

/// <summary>Thrown for any problem in the token source; always names the token path involved.</summary>
public sealed class TokenException(string message, string? path = null)
    : Exception(path is null ? message : $"{path}: {message}")
{
    public string? TokenPath { get; } = path;
}

/// <summary>A token as written in a source file, before references are resolved.</summary>
public sealed record RawToken(string Path, TokenType Type, System.Text.Json.Nodes.JsonNode Value, string? Description, string SourceFile);

/// <summary>A fully resolved token. <see cref="Value"/> is one of the value types in Values.cs, a double, or a FontFamilyValue.</summary>
public sealed record Token(string Path, TokenType Type, object Value, string? Description)
{
    public string[] Segments { get; } = Path.Split('.');

    public T As<T>() => Value is T t ? t : throw new TokenException($"Expected {typeof(T).Name} but token is {Type}.", Path);
}
