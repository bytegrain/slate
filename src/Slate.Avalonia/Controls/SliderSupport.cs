using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Slate.Collections;
using SlProps = Slate.Avalonia.Sl;

namespace Slate.Avalonia.Controls;

/// <summary>
/// Slate behaviour for native <see cref="Slider"/>s: step snapping and keyboard increments from Slate.Core's
/// <see cref="SliderMath"/>, tick marks, value text, and the optional second (range-end) thumb.
/// </summary>
internal static class SliderSupport
{
    private static bool _registered;

    public static void Register()
    {
        if (_registered) return;
        _registered = true;

        SlProps.StepProperty.Changed.AddClassHandler<Slider>((s, _) => ApplyStep(s));
        SlProps.ShowTicksProperty.Changed.AddClassHandler<Slider>((s, e) =>
        {
            s.Classes.Set("ticks", e.GetNewValue<bool>());
            s.TickPlacement = e.GetNewValue<bool>() ? TickPlacement.BottomRight : TickPlacement.None;
        });
        SlProps.ShowValueProperty.Changed.AddClassHandler<Slider>((s, e) => { s.Classes.Set("show-value", e.GetNewValue<bool>()); UpdateText(s); });
        SlProps.RangeEndProperty.Changed.AddClassHandler<Slider>((s, _) => Sync(s));
        RangeBase.ValueProperty.Changed.AddClassHandler<Slider>((s, _) => Sync(s));
        RangeBase.MinimumProperty.Changed.AddClassHandler<Slider>((s, _) => Sync(s));
        RangeBase.MaximumProperty.Changed.AddClassHandler<Slider>((s, _) => Sync(s));
        Visual.BoundsProperty.Changed.AddClassHandler<Slider>((s, _) => Layout(s));
        Control.LoadedEvent.AddClassHandler<Slider>((s, _) => { ApplyStep(s); Hook(s); Sync(s); });
        InputElement.KeyDownEvent.AddClassHandler<Slider>(OnKeyDown, RoutingStrategies.Tunnel);
        TemplatedControl.TemplateAppliedEvent.AddClassHandler<Slider>((s, _) => Hook(s));
    }

    private static void ApplyStep(Slider s)
    {
        var step = SlProps.GetStep(s);
        if (step <= 0) return;
        s.SmallChange = step;
        s.TickFrequency = step;
        s.IsSnapToTickEnabled = true;
    }

    private static SliderKey? KeyOf(Key key) => key switch
    {
        Key.Right or Key.Up => SliderKey.Increase,
        Key.Left or Key.Down => SliderKey.Decrease,
        Key.PageUp => SliderKey.PageIncrease,
        Key.PageDown => SliderKey.PageDecrease,
        Key.Home => SliderKey.Home,
        Key.End => SliderKey.End,
        _ => null,
    };

    private static void OnKeyDown(Slider s, KeyEventArgs e)
    {
        if (e.Handled || KeyOf(e.Key) is not { } key) return;
        var step = Math.Max(SlProps.GetStep(s), double.Epsilon);
        var onEnd = e.Source is Thumb { Name: "PART_EndThumb" };
        if (onEnd && SlProps.GetRangeEnd(s) is { } end)
        {
            var next = SliderMath.Key(end, key, s.Minimum, s.Maximum, step);
            var (a, b) = SliderMath.SetRangeThumb((s.Value, end), 1, next, s.Minimum, s.Maximum, step);
            s.Value = a;
            SlProps.SetRangeEnd(s, b);
        }
        else
        {
            var next = SliderMath.Key(s.Value, key, s.Minimum, s.Maximum, step);
            SetStart(s, next);
        }
        e.Handled = true;
    }

    /// <summary>Sets the start value, keeping it from crossing the range end.</summary>
    public static void SetStart(Slider s, double value)
    {
        var step = Math.Max(SlProps.GetStep(s), double.Epsilon);
        if (SlProps.GetRangeEnd(s) is { } end)
        {
            var (a, b) = SliderMath.SetRangeThumb((s.Value, end), 0, value, s.Minimum, s.Maximum, step);
            s.Value = a;
            SlProps.SetRangeEnd(s, b);
        }
        else
        {
            s.Value = SliderMath.Snap(value, s.Minimum, s.Maximum, step);
        }
    }

    private static readonly AttachedProperty<bool> HookedProperty = AvaloniaProperty.RegisterAttached<Slider, bool>("Hooked", typeof(SliderSupport));

    private static void Hook(Slider s)
    {
        if (Find<Thumb>(s, "PART_EndThumb") is not { } thumb || thumb.GetValue(HookedProperty)) return;
        thumb.SetValue(HookedProperty, true);
        AutomationProperties.SetName(thumb, "Range end");
        thumb.DragDelta += (_, e) =>
        {
            if (SlProps.GetRangeEnd(s) is not { } end || Find<Track>(s, "PART_Track") is not { } track || track.Bounds.Width <= 0) return;
            var fraction = SliderMath.ToFraction(end, s.Minimum, s.Maximum) + e.Vector.X / track.Bounds.Width;
            var value = SliderMath.FromFraction(Math.Clamp(fraction, 0, 1), s.Minimum, s.Maximum, Math.Max(SlProps.GetStep(s), double.Epsilon));
            var (a, b) = SliderMath.SetRangeThumb((s.Value, end), 1, value, s.Minimum, s.Maximum, Math.Max(SlProps.GetStep(s), double.Epsilon));
            s.Value = a;
            SlProps.SetRangeEnd(s, b);
        };
    }

    private static void Sync(Slider s)
    {
        var range = SlProps.GetRangeEnd(s) is not null;
        s.Classes.Set("range", range);
        if (range && SlProps.GetRangeEnd(s) is { } end && s.Value > end)
            SlProps.SetRangeEnd(s, s.Value);
        Layout(s);
        UpdateText(s);
    }

    private static void Layout(Slider s)
    {
        if (SlProps.GetRangeEnd(s) is not { } end) return;
        if (Find<Track>(s, "PART_Track") is not { } track || Find<Thumb>(s, "PART_EndThumb") is not { } thumb) return;
        var width = track.Bounds.Width;
        var thumbWidth = thumb.Bounds.Width > 0 ? thumb.Bounds.Width : 18;
        var usable = Math.Max(0, width - thumbWidth);
        var start = SliderMath.ToFraction(s.Value, s.Minimum, s.Maximum) * usable;
        var stop = SliderMath.ToFraction(end, s.Minimum, s.Maximum) * usable;
        Canvas.SetLeft(thumb, stop);
        if (Find<Border>(s, "PART_RangeFill") is { } fill)
        {
            Canvas.SetLeft(fill, start + thumbWidth / 2);
            fill.Width = Math.Max(0, stop - start);
        }
    }

    private static void UpdateText(Slider s)
    {
        if (Find<TextBlock>(s, "PART_ValueText") is not { } text) return;
        string F(double v) => v.ToString("0.##", CultureInfo.CurrentCulture);
        text.Text = SlProps.GetRangeEnd(s) is { } end ? $"{F(s.Value)} – {F(end)}" : F(s.Value);
        AutomationProperties.SetItemStatus(s, text.Text);
    }

    private static T? Find<T>(TemplatedControl c, string name) where T : Control =>
        c.GetVisualDescendants().OfType<T>().FirstOrDefault(x => x.Name == name && x.TemplatedParent == c);
}
