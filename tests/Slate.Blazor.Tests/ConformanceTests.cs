using System.Reflection;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Components;

namespace Slate.Blazor.Tests;

/// <summary>
/// The canonical API (design/api/components.json) is the contract every platform implements. These tests fail
/// when a Blazor component is missing an option, spells it differently, or uses the wrong enum type.
/// </summary>
public class ConformanceTests
{
    private static readonly JsonObject Api = LoadApi();

    private static JsonObject LoadApi()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var file = Path.Combine(dir.FullName, "design", "api", "components.json");
            if (File.Exists(file)) return JsonNode.Parse(File.ReadAllText(file))!.AsObject();
        }
        throw new FileNotFoundException("design/api/components.json not found above the test output.");
    }

    /// <summary>Canonical component → Blazor component type.</summary>
    public static readonly Dictionary<string, Type> Components = new()
    {
        ["Button"] = typeof(SlButton),
        ["TextField"] = typeof(SlTextField<string>),
        ["Checkbox"] = typeof(SlCheckbox<bool>),
        ["Switch"] = typeof(SlSwitch),
        ["RadioGroup"] = typeof(SlRadioGroup<string>),
        ["Alert"] = typeof(SlAlert),
        ["Badge"] = typeof(SlBadge),
        ["Progress"] = typeof(SlProgress),
        ["Spinner"] = typeof(SlSpinner),
        ["Card"] = typeof(SlCard),
        ["AppShell"] = typeof(SlAppShell),
        ["AppBar"] = typeof(SlAppBar),
        ["NavItem"] = typeof(SlNavItem),
        ["Container"] = typeof(SlContainer),
        ["Stack"] = typeof(SlStack),
        ["SnackbarHost"] = typeof(SlSnackbarHost),
        ["Dialog"] = typeof(SlDialog),
        ["Select"] = typeof(SlSelect<string>),
        ["Menu"] = typeof(SlMenu),
        ["Tabs"] = typeof(SlTabs),
        ["Tooltip"] = typeof(SlTooltip),
        ["Popover"] = typeof(SlPopover),
        ["DatePicker"] = typeof(SlDatePicker),
        ["TreeView"] = typeof(SlTreeView<string>),
        ["SegmentedControl"] = typeof(SlSegmented<string>),
        ["Slider"] = typeof(SlSlider),
        ["Avatar"] = typeof(SlAvatar),
        ["Breadcrumbs"] = typeof(SlBreadcrumbs),
        ["Pagination"] = typeof(SlPagination),
        ["Skeleton"] = typeof(SlSkeleton),
    };

    /// <summary>Canonical item spec ("item" in components.json) → Blazor child component type.</summary>
    public static readonly Dictionary<string, Type> ItemComponents = new()
    {
        ["Menu"] = typeof(SlMenuItem),
        ["Tabs"] = typeof(SlTab),
    };

    /// <summary>
    /// Per-component renames forced by Razor. SlTooltip's ChildContent is the anchor it wraps (so a tooltip reads
    /// <c>&lt;SlTooltip Text="…"&gt;&lt;SlButton/&gt;&lt;/SlTooltip&gt;</c>); the canonical rich Content is TooltipContent.
    /// </summary>
    private static readonly Dictionary<(string, string), string> Renames = new()
    {
        [("Tooltip", "Content")] = "TooltipContent",
    };

    public static TheoryData<string> ComponentNames => new(Api["components"]!.AsObject().Where(kv => kv.Value?["status"]?.GetValue<string>() != "planned").Select(kv => kv.Key));

    /// <summary>Blazor's one fixed spelling: the default content region is ChildContent (Razor needs it for inline content).</summary>
    private static string BlazorName(string canonical) => canonical == "Content" ? "ChildContent" : canonical;

    private static string BlazorName(string component, string canonical) =>
        Renames.TryGetValue((component, canonical), out var renamed) ? renamed : BlazorName(canonical);

    private static IEnumerable<(string Name, JsonObject Spec)> Options(string component) =>
        Api["components"]![component]!["options"]!.AsObject()
            .Select(kv => (kv.Key, kv.Value!.AsObject()))
            .Where(o => o.Item2["platforms"] is not JsonArray platforms || platforms.Any(p => p!.GetValue<string>() == "blazor"));

    [Theory, MemberData(nameof(ComponentNames))]
    public void Every_canonical_component_has_a_blazor_component(string component) =>
        Assert.True(Components.ContainsKey(component), $"No Blazor component mapped for '{component}'.");

    [Theory, MemberData(nameof(ComponentNames))]
    public void Every_option_is_a_parameter_with_the_canonical_name_and_kind(string component)
    {
        var type = Components[component];
        var missing = new List<string>();
        foreach (var (name, spec) in Options(component))
        {
            var prop = type.GetProperty(BlazorName(component, name), BindingFlags.Public | BindingFlags.Instance);
            if (prop is null || prop.GetCustomAttribute<ParameterAttribute>() is null)
            {
                missing.Add(name);
                continue;
            }

            var kind = spec["kind"]!.GetValue<string>();
            var t = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
            switch (kind)
            {
                case "slot":
                    Assert.True(t == typeof(RenderFragment) || (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(RenderFragment<>)),
                        $"{component}.{name} must be a RenderFragment, is {t.Name}.");
                    break;
                case "event":
                    Assert.True(t == typeof(EventCallback) || (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(EventCallback<>)),
                        $"{component}.{name} must be an EventCallback, is {t.Name}.");
                    break;
                case "bool":
                    Assert.True(t == typeof(bool), $"{component}.{name} must be a bool, is {t.Name}.");
                    break;
            }
        }
        Assert.True(missing.Count == 0, $"{component} ({type.Name}) is missing: {string.Join(", ", missing)}");
    }

    [Theory, MemberData(nameof(ComponentNames))]
    public void Enum_typed_options_use_the_shared_core_enum(string component)
    {
        var enums = Api["enums"]!.AsObject();
        foreach (var (name, spec) in Options(component))
        {
            var typeName = spec["type"]?.GetValue<string>();
            if (typeName is null || !enums.ContainsKey(typeName)) continue;

            var core = typeof(Tone).Assembly.GetType($"Slate.{typeName}") ?? typeof(Tone).Assembly.GetType($"Slate.Dialogs.{typeName}")
                       ?? typeof(Tone).Assembly.GetType($"Slate.Snackbars.{typeName}");
            if (core is null) continue; // e.g. Severity lives in Slate too; non-enum JSON vocab like ContainerWidth all exist
            var prop = Components[component].GetProperty(BlazorName(component, name))!;
            var actual = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
            Assert.True(actual == core, $"{component}.{name} should use {core.FullName}, uses {actual.FullName}.");
        }
    }

    [Theory, MemberData(nameof(ComponentNames))]
    public void Every_item_option_is_a_parameter_of_the_item_component(string component)
    {
        if (Api["components"]![component]!["item"] is not JsonObject item) return;
        Assert.True(ItemComponents.TryGetValue(component, out var type), $"No Blazor item component mapped for '{component}'.");
        var missing = item.Select(kv => kv.Key)
            .Where(name => type!.GetProperty(BlazorName(name))?.GetCustomAttribute<ParameterAttribute>() is null).ToList();
        Assert.True(missing.Count == 0, $"{component} item ({type!.Name}) is missing: {string.Join(", ", missing)}");
    }

    [Fact]
    public void Shared_enum_values_match_the_canonical_vocabulary()
    {
        foreach (var (name, values) in Api["enums"]!.AsObject())
        {
            var core = typeof(Tone).Assembly.GetTypes().FirstOrDefault(t => t.IsEnum && t.Name == name);
            if (core is null) continue;
            var expected = values!.AsArray().Select(v => v!.GetValue<string>()).ToList();
            var actual = Enum.GetNames(core).Select(Kebab).ToList();
            Assert.True(expected.SequenceEqual(actual), $"{name}: contract [{string.Join(", ", expected)}] vs core [{string.Join(", ", actual)}]");
        }
    }

    [Fact]
    public void Every_rendering_component_passes_class_style_and_attributes_through()
    {
        foreach (var type in typeof(SlButton).Assembly.GetExportedTypes()
                     .Where(t => t.Name.StartsWith("Sl", StringComparison.Ordinal) && typeof(IComponent).IsAssignableFrom(t) && !t.IsAbstract))
        {
            if (type == typeof(SlDialog) || type.Name is "SlSnackbarHost" or "SlDialogHost" or "SlDialogFrame") continue; // rendered by the provider
            foreach (var p in new[] { "Class", "Style", "AdditionalAttributes" })
                Assert.True(type.GetProperty(p)?.GetCustomAttribute<ParameterAttribute>() is not null, $"{type.Name} lacks {p}.");
        }
    }

    private static string Kebab(string value) =>
        string.Concat(value.Select((c, i) => char.IsUpper(c) ? (i > 0 ? "-" : "") + char.ToLowerInvariant(c) : c.ToString()));
}

/// <summary>Rendered markup carries the documented parts (css-classes.md) when every region is used.</summary>
public class MarkupPartsTests : SlateTestContext
{
    private static void HasClasses(AngleSharp.Dom.INode root, params string[] classes)
    {
        var html = ((AngleSharp.Dom.IElement)root).OuterHtml;
        foreach (var c in classes) Assert.Contains(c, html);
    }

    [Fact]
    public void Button_parts()
    {
        var cut = Render<SlButton>(p => p.Add(x => x.StartIcon, "plus").Add(x => x.EndIcon, "chevron-down").AddChildContent("New"));
        HasClasses(cut.Find("button"), "sl-button__start", "sl-button__label", "sl-button__end", "sl-button__icon");
    }

    [Fact]
    public void TextField_parts()
    {
        var cut = Render<SlTextField<string>>(p => p.Add(x => x.Label, "L").Add(x => x.Prefix, "a").Add(x => x.Suffix, "b")
            .Add(x => x.HelperText, "h").Add(x => x.Counter, true));
        HasClasses(cut.Find(".sl-field"), "sl-field__label", "sl-field__control", "sl-field__input", "sl-field__helper",
            "sl-field__affix--prefix", "sl-field__affix--suffix", "sl-field__footer", "sl-field__counter");
        var error = Render<SlTextField<string>>(p => p.Add(x => x.Error, "e"));
        HasClasses(error.Find(".sl-field"), "sl-field__error");
    }

    [Fact]
    public void Card_parts()
    {
        var cut = Render<SlCard>(p => p.Add(x => x.Title, "T").Add(x => x.Subtitle, "S").Add(x => x.HeaderActions, "<i></i>")
            .AddChildContent("b").Add(x => x.Footer, "<i></i>"));
        HasClasses(cut.Find("article"), "sl-card__header", "sl-card__title", "sl-card__subtitle", "sl-card__actions", "sl-card__body", "sl-card__footer");
    }

    [Fact]
    public void Alert_parts()
    {
        var cut = Render<SlAlert>(p => p.Add(x => x.Title, "T").AddChildContent("m").Add(x => x.Actions, "<i></i>").Add(x => x.Dismissible, true));
        HasClasses(cut.Find(".sl-alert"), "sl-alert__icon", "sl-alert__title", "sl-alert__message", "sl-alert__actions", "sl-alert__close");
    }

    [Fact]
    public void Checkbox_and_switch_parts()
    {
        HasClasses(Render<SlCheckbox<bool>>(p => p.Add(x => x.Label, "l").Add(x => x.Description, "d")).Find("label"),
            "sl-checkbox__input", "sl-checkbox__label", "sl-checkbox__description");
        HasClasses(Render<SlSwitch>(p => p.Add(x => x.Label, "l").Add(x => x.Description, "d")).Find("label"),
            "sl-switch__control", "sl-switch__thumb", "sl-switch__label", "sl-switch__description");
    }
}
