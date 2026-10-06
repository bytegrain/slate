using System.Reflection;

namespace Slate.Wpf.ResourceTests;

/// <summary>
/// Slate.Wpf's compiled metadata, loaded with <see cref="MetadataLoadContext"/> against the WindowsDesktop reference
/// pack from the NuGet cache, so WPF types can be inspected (not executed) on any OS.
/// </summary>
internal static class WpfMetadata
{
    private static readonly Lazy<(MetadataLoadContext Context, Assembly Slate, Assembly Core, Assembly[] Wpf)> State = new(Load);

    public static Assembly Slate => State.Value.Slate;
    public static Assembly Core => State.Value.Core;

    private static string Configuration =>
        typeof(WpfMetadata).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == "SlateConfiguration")?.Value ?? "Debug";

    private static (MetadataLoadContext, Assembly, Assembly, Assembly[]) Load()
    {
        var root = XamlResourceTests.RepoRoot;
        var bin = Path.Combine(root, "src", "Slate.Wpf", "bin", Configuration, "net10.0-windows");
        var slatePath = Path.Combine(bin, "Slate.Wpf.dll");
        if (!File.Exists(slatePath))
            throw new FileNotFoundException($"Build Slate.Wpf ({Configuration}) first; expected {slatePath}.");

        var desktopRef = FindWindowsDesktopRefPack();
        var runtime = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var nuget = Path.Combine(Environment.GetEnvironmentVariable("NUGET_PACKAGES") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages"));
        var extras = Directory.Exists(Path.Combine(nuget, "microsoft.extensions.dependencyinjection.abstractions"))
            ? Directory.GetFiles(Path.Combine(nuget, "microsoft.extensions.dependencyinjection.abstractions"), "*.dll", SearchOption.AllDirectories).Where(p => p.Contains("net10.0") || p.Contains("net9.0")).Take(1)
            : [];

        // Desktop reference assemblies first so WPF names resolve to them; then the running runtime for the BCL.
        var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.GetFiles(bin, "*.dll").Concat(Directory.GetFiles(desktopRef, "*.dll")).Concat(Directory.GetFiles(runtime, "*.dll")).Concat(extras))
            paths.TryAdd(Path.GetFileName(file), file);

        var context = new MetadataLoadContext(new PathAssemblyResolver(paths.Values), "System.Private.CoreLib");
        var slate = context.LoadFromAssemblyPath(slatePath);
        var core = context.LoadFromAssemblyName("Slate.Core");
        var wpf = new[] { "PresentationFramework", "PresentationCore", "WindowsBase" }.Select(context.LoadFromAssemblyName).ToArray();
        return (context, slate, core, wpf);
    }

    private static string FindWindowsDesktopRefPack()
    {
        var nuget = Environment.GetEnvironmentVariable("NUGET_PACKAGES") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
        var candidates = new[]
        {
            Path.Combine(nuget, "microsoft.windowsdesktop.app.ref"),
            Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(typeof(object).Assembly.Location)!)!)!)!, "packs", "Microsoft.WindowsDesktop.App.Ref"),
        };
        foreach (var packs in candidates.Where(Directory.Exists))
        {
            var dir = Directory.GetDirectories(packs).OrderByDescending(d => d).Select(d => Path.Combine(d, "ref", "net10.0")).FirstOrDefault(Directory.Exists);
            if (dir is not null) return dir;
        }
        throw new DirectoryNotFoundException("Microsoft.WindowsDesktop.App.Ref (net10.0) not found; build Slate.Wpf once so the targeting pack is restored.");
    }

    /// <summary>A Slate.Wpf type ("Card") or a WPF type ("Button", "TextBlock"…) by simple name.</summary>
    public static Type? Find(string name)
    {
        var (_, slate, _, wpf) = State.Value;
        if (name.StartsWith("sl:", StringComparison.Ordinal))
            return slate.GetType("Slate.Wpf." + name[3..]);

        string[] preferred = ["System.Windows.Controls", "System.Windows.Controls.Primitives", "System.Windows", "System.Windows.Shapes", "System.Windows.Documents",
            "System.Windows.Media", "System.Windows.Input", "System.Windows.Automation", "System.Windows.Shell"];
        foreach (var ns in preferred)
        {
            foreach (var asm in wpf)
            {
                if (asm.GetType($"{ns}.{name}") is { } t) return t;
            }
        }
        return null;
    }

    public static Type Sl => Slate.GetType("Slate.Wpf.Sl") ?? throw new InvalidOperationException("Slate.Wpf.Sl not found.");

    /// <summary>A dependency property field (attached or not) visible on <paramref name="type"/>.</summary>
    public static bool HasDependencyProperty(Type type, string name) =>
        type.GetField(name + "Property", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy) is not null;

    public static bool HasMember(Type type, string name) =>
        type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance) is not null
        || type.GetEvent(name, BindingFlags.Public | BindingFlags.Instance) is not null;

    public static Type? PropertyType(Type type, string name) =>
        type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?.PropertyType;

    /// <summary>Unwraps Nullable&lt;T&gt;.</summary>
    public static Type Unwrap(Type t) => t.IsGenericType && t.GetGenericTypeDefinition().FullName == "System.Nullable`1" ? t.GetGenericArguments()[0] : t;
}
