using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures;

namespace phys1ksUI;

/// <summary>Fill style for <see cref="W.IconTextButton"/> and friends.</summary>
internal enum ButtonKind
{
    Primary,
    Secondary,
    Danger,
    Ghost,
}

/// <summary>
/// Custom widgets drawn with the window DrawList over an InvisibleButton. All sizes are scaled.
/// Labels follow ImGui's "Visible##id" convention. Every widget respects an enclosing
/// ImGui.BeginDisabled() (colors dim via <see cref="Theme.U32"/>, no clicks).
/// </summary>
internal static partial class W
{
    private const float DisabledAlpha = 0.4f;

    private static readonly Dictionary<uint, uint> IconIdCache = new();
    private static readonly Dictionary<FontAwesomeIcon, string> IconStrings = new();
    private static readonly string[] AsciiGlyphs = BuildAsciiGlyphs();
    private static readonly Dictionary<char, string> OtherGlyphs = new();
    private static readonly Dictionary<string, string> UpperCache = new(StringComparer.Ordinal);
    private const int UpperCacheLimit = 1024;
    private static int cardDepth;
    private static float cardRightPad;

    // ───────────────────────── Buttons ─────────────────────────

    public static bool PrimaryButton(string label, Vector2? size = null, bool enabled = true, string? tooltip = null)
        => ButtonCore(label, null, ButtonKind.Primary, size, enabled, tooltip);

    public static bool SecondaryButton(string label, Vector2? size = null, bool enabled = true, string? tooltip = null)
        => ButtonCore(label, null, ButtonKind.Secondary, size, enabled, tooltip);

    public static bool DangerButton(string label, Vector2? size = null, bool enabled = true, string? tooltip = null)
        => ButtonCore(label, null, ButtonKind.Danger, size, enabled, tooltip);

    public static bool GhostButton(string label, Vector2? size = null, bool enabled = true, string? tooltip = null)
        => ButtonCore(label, null, ButtonKind.Ghost, size, enabled, tooltip);

    /// <summary>An icon glyph and a label (two fonts), centered together, in the given fill style.</summary>
    public static bool IconTextButton(FontAwesomeIcon icon, string label, ButtonKind kind,
        Vector2? size = null, bool enabled = true, string? tooltip = null)
        => ButtonCore(label, icon, kind, size, enabled, tooltip);

    /// <summary>Button width that <see cref="PrimaryButton"/> etc. would auto-size to.</summary>
    public static float ButtonWidth(string label, FontAwesomeIcon? icon = null)
    {
        var text = Visible(label);
        float iconW = 0f;
        if (icon.HasValue)
            using (Fonts.Icon.Push())
                iconW = ImGui.CalcTextSize(Glyph(icon.Value)).X;
        float textW = text.Length > 0 ? ImGui.CalcTextSize(text).X : 0f;
        float gap = iconW > 0 && textW > 0 ? Theme.S(8f) : 0f;
        return iconW + gap + textW + Theme.S(14f) * 2f;
    }

    private static bool ButtonCore(string label, FontAwesomeIcon? icon, ButtonKind kind,
        Vector2? size, bool enabled, string? tooltip)
    {
        var text = Visible(label);
        var id = string.IsNullOrEmpty(label) ? "##btn" : label;

        var req = size ?? Vector2.Zero;
        float avail = ImGui.GetContentRegionAvail().X;
        float w = req.X > 0 ? req.X : req.X < 0 ? MathF.Max(1f, avail + req.X) : ButtonWidth(label, icon);
        float h = req.Y > 0 ? req.Y : Theme.Space.ButtonHeight;

        var p = ImGui.GetCursorScreenPos();
        var key = ImGui.GetID(id);
        bool pressed = InvisibleItem(id, new Vector2(w, h), enabled);
        bool hovered = enabled && ImGui.IsItemHovered();
        bool held = enabled && ImGui.IsItemActive();

        // The hover color fades in and out; pressing shows at once.
        var hot = Motion.Hover(key, hovered);
        var (fill, ink, border) = held ? ButtonColors(kind, true, true) : MixColors(ButtonColors(kind, false, false), ButtonColors(kind, true, false), hot);
        float a = enabled ? 1f : DisabledAlpha;

        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(p, p + new Vector2(w, h), Theme.U32(fill with { W = fill.W * a }), Theme.Radius.Control);
        if (border.HasValue)
            dl.AddRect(p, p + new Vector2(w, h), Theme.U32(border.Value with { W = border.Value.W * a }),
                Theme.Radius.Control, ImDrawFlags.None, Theme.S(1f));

        DrawIconLabelCentered(dl, icon, text, p, new Vector2(w, h), ink with { W = ink.W * a });

        if (hovered) ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        if (tooltip != null && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            Tooltip(tooltip);

        return pressed && enabled;
    }

    private static (Vector4 Fill, Vector4 Ink, Vector4? Border) ButtonColors(ButtonKind kind, bool hovered, bool held)
    {
        switch (kind)
        {
            case ButtonKind.Primary:
                return (held ? Theme.AccentPressed : hovered ? Theme.AccentHover : Theme.Accent, Theme.OnAccent, null);
            case ButtonKind.Danger:
                var neg = Theme.Negative;
                return (held ? Theme.Darken(neg, 0.15f) : hovered ? Theme.Lighten(neg, 0.12f) : neg, Theme.OnNegative, null);
            case ButtonKind.Ghost:
                return (held ? Theme.AccentAlpha(0.25f) : hovered ? Theme.AccentAlpha(0.15f) : Theme.Transparent,
                    Theme.Accent, Theme.Accent);
            default:
                return (held ? Theme.BorderControl : hovered ? Theme.Raised2 : Theme.Raised, Theme.Ink, null);
        }
    }

    private static (Vector4 Fill, Vector4 Ink, Vector4? Border) MixColors(
        (Vector4 Fill, Vector4 Ink, Vector4? Border) from, (Vector4 Fill, Vector4 Ink, Vector4? Border) to, float t)
        => (Theme.Lerp(from.Fill, to.Fill, t), Theme.Lerp(from.Ink, to.Ink, t),
            from.Border is { } a && to.Border is { } b ? Theme.Lerp(a, b, t) : to.Border ?? from.Border);

    /// <summary>
    /// A round icon button at a screen position (Field fill; red on hover when <paramref name="danger"/>): the window's
    /// minimize / close and the modal's close. True when clicked.
    /// </summary>
    public static bool RoundButton(string id, Vector2 pos, float size, FontAwesomeIcon icon, string? tooltip = null, bool danger = false)
    {
        var dl = ImGui.GetWindowDrawList();
        ImGui.SetCursorScreenPos(pos);
        var key = ImGui.GetID(id);
        var clicked = ImGui.InvisibleButton(id, new Vector2(size, size));
        var hovered = ImGui.IsItemHovered();
        var hot = Motion.Hover(key, hovered);
        var fill = Theme.Lerp(Theme.Field, danger ? Theme.Negative : Theme.Raised, hot);
        var ink = Theme.Lerp(Theme.Dim, danger ? Theme.OnNegative : Theme.Ink, hot);
        dl.AddCircleFilled(pos + new Vector2(size * 0.5f), size * 0.5f, Theme.U32(fill), 32);
        DrawGlyphCentered(dl, icon, pos, new Vector2(size, size), ink);
        if (hovered)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            if (tooltip != null)
                Tooltip(tooltip);
        }
        return clicked;
    }

    /// <summary>Square icon button (Field fill, glyph centered). Size 0 = frame height.</summary>
    public static bool IconButton(FontAwesomeIcon icon, string id, string? tooltip = null, bool danger = false,
        bool enabled = true, float size = 0f)
    {
        float s = size > 0 ? size : ImGui.GetFrameHeight();
        var p = ImGui.GetCursorScreenPos();
        var itemId = string.IsNullOrEmpty(id) ? "##iconbtn" : id;
        var key = ImGui.GetID(itemId);
        bool pressed = InvisibleItem(itemId, new Vector2(s, s), enabled);
        bool hovered = enabled && ImGui.IsItemHovered();
        bool held = enabled && ImGui.IsItemActive();
        float a = enabled ? 1f : DisabledAlpha;

        var hot = Motion.Hover(key, hovered);
        var hotFill = danger ? Theme.Negative with { W = 0.22f } : Theme.Raised;
        Vector4 fill = held ? (danger ? Theme.Negative with { W = 0.35f } : Theme.Raised2) : Theme.Lerp(Theme.Field, hotFill, hot);
        Vector4 ink = Theme.Lerp(Theme.Dim, danger ? Theme.Negative : Theme.Ink, held ? 1f : hot);

        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(p, p + new Vector2(s, s), Theme.U32(fill with { W = fill.W * a }), Theme.Radius.Small);
        DrawGlyphCentered(dl, icon, p, new Vector2(s, s), ink with { W = ink.W * a });

        if (hovered) ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        if (tooltip != null && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            Tooltip(tooltip);

        return pressed && enabled;
    }

    // ───────────────────────── Toggle / Segmented ─────────────────────────

    /// <summary>iOS-style animated switch with a clickable label to its right.</summary>
    public static bool Toggle(string label, ref bool value, bool enabled = true, string? tooltip = null)
    {
        var text = Visible(label);
        uint key = ImGui.GetID(label);

        float trackW = Theme.S(34f), trackH = Theme.S(20f), knob = Theme.S(16f);
        float inset = (trackH - knob) * 0.5f;
        float h = MathF.Max(trackH, ImGui.GetFrameHeight());
        float labelGap = Theme.S(8f);
        float labelW = text.Length > 0 ? labelGap + ImGui.CalcTextSize(text).X : 0f;

        var p = ImGui.GetCursorScreenPos();
        bool pressed = InvisibleItem(string.IsNullOrEmpty(label) ? "##toggle" : label, new Vector2(trackW + labelW, h), enabled);
        bool hovered = enabled && ImGui.IsItemHovered();
        bool changed = false;
        if (pressed && enabled)
        {
            value = !value;
            changed = true;
        }

        float t = Motion.Approach(key, value ? 1f : 0f);

        float a = enabled ? 1f : DisabledAlpha;
        float top = p.Y + (h - trackH) * 0.5f;
        var tMin = new Vector2(p.X, top);
        var tMax = new Vector2(p.X + trackW, top + trackH);

        var offTrack = hovered ? Theme.Raised2 : Theme.Raised;
        var onTrack = hovered ? Theme.AccentHover : Theme.Accent;
        var track = Theme.Lerp(offTrack, onTrack, Theme.Ease(t));

        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(tMin, tMax, Theme.U32(track with { W = a }), Theme.Radius.Pill);
        if (t < 1f)
            dl.AddRect(tMin, tMax, Theme.U32(Theme.BorderControl with { W = (1f - t) * a }),
                Theme.Radius.Pill, ImDrawFlags.None, Theme.S(1f));

        float knobX = tMin.X + inset + knob * 0.5f + Theme.Ease(t) * (trackW - inset * 2f - knob);
        var knobCol = Theme.Lerp(Theme.Dim, Theme.OnAccent, t);
        dl.AddCircleFilled(new Vector2(knobX, top + trackH * 0.5f), knob * 0.5f, Theme.U32(knobCol with { W = a }), 24);

        if (text.Length > 0)
        {
            float lineH = ImGui.GetTextLineHeight();
            var ink = hovered ? Theme.Ink : Theme.Ink with { W = 0.92f };
            dl.AddText(new Vector2(p.X + trackW + labelGap, p.Y + (h - lineH) * 0.5f), Theme.U32(ink with { W = ink.W * a }), text);
        }

        if (hovered) ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        if (tooltip != null && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            Tooltip(tooltip);

        return changed;
    }

    /// <summary>Width <see cref="Toggle"/> takes for <paramref name="label"/> (switch, gap and the visible text), in the current font.</summary>
    public static float ToggleWidth(string label)
    {
        var text = Visible(label);
        return Theme.S(34f) + (text.Length > 0 ? Theme.S(8f) + ImGui.CalcTextSize(text).X : 0f);
    }

    // ───────────────────────── Checkbox ─────────────────────────

    /// <summary>
    /// Themed checkbox: a 16 px rounded box centered in <paramref name="height"/> (0 = frame height), accent fill with a
    /// check when ticked, a dash when <paramref name="mixed"/> (some of a group ticked). A "Visible##id" label is drawn to
    /// its right and is clickable too. Returns true on the frame it was clicked (<paramref name="value"/> flipped).
    /// </summary>
    public static bool Checkbox(string id, ref bool value, bool mixed = false, float height = 0f, string? tooltip = null,
        bool enabled = true)
    {
        var text = Visible(id);
        var itemId = string.IsNullOrEmpty(id) ? "##check" : id;
        float box = Theme.S(16f);
        float h = MathF.Max(box, height > 0f ? height : ImGui.GetFrameHeight());
        float labelGap = Theme.S(8f);
        float labelW = text.Length > 0 ? labelGap + ImGui.CalcTextSize(text).X : 0f;

        var p = ImGui.GetCursorScreenPos();
        uint key = ImGui.GetID(itemId);
        bool clicked = InvisibleItem(itemId, new Vector2(box + labelW, h), enabled) && enabled;
        bool hovered = enabled && ImGui.IsItemHovered();
        if (clicked)
            value = !value;
        float hot = Motion.Hover(key, hovered);
        float a = enabled ? 1f : DisabledAlpha;

        var dl = ImGui.GetWindowDrawList();
        var min = new Vector2(p.X, MathF.Round(p.Y + (h - box) * 0.5f));
        var max = min + new Vector2(box, box);
        float r = Theme.S(4f);

        if (value || mixed)
        {
            var fill = Theme.Lerp(Theme.Accent, Theme.AccentHover, hot);
            dl.AddRectFilled(min, max, Theme.U32(fill with { W = a }), r);
        }
        else
        {
            var fill = Theme.Lerp(Theme.Raised, Theme.Raised2, hot);
            dl.AddRectFilled(min, max, Theme.U32(fill with { W = a }), r);
            dl.AddRect(min, max, Theme.U32(Theme.BorderControl with { W = a }), r, ImDrawFlags.None, Theme.S(1f));
        }

        if (value)
        {
            DrawGlyphCentered(dl, FontAwesomeIcon.Check, min, new Vector2(box, box), Theme.OnAccent with { W = a });
        }
        else if (mixed)
        {
            float inset = Theme.S(4f);
            float midY = (min.Y + max.Y) * 0.5f;
            dl.AddRectFilled(new Vector2(min.X + inset, midY - Theme.S(1f)), new Vector2(max.X - inset, midY + Theme.S(1f)),
                Theme.U32(Theme.OnAccent with { W = a }), Theme.S(1f));
        }

        if (text.Length > 0)
        {
            float lineH = ImGui.GetTextLineHeight();
            var ink = hovered ? Theme.Ink : Theme.Ink with { W = 0.92f };
            dl.AddText(new Vector2(p.X + box + labelGap, MathF.Round(p.Y + (h - lineH) * 0.5f)), Theme.U32(ink with { W = ink.W * a }), text);
        }

        if (hovered)
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        if (tooltip != null && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            Tooltip(tooltip);
        return clicked;
    }

    /// <summary>
    /// Segmented control with a sliding accent pill. width &lt; 0 fills the available width,
    /// width == 0 sizes each segment to its label, width &gt; 0 is an explicit width.
    /// Returns true on the frame the selection changed.
    /// </summary>
    public static bool Segmented(string id, string[] options, ref int index, float width = -1f)
    {
        int count = options.Length;
        if (count == 0) return false;

        ImGui.PushID(id);
        try
        {
            uint key = ImGui.GetID("##segmented");
            float pad = Theme.S(3f);
            float h = MathF.Max(ImGui.GetFrameHeight(), Theme.S(30f));
            float segH = h - pad * 2f;

            // Per-segment scratch on the stack (no per-frame arrays); huge option lists fall back to the heap.
            Span<float> segW = count <= 64 ? stackalloc float[count] : new float[count];
            Span<float> segX = count <= 64 ? stackalloc float[count] : new float[count];
            Span<bool> hot = count <= 64 ? stackalloc bool[count] : new bool[count];
            float trackW;
            if (width == 0f)
            {
                for (int i = 0; i < count; i++)
                    segW[i] = SegmentWidth(options[i]);
                trackW = SegmentedWidth(options);
            }
            else
            {
                trackW = width < 0 ? MathF.Max(count * Theme.S(24f), ImGui.GetContentRegionAvail().X) : width;
                float even = (trackW - pad * 2f) / count;
                for (int i = 0; i < count; i++) segW[i] = even;
            }

            var origin = ImGui.GetCursorScreenPos();
            var dl = ImGui.GetWindowDrawList();
            dl.AddRectFilled(origin, origin + new Vector2(trackW, h), Theme.U32(Theme.Field), Theme.Radius.Control);
            dl.AddRect(origin, origin + new Vector2(trackW, h), Theme.U32(Theme.CardBorder),
                Theme.Radius.Control, ImDrawFlags.None, Theme.S(1f));

            float running = pad;
            for (int i = 0; i < count; i++)
            {
                segX[i] = running;
                running += segW[i];
            }

            bool changed = false;
            for (int i = 0; i < count; i++)
            {
                ImGui.SetCursorScreenPos(new Vector2(origin.X + segX[i], origin.Y + pad));
                ImGui.PushID(i);
                var segmentClicked = ImGui.InvisibleButton("##seg", new Vector2(MathF.Max(1f, segW[i]), segH));
                ImGui.PopID();
                if (segmentClicked && index != i)
                {
                    index = i;
                    changed = true;
                }
                hot[i] = ImGui.IsItemHovered();
                if (hot[i]) ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            }

            // The pill eases between segments; kept as fractions of the track so resizing the window doesn't animate it.
            int active = Math.Clamp(index, 0, count - 1);
            float pillX = trackW * Motion.Approach(key, segX[active] / trackW, snap: 0.0005f);
            float pillW = trackW * Motion.Approach(key + 1, segW[active] / trackW, snap: 0.0005f);

            var pillMin = new Vector2(origin.X + pillX, origin.Y + pad);
            var pillMax = new Vector2(pillMin.X + pillW, pillMin.Y + segH);
            dl.AddRectFilled(pillMin, pillMax, Theme.U32(Theme.Accent), Theme.Radius.Small);

            float lineH = ImGui.GetTextLineHeight();
            for (int i = 0; i < count; i++)
            {
                float left = origin.X + segX[i];
                float right = left + segW[i];
                float covered = segW[i] <= 0f ? 0f : Math.Clamp(
                    (MathF.Min(right, pillMax.X) - MathF.Max(left, pillMin.X)) / segW[i], 0f, 1f);

                if (hot[i] && covered < 0.5f)
                    dl.AddRectFilled(new Vector2(left, origin.Y + pad), new Vector2(right, origin.Y + pad + segH),
                        Theme.U32(Theme.Raised with { W = 1f - covered }), Theme.Radius.Small);

                string shown = Fit(options[i], segW[i] - Theme.S(12f));
                var ts = ImGui.CalcTextSize(shown);
                var ink = Theme.Lerp(hot[i] ? Theme.Ink : Theme.Dim, Theme.OnAccent, covered);
                dl.AddText(new Vector2(left + (segW[i] - ts.X) * 0.5f, origin.Y + (h - lineH) * 0.5f), Theme.U32(ink), shown);
            }

            ImGui.SetCursorScreenPos(origin);
            ImGui.Dummy(new Vector2(trackW, h));
            return changed;
        }
        finally
        {
            ImGui.PopID();
        }
    }

    private static float SegmentWidth(string option) => MathF.Ceiling(ImGui.CalcTextSize(option).X) + Theme.S(14f) * 2f;

    /// <summary>Width <see cref="Segmented"/> takes with width 0 (each segment sized to its label), in the current font.</summary>
    public static float SegmentedWidth(string[] options)
    {
        float total = Theme.S(3f) * 2f;
        foreach (var option in options)
            total += SegmentWidth(option);
        return total;
    }

    // ───────────────────────── Card ─────────────────────────

    /// <summary>
    /// A Field-filled rounded card with a 1px border, filling the available width. Content is
    /// drawn inside with CardPad padding. Use as <c>using (W.Card("id", "TITLE")) { ... }</c>.
    /// Inside a card, use <see cref="Avail"/> instead of GetContentRegionAvail().X for fill widths
    /// (the right padding can't be expressed to ImGui). Default item widths already respect it.
    /// Nested cards are drawn as an outline only (ChannelsSplit cannot nest).
    /// </summary>
    public static IDisposable Card(string id, string? title = null, string? rightNote = null)
        => new CardScope(id, title, rightNote);

    /// <summary>Available width at the cursor, minus the right padding of any enclosing cards.</summary>
    public static float Avail() => MathF.Max(1f, ImGui.GetContentRegionAvail().X - (tableDepth > 0 ? 0f : cardRightPad));

    private sealed class CardScope : IDisposable
    {
        private readonly ImDrawListPtr dl;
        private readonly Vector2 min;
        private readonly float width;
        private readonly float pad;
        private readonly bool split;
        private readonly uint windowId;
        private bool disposed;

        public CardScope(string id, string? title, string? rightNote)
        {
            windowId = CurrentWindowId();
            ImGui.PushID(id);
            dl = ImGui.GetWindowDrawList();
            pad = Theme.Space.CardPad;
            min = ImGui.GetCursorScreenPos();
            width = MathF.Max(1f, ImGui.GetContentRegionAvail().X - cardRightPad);

            split = cardDepth == 0;
            if (split)
            {
                dl.ChannelsSplit(2);
                dl.ChannelsSetCurrent(1);
            }
            cardDepth++;
            cardRightPad += pad;

            ImGui.SetCursorScreenPos(min + new Vector2(pad, pad));
            ImGui.Indent(pad);
            ImGui.PushItemWidth(-cardRightPad);

            if (title != null || rightNote != null)
            {
                var hp = ImGui.GetCursorScreenPos();
                float lineH;
                using (Fonts.Label.Push())
                {
                    lineH = ImGui.GetTextLineHeight();
                    if (!string.IsNullOrEmpty(title))
                        TrackedCaps(dl, hp, title, Theme.Slate);
                }
                if (!string.IsNullOrEmpty(rightNote))
                {
                    using (Fonts.Small.Push())
                    {
                        var ns = ImGui.CalcTextSize(rightNote);
                        dl.AddText(new Vector2(min.X + width - pad - ns.X, hp.Y + (lineH - ns.Y) * 0.5f),
                            Theme.U32(Theme.Dim), rightNote);
                        lineH = MathF.Max(lineH, ns.Y);
                    }
                }
                ImGui.Dummy(new Vector2(width - pad * 2f, lineH));
                ImGui.Dummy(new Vector2(0f, MathF.Max(0f, Theme.S(4f) - ImGui.GetStyle().ItemSpacing.Y)));
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;

            cardRightPad -= pad;
            cardDepth--;

            // Unwinding after an exception with a child window (or popup) the page opened still current: the pops below
            // would hit that window's stacks. Only merge the draw list; KitWindow's recovery closes the rest.
            if (CurrentWindowId() != windowId)
            {
                if (split)
                    dl.ChannelsMerge();
                return;
            }

            ImGui.PopItemWidth();
            ImGui.Unindent(pad);

            float bottom = ImGui.GetCursorScreenPos().Y - ImGui.GetStyle().ItemSpacing.Y + pad;
            // Also cover the last item drawn: a trailing SameLine() leaves the cursor at that item's top.
            bottom = MathF.Max(bottom, ImGui.GetItemRectMax().Y + pad);
            bottom = MathF.Max(bottom, min.Y + pad * 2f);
            var max = new Vector2(min.X + width, bottom);

            if (split)
            {
                dl.ChannelsSetCurrent(0);
                DrawCardSurface(dl, min, max);
                dl.ChannelsMerge();
            }
            else
            {
                dl.AddRect(min, max, Theme.U32(Theme.CardBorder), Theme.Radius.Card, ImDrawFlags.None, Theme.S(1f));
            }

            ImGui.SetCursorScreenPos(new Vector2(min.X, bottom));
            ImGui.Dummy(new Vector2(width, 0f));
            ImGui.PopID();
        }
    }

    /// <summary>
    /// A card's surface: a soft drop shadow, the fill, the border,
    /// and a faint highlight along the top edge so it reads as lit from above.
    /// </summary>
    private static void DrawCardSurface(ImDrawListPtr dl, Vector2 min, Vector2 max)
    {
        var r = Theme.Radius.Card;
        for (var i = 3; i >= 1; i--)
        {
            var spread = Theme.S(1.5f) * i;
            var drop = Theme.S(2f) * i;
            dl.AddRectFilled(min + new Vector2(-spread, drop - spread), max + new Vector2(spread, drop + spread),
                             Theme.U32(Theme.Shadow(0.07f)), r + spread);
        }
        dl.AddRectFilled(min, max, Theme.U32(Theme.Field), r);
        dl.AddRect(min, max, Theme.U32(Theme.CardBorder), r, ImDrawFlags.None, Theme.S(1f));
        dl.AddLine(new Vector2(min.X + r, min.Y + Theme.S(1f)), new Vector2(max.X - r, min.Y + Theme.S(1f)),
                   Theme.U32(Theme.Wash(0.06f)), Theme.S(1f));
    }

    // ───────────────────────── Text ─────────────────────────

    /// <summary>Tracked uppercase heading (Label font, Slate, 0.12em letter spacing).</summary>
    public static void Heading(string text)
    {
        using (Fonts.Label.Push())
        {
            var p = ImGui.GetCursorScreenPos();
            float w = TrackedCaps(ImGui.GetWindowDrawList(), p, text, Theme.Slate);
            ImGui.Dummy(new Vector2(w, ImGui.GetTextLineHeight()));
        }
    }

    /// <summary>
    /// Draws uppercase text glyph by glyph with extra letter spacing (fraction of the font size)
    /// in the CURRENT font. Returns the drawn width. Does not advance the cursor.
    /// </summary>
    public static float TrackedCaps(ImDrawListPtr dl, Vector2 pos, string text, Vector4 color, float tracking = 0.12f)
    {
        string shown = Upper(text);
        uint col = Theme.U32(color);
        float extra = ImGui.GetFontSize() * tracking;
        float x = pos.X;
        for (int i = 0; i < shown.Length; i++)
        {
            string glyph = CharGlyph(shown[i]);
            dl.AddText(new Vector2(x, pos.Y), col, glyph);
            x += ImGui.CalcTextSize(glyph).X + (i < shown.Length - 1 ? extra : 0f);
        }
        return x - pos.X;
    }

    /// <summary>Width <see cref="TrackedCaps"/> would draw in the current font.</summary>
    public static float TrackedCapsWidth(string text, float tracking = 0.12f)
    {
        string shown = Upper(text);
        float extra = ImGui.GetFontSize() * tracking;
        float x = 0f;
        for (int i = 0; i < shown.Length; i++)
            x += ImGui.CalcTextSize(CharGlyph(shown[i])).X + (i < shown.Length - 1 ? extra : 0f);
        return x;
    }

    /// <summary>Upper-cased text, cached (headings and captions repeat every frame).</summary>
    private static string Upper(string text)
    {
        if (UpperCache.TryGetValue(text, out var upper))
            return upper;
        if (UpperCache.Count >= UpperCacheLimit)
            UpperCache.Clear(); // one-off texts (item names...) can't grow it forever
        return UpperCache[text] = text.ToUpperInvariant();
    }

    /// <summary>A one-character string for drawing glyph by glyph, cached (ASCII in a table, the rest on first use).</summary>
    private static string CharGlyph(char c)
    {
        if (c < 128)
            return AsciiGlyphs[c];
        if (!OtherGlyphs.TryGetValue(c, out var glyph))
            OtherGlyphs[c] = glyph = c.ToString();
        return glyph;
    }

    private static string[] BuildAsciiGlyphs()
    {
        var glyphs = new string[128];
        for (int i = 0; i < glyphs.Length; i++)
            glyphs[i] = ((char)i).ToString();
        return glyphs;
    }

    /// <summary>A FontAwesome icon's glyph string, cached (ToIconString allocates).</summary>
    public static string Glyph(FontAwesomeIcon icon)
    {
        if (!IconStrings.TryGetValue(icon, out var glyph))
            IconStrings[icon] = glyph = icon.ToIconString();
        return glyph;
    }

    /// <summary>Page title in the Display font, with an optional Dim subtitle beneath.</summary>
    public static void PageTitle(string title, string? subtitle = null)
    {
        using (Fonts.Display.Push())
            ImGui.TextColored(Theme.Ink, title);
        if (!string.IsNullOrEmpty(subtitle))
            ImGui.TextColored(Theme.Dim, subtitle);
    }

    /// <summary>Small "?" circle in Faint; its tooltip wraps at 22em.</summary>
    public static void HelpMark(string text)
    {
        float side = MathF.Round(ImGui.GetTextLineHeight());
        var p = ImGui.GetCursorScreenPos();
        ImGui.PushID(text);
        ImGui.InvisibleButton("##help", new Vector2(side, side));
        ImGui.PopID();
        bool hovered = ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled);

        var dl = ImGui.GetWindowDrawList();
        var c = p + new Vector2(side * 0.5f);
        var col = hovered ? Theme.Dim : Theme.Faint;
        dl.AddCircle(c, side * 0.5f - Theme.S(1f), Theme.U32(col), 20, Theme.S(1.2f));
        using (Fonts.Small.Push())
        {
            var qs = ImGui.CalcTextSize("?");
            dl.AddText(c - qs * 0.5f, Theme.U32(col), "?");
        }

        if (hovered) Tooltip(text);
    }

    /// <summary>Tooltip with (10,8) padding, wrapping at 22em.</summary>
    public static void Tooltip(string text)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Theme.S(10f, 8f));
        // BeginTooltip returns void in Dalamud.Bindings.ImGui (the tooltip always opens), so EndTooltip is always owed.
        ImGui.BeginTooltip();
        ImGui.PushTextWrapPos(ImGui.GetFontSize() * 22f);
        ImGui.TextUnformatted(text);
        ImGui.PopTextWrapPos();
        ImGui.EndTooltip();
        ImGui.PopStyleVar();
    }

    /// <summary>
    /// Rounded chip for status/counts ("HQ", "Tab 3", "Running"). Small font. With <paramref name="status"/> the pill
    /// carries good / bad meaning: in colorblind mode a Positive / Negative <paramref name="fg"/> also gets a check / cross.
    /// </summary>
    public static void Pill(string text, Vector4 bg, Vector4 fg, bool status = false)
    {
        using (Fonts.Small.Push())
        {
            var p = ImGui.GetCursorScreenPos();
            var size = DrawPillCore(ImGui.GetWindowDrawList(), p, text, bg, fg, StatusShape(fg, status), PillPad);
            ImGui.Dummy(size);
        }
    }

    /// <summary>Width <see cref="Pill"/> / <see cref="DrawPill"/> would take (pass the same fg / status to count a colorblind glyph).</summary>
    public static float PillWidth(string text, Vector4? fg = null, bool status = false)
    {
        using (Fonts.Small.Push())
            return BadgeContentWidth(text, fg is { } c ? StatusShape(c, status) : null) + PillPad.X * 2f;
    }

    /// <summary>
    /// <see cref="Pill"/> drawn straight onto a draw list (no layout item): left edge at <paramref name="pos"/>.X,
    /// centered on <paramref name="pos"/>.Y. For tiles and custom rows. <paramref name="pad"/> overrides the padding
    /// (scaled px). Returns its width.
    /// </summary>
    public static float DrawPill(ImDrawListPtr dl, Vector2 pos, string text, Vector4 bg, Vector4 fg, bool status = false,
        Vector2? pad = null)
    {
        using (Fonts.Small.Push())
        {
            var padding = pad ?? PillPad;
            var h = ImGui.GetTextLineHeight() + padding.Y * 2f;
            return DrawPillCore(dl, new Vector2(pos.X, MathF.Round(pos.Y - h * 0.5f)), text, bg, fg, StatusShape(fg, status), padding).X;
        }
    }

    private static Vector2 PillPad => Theme.S(8f, 2f);

    /// <summary>Pill surface and text at a top-left corner, in the current font. Returns its size.</summary>
    private static Vector2 DrawPillCore(ImDrawListPtr dl, Vector2 min, string text, Vector4 bg, Vector4 fg, FontAwesomeIcon? shape, Vector2 pad)
    {
        var size = new Vector2(BadgeContentWidth(text, shape) + pad.X * 2f, ImGui.GetTextLineHeight() + pad.Y * 2f);
        dl.AddRectFilled(min, min + size, Theme.U32(bg), Theme.Radius.Pill);
        DrawBadgeContent(dl, new Vector2(min.X + pad.X, min.Y + pad.Y), text, fg, shape);
        return size;
    }

    // ───────────────────────── Badge internals (pills and chips) ─────────────────────────

    /// <summary>The colorblind glyph a status badge carries, if any.</summary>
    private static FontAwesomeIcon? StatusShape(Vector4 color, bool status) => status ? Theme.ColorblindShape(color) : null;

    private static float StatusGlyphBox => ImGui.GetTextLineHeight() * 0.8f;

    /// <summary>Text width plus room for a status glyph, in the current font.</summary>
    private static float BadgeContentWidth(string text, FontAwesomeIcon? shape)
        => ImGui.CalcTextSize(text).X + (shape != null ? StatusGlyphBox + Theme.S(4f) : 0f);

    /// <summary>Optional status glyph, then the text, starting at the content's top-left.</summary>
    private static void DrawBadgeContent(ImDrawListPtr dl, Vector2 at, string text, Vector4 color, FontAwesomeIcon? shape)
    {
        var x = at.X;
        if (shape is { } icon)
        {
            var box = StatusGlyphBox;
            DrawGlyphAt(dl, icon, new Vector2(x + box * 0.5f, at.Y + ImGui.GetTextLineHeight() * 0.5f), box, color);
            x += box + Theme.S(4f);
        }
        dl.AddText(new Vector2(x, at.Y), Theme.U32(color), text);
    }

    /// <summary>
    /// Small status dot, vertically centered on the text line. Pulses when asked. In colorblind mode a Positive /
    /// Negative dot is drawn as a check / cross instead.
    /// </summary>
    public static void StatusDot(Vector4 color, bool pulse)
    {
        float r = Theme.S(4f);
        float h = ImGui.GetTextLineHeight();
        var p = ImGui.GetCursorScreenPos();
        ImGui.Dummy(new Vector2(r * 2f, h));
        StatusDot(ImGui.GetWindowDrawList(), new Vector2(p.X + r, p.Y + h * 0.5f), color, pulse, r);
    }

    /// <summary>
    /// <see cref="StatusDot(Vector4, bool)"/> drawn at a center point (no layout item), for tiles and custom rows.
    /// <paramref name="radius"/> 0 = the standard 4 px.
    /// </summary>
    public static void StatusDot(ImDrawListPtr dl, Vector2 center, Vector4 color, bool pulse, float radius = 0f)
    {
        float r = radius > 0f ? radius : Theme.S(4f);
        float alpha = pulse ? Motion.Pulse() : 1f;
        var ink = color with { W = color.W * alpha };
        if (Theme.ColorblindShape(color) is { } shape)
            DrawGlyphAt(dl, shape, center, r * 2.75f, ink);
        else
            dl.AddCircleFilled(center, r, Theme.U32(ink), 16);
    }

    /// <summary>A thin rounded progress bar (Raised track, accent fill) drawn at a screen position.</summary>
    public static void ProgressBar(ImDrawListPtr dl, Vector2 min, float width, float height, float fraction)
    {
        dl.AddRectFilled(min, min + new Vector2(width, height), Theme.U32(Theme.Raised), Theme.Radius.Pill);
        if (fraction > 0f)
            dl.AddRectFilled(min, min + new Vector2(MathF.Max(height, width * Math.Clamp(fraction, 0f, 1f)), height),
                             Theme.U32(Theme.Accent), Theme.Radius.Pill);
    }

    /// <summary>
    /// Starts a fresh drawing surface (a modal or popup body drawn while a card may be open): card nesting, card padding
    /// and table state are set aside until disposed, so the surface's own cards draw normally.
    /// </summary>
    public static IDisposable NewSurface() => new SurfaceScope();

    private sealed class SurfaceScope : IDisposable
    {
        private readonly int depth = cardDepth, tables = tableDepth;
        private readonly float rightPad = cardRightPad;

        public SurfaceScope()
        {
            cardDepth = 0;
            cardRightPad = 0f;
            tableDepth = 0;
        }

        public void Dispose()
        {
            cardDepth = depth;
            cardRightPad = rightPad;
            tableDepth = tables;
        }
    }

    /// <summary>Truncates with an ellipsis so the text fits maxWidth in the current font.</summary>
    public static string Fit(string text, float maxWidth)
    {
        if (string.IsNullOrEmpty(text)) return text ?? string.Empty;
        if (maxWidth <= 0) return string.Empty; // no room: draw nothing rather than overflow
        if (ImGui.CalcTextSize(text).X <= maxWidth) return text;

        const string ellipsis = "…";
        float budget = maxWidth - ImGui.CalcTextSize(ellipsis).X;
        if (budget <= 0) return ellipsis;

        // Binary search the longest prefix that fits.
        int lo = 0, hi = text.Length - 1;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (ImGui.CalcTextSize(text.Substring(0, mid)).X <= budget) lo = mid;
            else hi = mid - 1;
        }
        return lo <= 0 ? ellipsis : text.Substring(0, lo).TrimEnd() + ellipsis;
    }

    // ───────────────────────── Inputs ─────────────────────────

    /// <summary>
    /// Rounded search input with a magnifier glyph inside and an ✕ clear button when non-empty.
    /// width &lt;= 0 fills the available width (plus width when negative). Returns true when the
    /// text changed (typed or cleared). <paramref name="error"/> draws a red border (an invalid expression...).
    /// </summary>
    public static bool SearchBox(string id, ref string text, string hint, float width, int maxLength = 512, bool error = false)
    {
        ImGui.PushID(id);
        try
        {
            float h = ImGui.GetFrameHeight();
            float avail = ImGui.GetContentRegionAvail().X;
            float w = width > 0 ? width : MathF.Max(h * 3f, avail + width);
            var p = ImGui.GetCursorScreenPos();
            var dl = ImGui.GetWindowDrawList();

            string glyph = Glyph(FontAwesomeIcon.Search);
            float iconW;
            using (Fonts.Icon.Push())
                iconW = ImGui.CalcTextSize(glyph).X;

            float iconLeft = Theme.S(12f);
            float inputX = iconLeft + iconW + Theme.S(2f);
            float clearW = text.Length > 0 ? h : 0f;

            dl.AddRectFilled(p, p + new Vector2(w, h), Theme.U32(Theme.Field), Theme.Radius.Control);

            ImGui.SetCursorScreenPos(new Vector2(p.X + inputX, p.Y));
            ImGui.SetNextItemWidth(MathF.Max(Theme.S(20f), w - inputX - clearW));
            bool changed;
            using (new Theme.StyleScope()
                       .Color(ImGuiCol.FrameBg, Theme.Transparent)
                       .Color(ImGuiCol.FrameBgHovered, Theme.Transparent)
                       .Color(ImGuiCol.FrameBgActive, Theme.Transparent)
                       .Var(ImGuiStyleVar.FramePadding, new Vector2(Theme.S(8f), ImGui.GetStyle().FramePadding.Y)))
            {
                changed = ImGui.InputTextWithHint("##input", hint, ref text, maxLength);
            }
            bool active = ImGui.IsItemActive();
            bool hovered = ImGui.IsItemHovered();

            var border = error ? Theme.Negative with { W = 0.8f }
                : active ? Theme.AccentAlpha(0.7f) : hovered ? Theme.BorderControl : Theme.CardBorder;
            dl.AddRect(p, p + new Vector2(w, h), Theme.U32(border), Theme.Radius.Control, ImDrawFlags.None,
                Theme.S(active || error ? 1.5f : 1f));

            using (Fonts.Icon.Push())
            {
                var gs = ImGui.CalcTextSize(glyph);
                dl.AddText(new Vector2(p.X + iconLeft, p.Y + (h - gs.Y) * 0.5f),
                    Theme.U32(active ? Theme.Dim : Theme.Faint), glyph);
            }

            if (clearW > 0f)
            {
                var cp = new Vector2(p.X + w - clearW, p.Y);
                ImGui.SetCursorScreenPos(cp);
                if (ImGui.InvisibleButton("##clear", new Vector2(clearW, h)))
                {
                    text = string.Empty;
                    changed = true;
                }
                bool clearHot = ImGui.IsItemHovered();
                if (clearHot)
                {
                    ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                    dl.AddCircleFilled(cp + new Vector2(clearW * 0.5f, h * 0.5f), h * 0.32f, Theme.U32(Theme.Raised), 20);
                }
                DrawGlyphCentered(dl, FontAwesomeIcon.Times, cp, new Vector2(clearW, h),
                    clearHot ? Theme.Ink : Theme.Faint);
            }

            ImGui.SetCursorScreenPos(p);
            ImGui.Dummy(new Vector2(w, h));
            return changed;
        }
        finally
        {
            ImGui.PopID();
        }
    }

    /// <summary>
    /// A plain themed text input with a hint (ImGui's InputTextWithHint in the kit's frame style), optionally in an error
    /// state: a red tint and a red border. width &lt;= 0 fills the available width (plus width when negative). The input
    /// stays the last item, so ImGui.IsItemDeactivatedAfterEdit() works after it. Returns true when the text changed.
    /// </summary>
    public static bool TextInput(string id, ref string text, string hint, float width, int maxLength = 256, bool error = false,
        ImGuiInputTextFlags flags = ImGuiInputTextFlags.None)
    {
        float w = width > 0 ? width : MathF.Max(ImGui.GetFrameHeight() * 3f, Avail() + width);
        ImGui.SetNextItemWidth(w);
        bool changed;
        using (error ? new Theme.StyleScope().Color(ImGuiCol.FrameBg, Theme.Negative with { W = 0.10f }) : null)
            changed = ImGui.InputTextWithHint(id, hint, ref text, maxLength, flags);
        if (error)
            ImGui.GetWindowDrawList().AddRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax(), Theme.U32(Theme.Negative with { W = 0.7f }),
                Theme.Radius.Control, ImDrawFlags.None, Theme.S(1f));
        return changed;
    }

    // ───────────────────────── Item icons ─────────────────────────

    /// <summary>
    /// Game icon for an item (Lumina Item.Icon, cached per item), rounded. Draws a Raised
    /// placeholder while the texture loads or if it is unavailable. Never throws.
    /// </summary>
    public static void ItemIcon(uint itemId, bool hq, float size)
    {
        var p = ImGui.GetCursorScreenPos();
        var sz = new Vector2(size, size);
        ImGui.Dummy(sz);

        // Offscreen rows (clipped) skip texture requests entirely.
        if (!ImGui.IsItemVisible()) return;

        var dl = ImGui.GetWindowDrawList();
        float rounding = MathF.Min(Theme.Radius.Chip, size * 0.25f);

        try
        {
            uint iconId = GetItemIconId(itemId);
            if (iconId != 0 && TryGetIconWrap(iconId, hq, out var handle))
            {
                dl.AddImageRounded(handle, p, p + sz, Vector2.Zero, Vector2.One,
                    Theme.U32(new Vector4(1f, 1f, 1f, 1f)), rounding);
                return;
            }
        }
        catch (Exception ex)
        {
            Kit.Log?.Verbose($"ItemIcon({itemId}) failed: {ex.Message}");
        }

        dl.AddRectFilled(p, p + sz, Theme.U32(Theme.Raised), rounding);
    }

    private static bool TryGetIconWrap(uint iconId, bool hq, out ImTextureID handle)
    {
        handle = default;
        var tex = Kit.Textures!.GetFromGameIcon(new GameIconLookup(iconId, hq));
        if (tex.TryGetWrap(out var wrap, out var error) && wrap != null)
        {
            handle = wrap.Handle;
            return true;
        }

        // An HQ variant that does not exist fails (not "still loading"): fall back to NQ.
        if (hq && error != null)
        {
            tex = Kit.Textures!.GetFromGameIcon(new GameIconLookup(iconId, false));
            if (tex.TryGetWrap(out wrap, out _) && wrap != null)
            {
                handle = wrap.Handle;
                return true;
            }
        }
        return false;
    }

    private static uint GetItemIconId(uint itemId)
    {
        // Normalise HQ (+1,000,000) and collectable (+500,000) encodings to the base row id.
        uint baseId = itemId >= 1_000_000 ? itemId - 1_000_000 : itemId >= 500_000 ? itemId - 500_000 : itemId;
        if (IconIdCache.TryGetValue(baseId, out var cached)) return cached;

        uint iconId = 0;
        var sheet = Kit.Data?.GetExcelSheet<Lumina.Excel.Sheets.Item>();
        if (sheet != null && sheet.TryGetRow(baseId, out var row))
            iconId = row.Icon;
        IconIdCache[baseId] = iconId;
        return iconId;
    }

    // ───────────────────────── Shared drawing helpers ─────────────────────────

    /// <summary>Draws a FontAwesome glyph (the icon font, or the large one) centered in a rect.</summary>
    public static void DrawGlyphCentered(ImDrawListPtr dl, FontAwesomeIcon icon, Vector2 min, Vector2 size, Vector4 color, bool large = false)
    {
        string glyph = Glyph(icon);
        using ((large ? Fonts.IconLarge : Fonts.Icon).Push())
        {
            var gs = ImGui.CalcTextSize(glyph);
            dl.AddText(new Vector2(MathF.Round(min.X + (size.X - gs.X) * 0.5f), MathF.Round(min.Y + (size.Y - gs.Y) * 0.5f)),
                Theme.U32(color), glyph);
        }
    }

    /// <summary>Draws a FontAwesome glyph at any pixel size (the icon font, scaled), centered on a point.</summary>
    public static void DrawGlyphAt(ImDrawListPtr dl, FontAwesomeIcon icon, Vector2 center, float px, Vector4 color)
    {
        string glyph = Glyph(icon);
        using (Fonts.Icon.Push())
        {
            var font = ImGui.GetFont();
            var gs = ImGui.CalcTextSize(glyph) * (px / MathF.Max(1f, ImGui.GetFontSize()));
            dl.AddText(font, px, new Vector2(MathF.Round(center.X - gs.X * 0.5f), MathF.Round(center.Y - gs.Y * 0.5f)),
                Theme.U32(color), glyph);
        }
    }

    /// <summary>Draws an optional icon + label (current font) centered together in a rect.</summary>
    public static void DrawIconLabelCentered(ImDrawListPtr dl, FontAwesomeIcon? icon, string text, Vector2 min, Vector2 size, Vector4 color)
    {
        uint col = Theme.U32(color);
        string glyph = icon is { } i ? Glyph(i) : string.Empty;
        Vector2 gs = Vector2.Zero;
        if (icon.HasValue)
            using (Fonts.Icon.Push())
                gs = ImGui.CalcTextSize(glyph);

        var ts = text.Length > 0 ? ImGui.CalcTextSize(text) : Vector2.Zero;
        float gap = gs.X > 0 && ts.X > 0 ? Theme.S(8f) : 0f;
        float total = gs.X + gap + ts.X;
        float x = MathF.Round(min.X + (size.X - total) * 0.5f);
        float midY = min.Y + size.Y * 0.5f;

        if (icon.HasValue)
            using (Fonts.Icon.Push())
                dl.AddText(new Vector2(x, MathF.Round(midY - gs.Y * 0.5f)), col, glyph);
        if (ts.X > 0)
            dl.AddText(new Vector2(x + gs.X + gap, MathF.Round(midY - ts.Y * 0.5f)), col, text);
    }

    /// <summary>The visible part of an ImGui label ("Text##id" → "Text").</summary>
    public static string Visible(string label)
    {
        if (string.IsNullOrEmpty(label)) return string.Empty;
        int marker = label.IndexOf("##", StringComparison.Ordinal);
        return marker >= 0 ? label.Substring(0, marker) : label;
    }

    /// <summary>
    /// The ImGui ID of the window being drawn into (0 if none). Scopes compare it on dispose, so after an exception that
    /// left a child window open they don't pop another window's stacks.
    /// </summary>
    internal static uint CurrentWindowId()
    {
        var window = ImGuiP.GetCurrentWindow();
        return window.IsNull ? 0u : window.ID;
    }

    /// <summary>InvisibleButton that can be disabled while still reporting hover for tooltips.</summary>
    private static bool InvisibleItem(string id, Vector2 size, bool enabled)
    {
        size = new Vector2(MathF.Max(1f, size.X), MathF.Max(1f, size.Y));
        if (!enabled) ImGui.BeginDisabled();
        bool pressed = ImGui.InvisibleButton(id, size);
        if (!enabled) ImGui.EndDisabled();
        return pressed;
    }
}
