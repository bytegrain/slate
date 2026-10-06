using Microsoft.AspNetCore.Components;

namespace Slate.Blazor.Tests;

public class TextFieldTests : SlateTestContext
{
    [Fact]
    public void Renders_contract_markup_with_label_helper_and_description_link()
    {
        var cut = Render<SlTextField<string>>(p => p
            .Add(x => x.Label, "Email").Add(x => x.HelperText, "Work address").Add(x => x.Placeholder, "you@studio.dev")
            .AddUnmatched("id", "email"));

        cut.MarkupMatches("""
            <div class="sl-field">
              <label class="sl-field__label" for="email">Email</label>
              <div class="sl-field__control">
                <input id="email" class="sl-field__input" type="text" placeholder="you@studio.dev" aria-invalid="false" aria-describedby="email-desc" />
              </div>
              <p class="sl-field__helper" id="email-desc">Work address</p>
            </div>
            """);
    }

    [Fact]
    public void Required_adds_asterisk_and_native_required()
    {
        var cut = Render<SlTextField<string>>(p => p.Add(x => x.Label, "Name").Add(x => x.Required, true));
        Assert.Equal("true", cut.Find(".sl-field__required").GetAttribute("aria-hidden"));
        Assert.True(cut.Find("input").HasAttribute("required"));
    }

    [Fact]
    public void Error_replaces_helper_and_marks_invalid()
    {
        var cut = Render<SlTextField<string>>(p => p.Add(x => x.Label, "Email").Add(x => x.HelperText, "h").Add(x => x.Error, "Enter a complete email address."));
        Assert.Contains("sl-field--invalid", cut.Find(".sl-field").ClassList);
        Assert.Equal("true", cut.Find("input").GetAttribute("aria-invalid"));
        Assert.Empty(cut.FindAll(".sl-field__helper"));
        var error = cut.Find(".sl-field__error");
        Assert.Equal(error.Id, cut.Find("input").GetAttribute("aria-describedby"));
        Assert.Contains("Enter a complete email address.", error.TextContent);
        Assert.NotNull(error.QuerySelector("svg.sl-icon"));
    }

    [Fact]
    public void Affixes_icon_size_disabled_and_readonly()
    {
        var cut = Render<SlTextField<string>>(p => p.Add(x => x.Prefix, "https://").Add(x => x.Suffix, ".dev").Add(x => x.StartIcon, "search")
            .Add(x => x.Size, ControlSize.Large).Add(x => x.Disabled, true).Add(x => x.ReadOnly, true));
        var control = cut.Find(".sl-field__control");
        Assert.Equal(["sl-field__affix sl-field__affix--prefix", "sl-field__icon", "sl-field__input", "sl-field__affix sl-field__affix--suffix"],
            control.Children.Select(c => c.ClassName));
        Assert.Contains("sl-field--lg", cut.Find(".sl-field").ClassList);
        Assert.Contains("sl-field--disabled", cut.Find(".sl-field").ClassList);
        Assert.True(cut.Find("input").HasAttribute("disabled"));
        Assert.True(cut.Find("input").HasAttribute("readonly"));
    }

    [Fact]
    public void Multiline_renders_textarea()
    {
        var cut = Render<SlTextField<string>>(p => p.Add(x => x.Multiline, true).Add(x => x.Rows, 5));
        var area = cut.Find("textarea");
        Assert.Equal("sl-field__input sl-field__input--multiline", area.ClassName);
        Assert.Equal("5", area.GetAttribute("rows"));
        Assert.Contains("sl-field--multiline", cut.Find(".sl-field").ClassList);
    }

    [Fact]
    public void Two_way_binding_on_change()
    {
        string? bound = "a";
        var cut = Render<SlTextField<string>>(p => p.Add(x => x.Value, bound).Add(x => x.ValueChanged, (string? v) => bound = v));
        Assert.Equal("a", cut.Find("input").GetAttribute("value"));
        cut.Find("input").Change("hello");
        Assert.Equal("hello", bound);
    }

    [Fact]
    public void Immediate_updates_on_input_but_change_is_ignored()
    {
        var values = new List<string?>();
        var cut = Render<SlTextField<string>>(p => p.Add(x => x.Immediate, true).Add(x => x.ValueChanged, (string? v) => values.Add(v)));
        cut.Find("input").Input("h");
        cut.Find("input").Change("ignored");
        Assert.Equal(["h"], values);
    }

    [Fact]
    public void Debounce_waits_for_quiet_period()
    {
        var values = new List<string?>();
        var cut = Render<SlTextField<string>>(p => p.Add(x => x.Immediate, true).Add(x => x.DebounceMilliseconds, 300)
            .Add(x => x.ValueChanged, (string? v) => values.Add(v)));
        cut.Find("input").Input("a");
        Time.Advance(TimeSpan.FromMilliseconds(200));
        cut.Find("input").Input("ab");
        Time.Advance(TimeSpan.FromMilliseconds(299));
        Assert.Empty(values);
        Time.Advance(TimeSpan.FromMilliseconds(1));
        cut.WaitForAssertion(() => Assert.Equal(["ab"], values));
    }

    [Fact]
    public void Numeric_values_parse_and_invalid_text_shows_parse_error()
    {
        int bound = 1;
        var cut = Render<SlTextField<int>>(p => p.Add(x => x.Value, bound).Add(x => x.ValueChanged, (int v) => bound = v).Add(x => x.InputType, "number"));
        cut.Find("input").Change("42");
        Assert.Equal(42, bound);
        cut.Find("input").Change("forty");
        Assert.Equal(42, bound);
        Assert.Equal("Enter a valid value.", cut.Find(".sl-field__error").TextContent.Trim());
    }

    [Fact]
    public void Nullable_numbers_accept_empty()
    {
        int? bound = 5;
        var cut = Render<SlTextField<int?>>(p => p.Add(x => x.Value, bound).Add(x => x.ValueChanged, (int? v) => bound = v));
        cut.Find("input").Change("");
        Assert.Null(bound);
    }

    [Fact]
    public void Format_is_applied_to_display_value() =>
        Assert.Equal("3.50", Render<SlTextField<decimal>>(p => p.Add(x => x.Value, 3.5m).Add(x => x.Format, "0.00")).Find("input").GetAttribute("value"));

    [Fact]
    public void Generates_unique_ids_when_none_given()
    {
        var a = Render<SlTextField<string>>(p => p.Add(x => x.Label, "A")).Find("input").Id;
        var b = Render<SlTextField<string>>(p => p.Add(x => x.Label, "B")).Find("input").Id;
        Assert.NotEqual(a, b);
    }
}

public class SelectionTests : SlateTestContext
{
    [Fact]
    public void Checkbox_markup_and_toggle()
    {
        bool value = false;
        var cut = Render<SlCheckbox<bool>>(p => p.Add(x => x.Label, "Notify").Add(x => x.Description, "Sends to #builds")
            .Add(x => x.Value, value).Add(x => x.ValueChanged, (bool v) => value = v).AddUnmatched("id", "c1"));

        cut.MarkupMatches("""
            <label class="sl-checkbox">
              <input id="c1" type="checkbox" class="sl-checkbox__input" aria-describedby="c1-desc" />
              <span class="sl-checkbox__text">
                <span class="sl-checkbox__label">Notify</span>
                <span class="sl-checkbox__description" id="c1-desc">Sends to #builds</span>
              </span>
            </label>
            """);

        cut.Find("input").Change(true);
        Assert.True(value);
    }

    [Fact]
    public void Nullable_checkbox_null_is_indeterminate_and_click_checks_it()
    {
        bool? value = null;
        var cut = Render<SlCheckbox<bool?>>(p => p.Add(x => x.Value, value).Add(x => x.ValueChanged, (bool? v) => value = v));
        var input = cut.Find("input");
        Assert.Contains("is-indeterminate", input.ClassList);
        Assert.Equal("mixed", input.GetAttribute("aria-checked"));
        Assert.False(input.HasAttribute("checked"));

        input.Change(true);
        Assert.True(value);
    }

    [Fact]
    public void Indeterminate_property_is_set_through_js()
    {
        var module = Module;
        module.SetupVoid("setIndeterminate", _ => true);
        Render<SlCheckbox<bool?>>(p => p.Add(x => x.Value, (bool?)null));
        Assert.Single(module.Invocations["setIndeterminate"]);
    }

    [Fact]
    public void Checkbox_rejects_non_bool_types() =>
        Assert.Throws<InvalidOperationException>(() => Render<SlCheckbox<int>>());

    [Fact]
    public void Disabled_checkbox()
    {
        var cut = Render<SlCheckbox<bool>>(p => p.Add(x => x.Disabled, true));
        Assert.Contains("is-disabled", cut.Find("label").ClassList);
        Assert.True(cut.Find("input").HasAttribute("disabled"));
    }

    [Fact]
    public void Switch_markup_states_and_label_position()
    {
        bool on = true;
        var cut = Render<SlSwitch>(p => p.Add(x => x.Label, "Preview deployments").Add(x => x.Spread, true)
            .Add(x => x.Value, on).Add(x => x.ValueChanged, (bool v) => on = v));

        var label = cut.Find("label");
        Assert.Equal("sl-switch sl-switch--label-start sl-switch--spread", label.ClassName);
        var input = cut.Find("input");
        Assert.Equal("switch", input.GetAttribute("role"));
        Assert.Equal("true", input.GetAttribute("aria-checked"));
        Assert.True(input.HasAttribute("checked"));
        Assert.Equal("INPUT", label.LastElementChild!.TagName); // label text first, switch after

        input.Change(false);
        Assert.False(on);

        var end = Render<SlSwitch>(p => p.Add(x => x.LabelPosition, LabelPosition.End)).Find("label");
        Assert.Contains("sl-switch--label-end", end.ClassList);
        Assert.Equal("INPUT", end.FirstElementChild!.TagName);
    }

    [Fact]
    public void Switch_prevents_enter_submission_via_js()
    {
        var module = Module;
        module.SetupVoid("preventEnter", _ => true);
        Render<SlSwitch>();
        Assert.Single(module.Invocations["preventEnter"]);
    }
}
