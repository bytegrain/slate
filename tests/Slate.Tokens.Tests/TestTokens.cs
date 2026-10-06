using Slate.Tokens.Loading;
using Slate.Tokens.Model;

namespace Slate.Tokens.Tests;

/// <summary>Helpers for building small in-memory token sets.</summary>
internal static class TestTokens
{
    public static IReadOnlyList<RawToken> Read(string json) => TokenFileReader.Read(json, "test.json");

    public static IReadOnlyList<Token> Resolve(string json) => new TokenResolver(Read(json)).ResolveAll();

    public static Token Single(string json, string path) => Resolve(json).Single(t => t.Path == path);

    public static TokenBuild Build(string primitives, params (string Name, string Json)[] themes) =>
        TokenBuild.Create(
            new TokenConfig { DefaultTheme = themes[0].Name },
            Read(primitives),
            themes.ToDictionary(t => t.Name, t => Read(t.Json)));

    public static string RepoRoot => TokenPipeline.FindRepoRoot(AppContext.BaseDirectory);

    public static TokenBuild Real => TokenBuild.Load(TokenPipeline.DefaultConfig(RepoRoot));
}
