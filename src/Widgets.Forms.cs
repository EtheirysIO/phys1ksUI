using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace phys1ksUI;

/// <summary>Form widgets: a multi-line text area, a whole-number input, and a selectable list row.</summary>
internal static partial class W
{
    // ───────────────────────── Text area ─────────────────────────

    /// <summary>
    /// A multi-line themed text input (ImGui's InputTextMultiline in the kit's frame style), with an optional
    /// <paramref name="hint"/> shown while it's empty and not being edited. width &lt;= 0 fills the available width (plus
    /// width when negative); height is scaled px (0 = four lines). <paramref name="error"/> tints it red like
    /// <see cref="TextInput"/>. The input stays the last item, so ImGui.IsItemDeactivatedAfterEdit() works after it.
    /// <paramref name="maxLength"/> is UTF-8 bytes. Returns true when the text changed.
    /// </summary>
    public static bool TextArea(string id, ref string text, float width, float height = 0f, string? hint = null,
        int maxLength = 2048, bool error = false, ImGuiInputTextFlags flags = ImGuiInputTextFlags.None)
    {
        float w = FillWidth(width, ImGui.GetFrameHeight() * 3f);
        float h = height > 0f ? height : ImGui.GetTextLineHeight() * 4f + ImGui.GetStyle().FramePadding.Y * 2f;

        // A multi-line input is a child window, drawn after (over) this window's draw list. So the field's surface
        // is painted here first and the input's own background is transparent; the hint and border can then show.
        var min = ImGui.GetCursorScreenPos();
        var max = min + new Vector2(w, h);
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(min, max, Theme.U32(error ? Theme.Lerp(Theme.Field, Theme.Negative, 0.10f) : Theme.Field), Theme.Radius.Control);
        if (!string.IsNullOrEmpty(hint) && text.Length == 0 && ImGuiP.GetActiveID() != ImGui.GetID(id))
            dl.AddText(min + ImGui.GetStyle().FramePadding, Theme.U32(Theme.Faint), hint);
        if (error)
            dl.AddRect(min, max, Theme.U32(Theme.Negative with { W = 0.7f }), Theme.Radius.Control, ImDrawFlags.None, Theme.S(1f));

        bool changed;
        using (new Theme.StyleScope()
                   .Color(ImGuiCol.FrameBg, Theme.Transparent)
                   .Color(ImGuiCol.FrameBgHovered, Theme.Transparent)
                   .Color(ImGuiCol.FrameBgActive, Theme.Transparent))
            changed = ImGui.InputTextMultiline(id, ref text, ClampLength(maxLength), new Vector2(w, h), flags);
        return changed;
    }

    // ───────────────────────── Number input ─────────────────────────

    /// <summary>
    /// A whole-number input with − / + steppers (ImGui's InputInt in the kit's frame style), kept within
    /// [<paramref name="min"/>, <paramref name="max"/>]. width &lt;= 0 fills the available width (plus width when
    /// negative). <paramref name="suffix"/> ("seconds") is drawn in Dim after it. Returns true when the value changed.
    /// </summary>
    public static bool NumberInput(string id, ref int value, int min, int max, int step = 1, float width = 0f,
        string? suffix = null)
    {
        if (max < min)
            (min, max) = (max, min);
        float suffixW = string.IsNullOrEmpty(suffix) ? 0f : ImGui.CalcTextSize(suffix).X + Theme.Space.Tight;
        float w = width > 0f ? width : MathF.Max(ImGui.GetFrameHeight() * 4f, Avail() + width - suffixW);

        var edited = value;
        ImGui.SetNextItemWidth(w);
        ImGui.PushID(id);
        var typed = ImGui.InputInt("##number", ref edited, step, step * 10);
        ImGui.PopID();
        if (!string.IsNullOrEmpty(suffix))
        {
            ImGui.SameLine(0f, Theme.Space.Tight);
            ImGui.AlignTextToFramePadding();
            ImGui.TextColored(Theme.Dim, suffix);
        }

        edited = Math.Clamp(edited, min, max);
        if (!typed || edited == value)
            return false;
        value = edited;
        return true;
    }

    // ───────────────────────── List row ─────────────────────────

    /// <summary>
    /// A full-width selectable row for a list inside a page (a reaction, a file): an optional status dot, the label, an
    /// optional gray subtitle under it and Dim trailing text on the right. The selected row gets an accent wash and an
    /// accent bar on its left edge; others fade in a hover wash. True when clicked.
    /// </summary>
    public static bool ListRow(string id, string label, bool selected, string? subtitle = null, Vector4? dot = null,
        bool dotPulse = false, string? trailing = null, string? tooltip = null, bool enabled = true)
    {
        var width = Avail();
        float lineH = ImGui.GetTextLineHeight();
        float smallH;
        using (Fonts.Small.Push())
            smallH = ImGui.GetTextLineHeight();
        var height = subtitle == null ? Theme.Space.NavRowHeight : MathF.Max(Theme.S(48f), lineH + smallH + Theme.S(12f));

        var p = ImGui.GetCursorScreenPos();
        ImGui.PushID(id);
        var hoverKey = ImGui.GetID("##row");
        var selectKey = ImGui.GetID("##rowSelected");
        var clicked = InvisibleItem("##row", new Vector2(width, height), enabled);
        ImGui.PopID();
        var hovered = enabled && ImGui.IsItemHovered();

        var dl = ImGui.GetWindowDrawList();
        var max = p + new Vector2(width, height);
        var r = Theme.Radius.Small;
        var hot = Motion.Hover(hoverKey, hovered);
        var on = Motion.Approach(selectKey, selected ? 1f : 0f, 16f);
        float a = enabled ? 1f : DisabledAlpha;

        if (hot > 0.01f && on < 0.99f)
            dl.AddRectFilled(p, max, Theme.U32(Theme.Wash(0.05f * hot * (1f - on))), r);
        if (on > 0.01f)
        {
            dl.AddRectFilled(p, max, Theme.U32(Theme.AccentAlpha(0.16f * on * a)), r);
            dl.AddRectFilled(p, new Vector2(p.X + Theme.S(3f), max.Y), Theme.U32(Theme.AccentAlpha(on * a)), r, ImDrawFlags.RoundCornersLeft);
        }

        var x = p.X + Theme.S(12f);
        if (dot is { } dotColor)
        {
            StatusDot(dl, new Vector2(x + Theme.S(4f), p.Y + height * 0.5f), Theme.Fade(dotColor, a), dotPulse);
            x += Theme.S(18f);
        }

        float trailingW = 0f;
        if (!string.IsNullOrEmpty(trailing))
        {
            using (Fonts.Small.Push())
            {
                var ts = ImGui.CalcTextSize(trailing);
                trailingW = ts.X + Theme.S(12f);
                dl.AddText(new Vector2(max.X - trailingW, p.Y + (height - ts.Y) * 0.5f), Theme.U32(Theme.Fade(Theme.Dim, a)), trailing);
            }
        }

        var textW = max.X - trailingW - Theme.S(8f) - x;
        var ink = Theme.Fade(Theme.Lerp(Theme.Fade(Theme.Ink, 0.92f), Theme.Ink, MathF.Max(hot, on)), a);
        if (subtitle == null)
        {
            dl.AddText(new Vector2(x, p.Y + (height - lineH) * 0.5f), Theme.U32(ink), Fit(label, textW));
        }
        else
        {
            var top = p.Y + (height - lineH - smallH) * 0.5f;
            dl.AddText(new Vector2(x, top), Theme.U32(ink), Fit(label, textW));
            using (Fonts.Small.Push())
                dl.AddText(new Vector2(x, top + lineH), Theme.U32(Theme.Fade(Theme.Faint, a)), Fit(subtitle, textW));
        }

        HoverFeedback(hovered, tooltip);
        return clicked && enabled;
    }
}
