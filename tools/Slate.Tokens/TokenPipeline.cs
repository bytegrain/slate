using Slate.Tokens.Emitters;
using Slate.Tokens.Loading;
using Slate.Tokens.Model;

namespace Slate.Tokens;

public sealed record PipelineResult(IReadOnlyList<string> Written, IReadOnlyList<string> Stale, IReadOnlyList<string> Unchanged);

/// <summary>Loads the design source, runs every configured emitter and writes (or checks) the outputs.</summary>
public static class TokenPipeline
{
    /// <param name="configFile">Path to slate.tokens.config.json.</param>
    /// <param name="check">When true nothing is written; outputs that differ are reported as stale.</param>
    public static PipelineResult Run(string configFile, bool check, IReadOnlyList<IEmitter>? emitters = null)
    {
        var build = TokenBuild.Load(configFile);
        var configDir = Path.GetDirectoryName(Path.GetFullPath(configFile))!;
        emitters ??= EmitterRegistry.All;

        var unknown = build.Config.Outputs.Keys.Except(emitters.Select(e => e.Name)).ToList();
        if (unknown.Count > 0)
            throw new TokenException($"Unknown output(s) in config: {string.Join(", ", unknown)}.");

        List<string> written = [], stale = [], unchanged = [];
        foreach (var emitter in emitters)
        {
            if (!build.Config.Outputs.TryGetValue(emitter.Name, out var outDir))
                continue;

            foreach (var file in emitter.Emit(build))
            {
                var target = Path.GetFullPath(Path.Combine(configDir, outDir, file.RelativePath));
                var current = File.Exists(target) ? File.ReadAllText(target).Replace("\r\n", "\n") : null;

                if (current == file.Content)
                {
                    unchanged.Add(target);
                }
                else if (check)
                {
                    stale.Add(target);
                }
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.WriteAllText(target, file.Content);
                    written.Add(target);
                }
            }
        }

        return new PipelineResult(written, stale, unchanged);
    }

    /// <summary>Walks up from <paramref name="start"/> to the repository root (the folder holding global.json and design/).</summary>
    public static string FindRepoRoot(string start)
    {
        for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "global.json")) && Directory.Exists(Path.Combine(dir.FullName, "design")))
                return dir.FullName;
        }
        throw new DirectoryNotFoundException($"Could not find the Slate repository root above {start}.");
    }

    public static string DefaultConfig(string repoRoot) => Path.Combine(repoRoot, "design", "tokens", "slate.tokens.config.json");
}
