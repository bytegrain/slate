using System.Globalization;
using System.Net;
using System.Text;
using Slate.Blazor.Internal;
using Slate.Data;

namespace Slate.Blazor;

// Plain cells as markup strings. A cell without a template, editor or open menu has no components or handlers (clicks,
// checkboxes and expanders are delegated by slate.js), so it renders as ONE markup frame instead of ~15 element/attribute
// frames: a row entering the window costs about its HTML size on Blazor Server, and unchanged rows diff as a string
// compare. The markup is byte-for-byte the same as the Razor path (css-classes.md#datagrid).
public partial class SlDataGrid<T>
{
    private static readonly Dictionary<string, string> IconHtmlCache = new();

    private static string IconHtml(string name)
    {
        lock (IconHtmlCache)
        {
            if (!IconHtmlCache.TryGetValue(name, out var html))
            {
                html = "<svg class=\"sl-icon\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\" stroke-linecap=\"round\" stroke-linejoin=\"round\" aria-hidden=\"true\" focusable=\"false\"><path d=\""
                       + WebUtility.HtmlEncode(SlateIcons.All[name]) + "\"></path></svg>";
                IconHtmlCache[name] = html;
            }
            return html;
        }
    }

    private static string Enc(string? text) => WebUtility.HtmlEncode(text ?? "");

    /// <summary>Cells that need real frames: templates (components), the editor, an open row menu.</summary>
    private bool NeedsFrames(Slot slot, ResolvedColumn<T> c, bool editing) =>
        editing || Def(c.Field)?.Template is not null || (c.Column.Type == GridColumnType.Actions && _openActionsKey == slot.Key);

    private string SelectCellHtml(Slot slot, int row, bool selected)
    {
        var sb = new StringBuilder(256);
        sb.Append("<div class=\"").Append(Css.Of("sl-data-grid__cell sl-data-grid__cell--select is-pinned-start").Add("is-pinned-edge-start", SelectIsEdge).ToString())
          .Append("\" role=\"gridcell\" style=\"width:").Append(GridCells.Px(SelectWidth)).Append(";left:0\">");
        if (slot.Kind == SlotKind.Data)
        {
            sb.Append("<label class=\"sl-checkbox sl-tone-accent sl-data-grid__check\"><input type=\"checkbox\" class=\"sl-checkbox__input\" tabindex=\"-1\"")
              .Append(selected ? " checked" : "").Append(" aria-label=\"Select row ")
              .Append((RowAriaIndex(row) - HeaderRowCount).ToString(CultureInfo.InvariantCulture)).Append("\"></label>");
        }
        return sb.Append("</div>").ToString();
    }

    private string DetailCellHtml(Slot slot)
    {
        var sb = new StringBuilder(256);
        sb.Append("<div class=\"sl-data-grid__cell sl-data-grid__cell--select is-pinned-start\" role=\"gridcell\" style=\"width:").Append(GridCells.Px(DetailToggleWidth))
          .Append(";left:").Append(GridCells.Px(SelectionMode == GridSelectionMode.Multi ? SelectWidth : 0)).Append("\">");
        if (slot.Kind == SlotKind.Data)
        {
            sb.Append("<button class=\"sl-data-grid__expander\" type=\"button\" tabindex=\"-1\" aria-expanded=\"")
              .Append(_state.ExpandedDetails.Contains(slot.Key) ? "true" : "false").Append("\" aria-label=\"Toggle details\">")
              .Append(IconHtml("chevron-right")).Append("</button>");
        }
        return sb.Append("</div>").ToString();
    }

    private string CellHtml(Slot slot, int row, ResolvedColumn<T> c, bool active, bool dirty)
    {
        var column = c.Column;
        var type = column.Type;
        var align = column.EffectiveAlign;
        var cls = PinClasses(Css.Of("sl-data-grid__cell")
            .Add($"sl-data-grid__cell--{Names.Kebab(type.ToString())}")
            .Add("sl-data-grid__cell--end", align == GridAlign.End)
            .Add("sl-data-grid__cell--center", align == GridAlign.Center)
            .Add("is-active", active)
            .Add("is-dirty", dirty), c);
        var sb = new StringBuilder(256);
        sb.Append("<div class=\"").Append(cls.ToString()).Append("\" role=\"gridcell\" part=\"cell\" id=\"").Append(CellId(row, c.Index))
          .Append("\" data-field=\"").Append(Enc(c.Field)).Append("\" aria-colindex=\"").Append(ColIndex(c).ToString(CultureInfo.InvariantCulture)).Append('"');
        if (EditMode != GridEditMode.None && !column.Editable) sb.Append(" aria-readonly=\"true\"");
        sb.Append(" style=\"").Append(PinStyle(c)).Append("\">");

        if (ChildrenSelector is not null && c.Index == 0 && slot.View is { } view)
        {
            sb.Append("<span style=\"width:").Append(GridCells.Px(slot.Depth * 20)).Append(";flex:none\"></span>");
            if (view.HasChildren)
            {
                sb.Append("<button class=\"sl-data-grid__expander\" type=\"button\" tabindex=\"-1\" aria-expanded=\"").Append(view.Expanded ? "true" : "false")
                  .Append("\" aria-label=\"").Append(view.Expanded ? "Collapse" : "Expand").Append("\">").Append(IconHtml("chevron-right")).Append("</button>");
            }
            else sb.Append("<span class=\"sl-data-grid__expander-spacer\"></span>");
        }

        if (slot.Kind == SlotKind.Skeleton) sb.Append("<span class=\"sl-data-grid__skeleton\"></span>");
        else if (slot.Item is not null)
        {
            var value = _edit.Pending.Count > 0 ? _edit.GetValue(slot.Item, slot.Key, column) : column.GetValue(slot.Item);
            var text = value is null ? "" : column.DisplayText(value);
            AppendContent(sb, Def(c.Field), column, value, slot.Item, text);
        }
        return sb.Append("</div>").ToString();
    }

    private static void AppendContent(StringBuilder sb, DataGridColumn<T>? def, GridColumn<T> column, object? value, T item, string text)
    {
        var type = column.Type;
        if (value is null || value is "")
        {
            if (type == GridColumnType.Actions) AppendActionsTrigger(sb, def);
            else sb.Append("<span class=\"sl-data-grid__muted\">—</span>");
            return;
        }
        switch (type)
        {
            case GridColumnType.Boolean:
                if (value is true || text is "true" or "Yes") sb.Append("<span class=\"sl-data-grid__bool\" role=\"img\" aria-label=\"Yes\">").Append(IconHtml("check")).Append("</span>");
                else sb.Append("<span class=\"sl-data-grid__muted\" aria-label=\"No\">—</span>");
                break;
            case GridColumnType.Enum:
                var tone = column.EnumTones is { } tones && tones.TryGetValue(text, out var t) ? t : Tone.Neutral;
                sb.Append("<span class=\"sl-badge sl-badge--soft ").Append(Names.Tone(tone)).Append("\">").Append(Enc(text)).Append("</span>");
                break;
            case GridColumnType.Progress:
                var n = Math.Min(Math.Max(GridValues.ToDouble(value) ?? 0, 0), 100).ToString(CultureInfo.InvariantCulture);
                sb.Append("<span class=\"sl-data-grid__progress\"><span class=\"sl-progress sl-progress--small sl-tone-accent\" role=\"progressbar\" aria-valuemin=\"0\" aria-valuemax=\"100\" aria-valuenow=\"")
                  .Append(n).Append("\"><span class=\"sl-progress__bar\" style=\"--_value:").Append(n).Append("%\"></span></span><span class=\"sl-data-grid__progress-value\">")
                  .Append(Enc(text)).Append("</span></span>");
                break;
            case GridColumnType.Sparkline:
                var values = GridCells.Series(value);
                var max = Math.Max(1e-9, values.Count > 0 ? values.Max(Math.Abs) : 0);
                sb.Append("<span class=\"sl-data-grid__sparkline\" role=\"img\" aria-label=\"Trend: ")
                  .Append(Enc(string.Join(", ", values.Select(x => x.ToString(CultureInfo.InvariantCulture))))).Append("\">");
                foreach (var x in values)
                    sb.Append("<span class=\"sl-data-grid__spark\" style=\"height:").Append(Math.Max(6, Math.Abs(x) / max * 100).ToString(CultureInfo.InvariantCulture)).Append("%\"></span>");
                sb.Append("</span>");
                break;
            case GridColumnType.Actions:
                AppendActionsTrigger(sb, def);
                break;
            default:
                sb.Append("<span class=\"sl-data-grid__text\">").Append(Enc(text)).Append("</span>");
                break;
        }
    }

    /// <summary>SlMenu's closed markup; the real menu mounts when the trigger is clicked.</summary>
    private static void AppendActionsTrigger(StringBuilder sb, DataGridColumn<T>? def)
    {
        if (def?.Actions is not { Count: > 0 }) return;
        sb.Append("<span class=\"sl-menu sl-data-grid__actions\"><span class=\"sl-menu__trigger\"><button class=\"sl-data-grid__row-action\" type=\"button\" aria-label=\"Row actions\" tabindex=\"-1\" aria-haspopup=\"menu\" aria-expanded=\"false\">")
          .Append(IconHtml("more-horizontal")).Append("</button></span></span>");
    }
}
