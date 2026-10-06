using Slate.Tokens;
using Slate.Tokens.Model;

// slate-tokens build [--check] [--config <path>]
//   build    Generate CSS, XAML (WPF + Avalonia), C# and resolved JSON from design/tokens.
//   --check  Write nothing; exit 1 if any generated file is out of date (use in CI).

var check = args.Contains("--check");
var configIndex = Array.IndexOf(args, "--config");
var command = args.FirstOrDefault(a => !a.StartsWith("--")) ?? "build";

if (command is "help" or "-h" || args.Contains("--help"))
{
    Console.WriteLine("Usage: slate-tokens build [--check] [--config <slate.tokens.config.json>]");
    return 0;
}

if (command != "build")
{
    Console.Error.WriteLine($"Unknown command '{command}'. Try --help.");
    return 2;
}

try
{
    var config = configIndex >= 0 && configIndex + 1 < args.Length
        ? args[configIndex + 1]
        : TokenPipeline.DefaultConfig(TokenPipeline.FindRepoRoot(Directory.GetCurrentDirectory()));

    var result = TokenPipeline.Run(config, check);
    var root = Path.GetDirectoryName(Path.GetFullPath(config))!;
    string Rel(string p) => Path.GetRelativePath(root, p);

    foreach (var f in result.Written) Console.WriteLine($"  wrote   {Rel(f)}");
    foreach (var f in result.Stale) Console.WriteLine($"  STALE   {Rel(f)}");

    if (check && result.Stale.Count > 0)
    {
        Console.Error.WriteLine($"{result.Stale.Count} generated file(s) are out of date. Run: dotnet run --project tools/Slate.Tokens.Cli -- build");
        return 1;
    }

    Console.WriteLine(check
        ? $"All {result.Unchanged.Count} generated files are up to date."
        : $"{result.Written.Count} written, {result.Unchanged.Count} unchanged.");
    return 0;
}
catch (TokenException ex)
{
    Console.Error.WriteLine($"Token error: {ex.Message}");
    return 1;
}
