using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace phys1ksUI;

/// <summary>More widgets: foldable cards, compact buttons, banners, tables, sidebar rows, links, figures.</summary>
internal static partial class W
{

    /// <summary>Open <see cref="Table"/>s: inside a cell, <see cref="Avail"/> is the cell's width (no card padding to take off).</summary>
    private static int tableDepth;

    /// <summary>
    /// Called at the start of each frame (Kit's frame hook) and of each <see cref="KitWindow"/>: if a draw threw mid-card,
    /// the card nesting would otherwise stay off for good (every card drawn as an outline).
    /// </summary>
    internal static void ResetFrame()
    {
        cardDepth = 0;
        cardRightPad = 0f;
        tableDepth = 0;
    }

    // ───────────────────────── Foldable card ─────────────────────────

    private const float FoldMs = 220f;

    /// <summary>A fold card's state: open or not, and the animation between.</summary>
    private sealed class FoldState
    {
        public bool Open;
        public bool DefaultOpen; // what a fresh state would say: a stale state that still matches it can be dropped
        public float From;   // progress when the last toggle happened
        public long Tick;    // when it happened
        public float Height; // the content's full height, measured every frame it's drawn
        public int Frame;    // last frame it was drawn

        /// <summary>0 folded .. 1 open, eased out; the full distance takes <see cref="FoldMs"/>.</summary>
        public float Progress() => Motion.TweenOut(From, Open ? 1f : 0f, Tick, FoldMs);
    }

    private static readonly Dictionary<uint, FoldState> Folds = new();
    private static int lastFoldSweep;

    /// <summary>
    /// Drops fold states no card has drawn in a while (closed pages, one-off ids), like <see cref="Motion"/> does with its
    /// values. Only states still at their default are dropped: one the user folded or unfolded is kept, so it comes back
    /// the way they left it.
    /// </summary>
    private static void SweepFolds(int frame)
    {
        if (frame - lastFoldSweep < Motion.StaleAfterFrames)
            return;
        lastFoldSweep = frame;
        List<uint>? stale = null;
        foreach (var (key, state) in Folds)
            if (state.Open == state.DefaultOpen && Motion.IsStale(state.Frame, frame))
                (stale ??= []).Add(key);
        if (stale != null)
            foreach (var key in stale)
                Folds.Remove(key);
    }

    /// <summary>
    /// A <see cref="Card"/> whose title row folds it (chevron, tracked caps title, right-hand note). Draw the contents
    /// only when <paramref name="open"/>: <c>using (W.FoldCard("id", "Title", "3 items", out var open)) if (open) { ... }</c>.
    /// Opening and closing animate: the content is clipped to a height easing between 0 and its full height, and fades,
    /// so <paramref name="open"/> stays true while it's still closing.
    /// </summary>
    public static IDisposable FoldCard(string id, string title, string? rightNote, out bool open, bool defaultOpen = true,
                                       Vector4? noteColor = null)
    {
        var scope = new CardScope(id, null, null);
        var key = ImGui.GetID("##fold");
        var frame = ImGui.GetFrameCount();
        SweepFolds(frame);
        if (!Folds.TryGetValue(key, out var state))
            Folds[key] = state = new FoldState { Open = defaultOpen, DefaultOpen = defaultOpen, From = defaultOpen ? 1f : 0f };
        state.Frame = frame;

        var dl = ImGui.GetWindowDrawList();
        var p = ImGui.GetCursorScreenPos();
        var width = Avail();
        float lineH;
        using (Fonts.Label.Push())
            lineH = ImGui.GetTextLineHeight();
        var rowH = MathF.Max(lineH, Theme.S(18f));

        if (ImGui.InvisibleButton("##foldHeader", new Vector2(width, rowH)))
        {
            state.From = state.Progress();
            state.Open = !state.Open;
            state.Tick = Environment.TickCount64;
        }
        var hovered = ImGui.IsItemHovered();
        if (hovered)
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

        DrawGlyphCentered(dl, state.Open ? FontAwesomeIcon.ChevronDown : FontAwesomeIcon.ChevronRight, p, new Vector2(Theme.S(14f), rowH),
                          hovered ? Theme.Ink : Theme.Faint);
        using (Fonts.Label.Push())
            TrackedCaps(dl, new Vector2(p.X + Theme.S(20f), p.Y + (rowH - lineH) * 0.5f), title, hovered ? Theme.Ink : Theme.Slate);
        if (!string.IsNullOrEmpty(rightNote))
        {
            using (Fonts.Small.Push())
            {
                var ns = ImGui.CalcTextSize(rightNote);
                dl.AddText(new Vector2(p.X + width - ns.X, p.Y + (rowH - ns.Y) * 0.5f), Theme.U32(noteColor ?? Theme.Dim), rightNote);
            }
        }

        var progress = state.Progress();
        open = progress > 0.001f;
        return new FoldScope(scope, state, open ? progress : 0f);
    }

    /// <summary>
    /// The content part of a fold card. While animating it clips the content to the eased height and fades it; on close
    /// it measures the content, puts the cursor at the eased height (so the card's edge follows), then closes the card and
    /// leaves the gap to the next one (fold cards come in stacks).
    /// </summary>
    private sealed class FoldScope : IDisposable
    {
        private readonly IDisposable card;
        private readonly FoldState state;
        private readonly float progress;
        private readonly Vector2 start;
        private readonly bool animating;
        private readonly IDisposable? fade;
        private readonly uint windowId;
        private bool disposed;

        public FoldScope(IDisposable card, FoldState state, float progress)
        {
            windowId = CurrentWindowId();
            this.card = card;
            this.state = state;
            this.progress = progress;
            start = ImGui.GetCursorScreenPos(); // right under the title row: where a folded card ends
            animating = progress > 0f && progress < 0.999f;
            if (animating)
            {
                ImGui.PushClipRect(new Vector2(-100000f, start.Y), new Vector2(100000f, start.Y + state.Height * progress), true);
                fade = new Theme.StyleScope().Var(ImGuiStyleVar.Alpha, MathF.Max(0.001f, progress * ImGui.GetStyle().Alpha));
            }
            if (progress > 0f)
                Gap(4f);
        }

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;

            if (KitRecovery.Unwinding || CurrentWindowId() != windowId)
            {
                // A throw with something still open: popping the clip rect now could hit another window. Recovery pops
                // it once this window is current again; the fade and the card handle unwinding themselves.
                if (animating && KitRecovery.Unwinding)
                    KitRecovery.Defer(windowId, ImGui.PopClipRect);
                fade?.Dispose();
                card.Dispose();
                return;
            }

            if (progress > 0f)
            {
                var end = ImGui.GetCursorScreenPos();
                state.Height = MathF.Max(0f, end.Y - start.Y);
                if (animating)
                {
                    fade?.Dispose();
                    ImGui.PopClipRect();
                    // Leave the last item at the eased height (the card's bottom covers its last item), with the
                    // cursor where it would be after a real last row.
                    var spacing = ImGui.GetStyle().ItemSpacing.Y;
                    ImGui.SetCursorScreenPos(new Vector2(end.X, start.Y + state.Height * progress - spacing));
                    ImGui.Dummy(Vector2.Zero);
                }
            }
            card.Dispose();
            Spacer();
        }
    }

    // ───────────────────────── Compact button ─────────────────────────

    /// <summary>A smaller button for actions on a row (Prefer, Go, Add...). Accent = outlined in the accent color.</summary>
    public static bool CompactButton(string label, bool accent = false, bool enabled = true, string? tooltip = null)
    {
        var size = new Vector2(MathF.Max(Theme.S(28f), ButtonWidth(label) - Theme.S(10f)), Theme.S(26f));
        return accent ? GhostButton(label, size, enabled, tooltip) : SecondaryButton(label, size, enabled, tooltip);
    }

    // ───────────────────────── Banner ─────────────────────────

    /// <summary>
    /// A tinted notice with a colored left edge (a message, a warning), optionally led by an <paramref name="icon"/> in the
    /// same color. With <paramref name="dismissable"/> it has an ✕; returns true when that was clicked. The ✕'s ID is the
    /// text: pass <paramref name="id"/> when two dismissable banners can show the same text.
    /// </summary>
    public static bool Banner(string text, Vector4 color, bool dismissable = false, FontAwesomeIcon? icon = null, string? id = null)
    {
        var p = ImGui.GetCursorScreenPos();
        var width = Avail();
        var pad = Theme.S(12f, 8f);
        var closeW = dismissable ? Theme.S(22f) : 0f;
        var iconW = icon.HasValue ? Theme.S(16f) + Theme.S(8f) : 0f;
        var wrap = width - pad.X * 2f - closeW - iconW;
        var textSize = ImGui.CalcTextSize(text, false, wrap);
        var size = new Vector2(width, textSize.Y + pad.Y * 2f);

        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(p, p + size, Theme.U32(color with { W = 0.13f }), Theme.Radius.Control);
        dl.AddRectFilled(p, new Vector2(p.X + Theme.S(3f), p.Y + size.Y), Theme.U32(color), Theme.Radius.Control, ImDrawFlags.RoundCornersLeft);
        if (icon is { } glyph)
            DrawGlyphCentered(dl, glyph, new Vector2(p.X + pad.X, p.Y + pad.Y), new Vector2(Theme.S(16f), ImGui.GetTextLineHeight()), color);

        ImGui.SetCursorScreenPos(p + pad + new Vector2(iconW, 0f));
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + wrap);
        ImGui.TextColored(Theme.Ink, text);
        ImGui.PopTextWrapPos();

        var dismissed = false;
        if (dismissable)
        {
            var at = new Vector2(p.X + width - closeW - Theme.S(6f), p.Y + (size.Y - closeW) * 0.5f);
            ImGui.SetCursorScreenPos(at);
            ImGui.PushID(id ?? text);
            dismissed = ImGui.InvisibleButton("##dismiss", new Vector2(closeW, closeW));
            ImGui.PopID();
            var hot = ImGui.IsItemHovered();
            if (hot)
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            DrawGlyphCentered(dl, FontAwesomeIcon.Times, at, new Vector2(closeW, closeW), hot ? Theme.Ink : Theme.Dim);
        }

        ImGui.SetCursorScreenPos(new Vector2(p.X, p.Y + size.Y));
        ImGui.Dummy(new Vector2(width, 0f));
        Spacer();
        return dismissed;
    }

    // ───────────────────────── Chips, spacing ─────────────────────────

    /// <summary>
    /// A small tinted tag ("MakePlace", "12 items", "short"), centered on the current text line. With
    /// <paramref name="status"/> the chip carries good / bad meaning: in colorblind mode a Positive / Negative chip also
    /// gets a check / cross.
    /// </summary>
    public static void Chip(string text, Vector4? color = null, bool status = false)
    {
        var c = color ?? Theme.Dim;
        var lineH = ImGui.GetTextLineHeight();
        using (Fonts.Small.Push())
        {
            var p = ImGui.GetCursorScreenPos();
            var h = ImGui.GetTextLineHeight() + ChipPad.Y * 2f;
            var y = p.Y + MathF.Max(0f, (lineH - h) * 0.5f);
            var size = DrawChipCore(ImGui.GetWindowDrawList(), new Vector2(p.X, y), text, c, StatusShape(c, status));
            ImGui.Dummy(new Vector2(size.X, MathF.Max(size.Y, lineH)));
        }
    }

    /// <summary>Width <see cref="Chip"/> / <see cref="DrawChip"/> take (pass the same color / status to count a colorblind glyph).</summary>
    public static float ChipWidth(string text, Vector4? color = null, bool status = false)
    {
        using (Fonts.Small.Push())
            return BadgeContentWidth(text, StatusShape(color ?? Theme.Dim, status)) + ChipPad.X * 2f;
    }

    /// <summary>
    /// <see cref="Chip"/> drawn straight onto a draw list (no layout item): left edge at <paramref name="pos"/>.X,
    /// centered on <paramref name="pos"/>.Y. For names with an "HQ" tag, group headers, tiles. Returns its width.
    /// </summary>
    public static float DrawChip(ImDrawListPtr dl, Vector2 pos, string text, Vector4? color = null, bool status = false)
    {
        var c = color ?? Theme.Dim;
        using (Fonts.Small.Push())
        {
            var h = ImGui.GetTextLineHeight() + ChipPad.Y * 2f;
            return DrawChipCore(dl, new Vector2(pos.X, MathF.Round(pos.Y - h * 0.5f)), text, c, StatusShape(c, status)).X;
        }
    }

    private static Vector2 ChipPad => Theme.S(7f, 1f);

    /// <summary>Chip surface (the color at 16%) and text at a top-left corner, in the current font. Returns its size.</summary>
    private static Vector2 DrawChipCore(ImDrawListPtr dl, Vector2 min, string text, Vector4 color, FontAwesomeIcon? shape)
    {
        var pad = ChipPad;
        var size = new Vector2(BadgeContentWidth(text, shape) + pad.X * 2f, ImGui.GetTextLineHeight() + pad.Y * 2f);
        dl.AddRectFilled(min, min + size, Theme.U32(color with { W = 0.16f }), Theme.Radius.Chip);
        DrawBadgeContent(dl, min + pad, text, color, shape);
        return size;
    }

    /// <summary>
    /// A 1 px hairline (RuleHair) across the available width, with <paramref name="space"/> (scaled px) of extra air above
    /// and below it: separates rows in a list, or a dialog's body from its buttons. Hairlines, not boxes.
    /// </summary>
    public static void Divider(float space = 0f)
    {
        if (space > 0f)
            ImGui.Dummy(new Vector2(0f, space));
        var p = ImGui.GetCursorScreenPos();
        var w = Avail();
        var thickness = Theme.S(1f);
        Hairline(ImGui.GetWindowDrawList(), p, new Vector2(p.X + w, p.Y + thickness));
        ImGui.Dummy(new Vector2(w, thickness));
        if (space > 0f)
            ImGui.Dummy(new Vector2(0f, space));
    }

    /// <summary>The gap between stacked cards (on top of the item spacing).</summary>
    public static void Spacer() => ImGui.Dummy(new Vector2(0f, Theme.Space.Tight));

    // ───────────────────────── Text ─────────────────────────

    /// <summary>Wrapped text in a color, wrapping inside the current card.</summary>
    public static void TextWrapped(string text, Vector4? color = null)
    {
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + Avail());
        ImGui.TextColored(color ?? Theme.Dim, text);
        ImGui.PopTextWrapPos();
    }

    /// <summary>Clickable text in the link color (hand cursor on hover). True when clicked.</summary>
    public static bool Link(string text, string? tooltip = null)
    {
        ImGui.TextColored(Theme.Link, text);
        var hovered = ImGui.IsItemHovered();
        HoverFeedback(hovered, tooltip);
        return hovered && ImGui.IsItemClicked(ImGuiMouseButton.Left);
    }

    /// <summary>A big figure (Display font) over a small tracked-caps caption (<see cref="Heading"/>).</summary>
    public static void Stat(string value, string caption, Vector4? color = null)
    {
        ImGui.BeginGroup();
        using (Fonts.Display.Push())
            ImGui.TextColored(color ?? Theme.Ink, value);
        Heading(caption);
        ImGui.EndGroup();
    }

    /// <summary>Puts the next item on this line against the right edge (of the card) if there's room, else just after.</summary>
    public static void RightAlign(float width)
    {
        ImGui.SameLine();
        var x = ImGui.GetCursorPosX() + Avail() - width;
        if (x > ImGui.GetCursorPosX())
            ImGui.SetCursorPosX(x);
    }

    // ───────────────────────── Tables ─────────────────────────

    /// <summary>The table look: alternating rows, hairlines between them, no boxes.</summary>
    public const ImGuiTableFlags TableFlags = ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.PadOuterX;

    /// <summary>
    /// Opens a table that fits the current card (<c>using var t = W.Table(...)</c>; draw rows only if <c>t.Open</c>).
    /// <paramref name="outerSize"/>: X &lt;= 0 fills the card, Y is the height (needed with ScrollY; 0 = fit the rows).
    /// </summary>
    public static TableScope Table(string id, int columns, ImGuiTableFlags flags = TableFlags, Vector2 outerSize = default)
    {
        var open = ImGui.BeginTable(id, columns, flags, new Vector2(outerSize.X > 0f ? outerSize.X : Avail(), outerSize.Y));
        if (open)
            tableDepth++;
        return new TableScope(open, open ? CurrentTableId() : 0u);
    }

    public sealed class TableScope(bool open, uint tableId = 0u) : IDisposable
    {
        private bool disposed;

        public bool Open { get; } = open;

        public void Dispose()
        {
            if (disposed || !Open)
                return;
            disposed = true;
            tableDepth--;
            // Unwinding (recovery ends the table), or a child window a cell opened is current, not this table: EndTable
            // would end the wrong thing.
            if (!KitRecovery.Unwinding && CurrentTableId() == tableId)
                ImGui.EndTable();
        }
    }

    private static uint CurrentTableId()
    {
        var table = ImGuiP.GetCurrentTable();
        return table.IsNull ? 0u : table.ID;
    }

    /// <summary>
    /// A fixed-width column (design pixels, scaled), with extra column <paramref name="flags"/> (NoSort, NoResize,
    /// DefaultSort...) and a <paramref name="userId"/> for sort specs.
    /// </summary>
    public static void FixedColumn(string name, float width, ImGuiTableColumnFlags flags = ImGuiTableColumnFlags.None, uint userId = 0)
        => ImGui.TableSetupColumn(name, ImGuiTableColumnFlags.WidthFixed | flags, Theme.S(width), userId);

    /// <summary>
    /// The header row in Slate (Label font). The defaults are ImGui's header row. <paramref name="trackedCaps"/> draws
    /// the labels as letter-spaced caps (like card titles); <paramref name="rowHeight"/> (scaled px, 0 = natural)
    /// sets the row's height and centers the labels in it; <paramref name="labels"/> replaces the column names
    /// ("LVL" for a "Level" column; also spares a string per column per frame).
    /// Sortable columns keep click-to-sort and the arrow; other columns get no hover highlight in tracked-caps mode.
    /// </summary>
    public static void TableHeaders(bool trackedCaps = false, float rowHeight = 0f, IReadOnlyList<string>? labels = null)
    {
        if (!trackedCaps && rowHeight <= 0f && labels == null)
        {
            using (Fonts.Label.Push())
            {
                ImGui.PushStyleColor(ImGuiCol.Text, Theme.Slate);
                ImGui.TableHeadersRow();
                ImGui.PopStyleColor();
            }
            return;
        }

        ImGui.TableNextRow(ImGuiTableRowFlags.Headers, rowHeight);
        var contentH = HeaderContentHeight(rowHeight);
        var sortable = !ImGui.TableGetSortSpecs().IsNull;
        var columns = ImGui.TableGetColumnCount();
        for (var c = 0; c < columns; c++)
            if (ImGui.TableSetColumnIndex(c))
                HeaderCell(c, labels, trackedCaps, contentH, sortable);
    }

    /// <summary>
    /// The header row, with a select-all <see cref="Checkbox"/> in the first column instead of its name: ticked when
    /// every row is, a dash when only some are. Returns true (tick all) or false (untick all) when it's clicked,
    /// otherwise null. Clicking it while only some are ticked ticks all of them. The options are
    /// <see cref="TableHeaders"/>'s.
    /// </summary>
    public static bool? TableHeadersWithCheckAll(int ticked, int total, bool trackedCaps = false, float rowHeight = 0f,
        IReadOnlyList<string>? labels = null)
    {
        bool? result = null;
        ImGui.TableNextRow(ImGuiTableRowFlags.Headers, rowHeight);
        var contentH = HeaderContentHeight(rowHeight);
        var styled = trackedCaps || rowHeight > 0f || labels != null;
        var sortable = styled && !ImGui.TableGetSortSpecs().IsNull;
        var columns = ImGui.TableGetColumnCount();
        for (var c = 0; c < columns; c++)
        {
            if (!ImGui.TableSetColumnIndex(c))
                continue;
            if (c == 0)
            {
                var all = total > 0 && ticked == total;
                var mixed = ticked > 0 && ticked < total;
                if (Checkbox("##checkAll", ref all, mixed, rowHeight > 0f ? contentH : 0f, all ? "Untick all" : "Tick all"))
                    result = mixed || all; // from "some", a click ticks everything
                continue;
            }
            if (styled)
            {
                HeaderCell(c, labels, trackedCaps, contentH, sortable);
                continue;
            }
            using (Fonts.Label.Push())
            using (new Theme.StyleScope().Color(ImGuiCol.Text, Theme.Slate))
            {
                ImGui.PushID(c); // like TableHeadersRow: columns with the same (or no) name keep their own hover and sort
                ImGui.TableHeader(ImGui.TableGetColumnName(c));
                ImGui.PopID();
            }
        }
        return result;
    }

    private static readonly string[] HeaderIds = BuildHeaderIds(64);

    private static string[] BuildHeaderIds(int count)
    {
        var ids = new string[count];
        for (var i = 0; i < count; i++)
            ids[i] = $"##kitTh{i}";
        return ids;
    }

    /// <summary>The label area inside a header cell: the row height less the cell padding (at least one label line).</summary>
    private static float HeaderContentHeight(float rowHeight)
    {
        float lineH;
        using (Fonts.Label.Push())
            lineH = ImGui.GetTextLineHeight();
        return rowHeight > 0f ? MathF.Max(lineH, rowHeight - ImGui.GetStyle().CellPadding.Y * 2f) : lineH;
    }

    /// <summary>One header cell: the label (tracked caps or plain) centered in contentH; sortable columns stay clickable.</summary>
    private static void HeaderCell(int column, IReadOnlyList<string>? labels, bool trackedCaps, float contentH, bool sortable)
    {
        var label = labels != null ? column < labels.Count ? labels[column] : string.Empty : ImGui.TableGetColumnName(column);
        var canSort = sortable && (ImGui.TableGetColumnFlags(column) & ImGuiTableColumnFlags.NoSort) == 0;
        var dl = ImGui.GetWindowDrawList();
        using (Fonts.Label.Push())
        {
            var p = ImGui.GetCursorScreenPos();
            var lineH = ImGui.GetTextLineHeight();
            var textPos = new Vector2(p.X, MathF.Round(p.Y + (contentH - lineH) * 0.5f));
            var text = Visible(label);
            if (canSort)
                // An empty label keeps click-to-sort, the hover highlight and the arrow; the text is drawn over it.
                ImGui.TableHeader(column < HeaderIds.Length ? HeaderIds[column] : $"##kitTh{column}");
            float w;
            if (trackedCaps)
            {
                w = TrackedCaps(dl, textPos, text, Theme.Slate);
            }
            else
            {
                dl.AddText(textPos, Theme.U32(Theme.Slate), text);
                w = ImGui.CalcTextSize(text).X;
            }
            if (!canSort)
                ImGui.Dummy(new Vector2(MathF.Max(1f, w), contentH));
        }
    }

    // ───────────────────────── Combo ─────────────────────────

    /// <summary>
    /// A themed dropdown (Raised fill like the Secondary buttons, Panel popup, accent-tinted selection).
    /// <c>using (var combo = W.Combo("##dest", preview, width)) if (combo.Open) { ... W.ComboItem(...) ... }</c>.
    /// width &lt;= 0 fills the available width (plus width when negative); height 0 = frame height.
    /// </summary>
    public static ComboScope Combo(string id, string preview, float width, float height = 0f)
    {
        float h = height > 0f ? height : ImGui.GetFrameHeight();
        float w = FillWidth(width, h * 3f);
        float padY = MathF.Max(0f, (h - ImGui.GetTextLineHeight()) * 0.5f);
        var style = new Theme.StyleScope()
            .Var(ImGuiStyleVar.FramePadding, new Vector2(Theme.S(12f), padY))
            .Color(ImGuiCol.FrameBg, Theme.Raised)
            .Color(ImGuiCol.FrameBgHovered, Theme.Raised2)
            .Color(ImGuiCol.FrameBgActive, Theme.Raised2)
            .Color(ImGuiCol.Button, Theme.Raised)
            .Color(ImGuiCol.ButtonHovered, Theme.Raised2)
            .Color(ImGuiCol.ButtonActive, Theme.Raised2)
            .Color(ImGuiCol.HeaderHovered, Theme.Raised)
            .Color(ImGuiCol.HeaderActive, Theme.AccentAlpha(0.33f))
            .Color(ImGuiCol.Header, Theme.AccentAlpha(0.25f));
        ImGui.SetNextItemWidth(w);
        var open = ImGui.BeginCombo(id, preview);
        if (!open && ImGui.IsItemHovered()) // while open, the popup is the current window
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        return new ComboScope(open, open ? CurrentWindowId() : 0u, style);
    }

    /// <summary>An open <see cref="Combo"/>: draw the items only if <see cref="Open"/>; disposing ends it.</summary>
    public sealed class ComboScope(bool open, uint popupId, IDisposable style) : IDisposable
    {
        private bool disposed;

        public bool Open { get; } = open;

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            // Only end the combo's own popup: while unwinding, or with a child an item opened still current, EndCombo
            // would end the wrong window (recovery ends the popup instead).
            if (Open && !KitRecovery.Unwinding && CurrentWindowId() == popupId)
                ImGui.EndCombo();
            style.Dispose();
        }
    }

    /// <summary>A row in an open <see cref="Combo"/>; the selected one gets the keyboard focus. True when picked.</summary>
    public static bool ComboItem(string label, bool selected)
    {
        var picked = ImGui.Selectable(label, selected);
        if (selected)
            ImGui.SetItemDefaultFocus();
        return picked;
    }

    /// <summary>A <see cref="Combo"/> over a list of labels. Returns true when <paramref name="index"/> changed.</summary>
    public static bool Combo(string id, IReadOnlyList<string> items, ref int index, float width, float height = 0f)
    {
        var preview = index >= 0 && index < items.Count ? items[index] : string.Empty;
        var changed = false;
        using var combo = Combo(id, preview, width, height);
        if (!combo.Open)
            return false;
        for (var i = 0; i < items.Count; i++)
        {
            ImGui.PushID(i);
            if (ComboItem(items[i], i == index) && i != index)
            {
                index = i;
                changed = true;
            }
            ImGui.PopID();
        }
        return changed;
    }

    // ───────────────────────── Sidebar rows ─────────────────────────

    private static readonly float[] NavGlowAlpha = [0.06f, 0.10f, 0.16f];

    /// <summary>
    /// A row in the sidebar (a page, a document): icon and label, an optional gray subtitle, and the accent fill with a
    /// soft glow when it's the active one. Flows with the cursor, full width. True when clicked.
    /// </summary>
    public static bool NavRow(string id, FontAwesomeIcon icon, string label, bool active, string? subtitle = null, bool enabled = true)
    {
        var width = ImGui.GetContentRegionAvail().X;
        var height = subtitle == null ? Theme.Space.NavRowHeight : Theme.S(48f);
        var p = ImGui.GetCursorScreenPos();
        ImGui.PushID(id);
        var hoverKey = ImGui.GetID("##nav");
        var activeKey = ImGui.GetID("##navActive");
        var clicked = InvisibleItem("##nav", new Vector2(width, height), enabled);
        ImGui.PopID();
        var hovered = enabled && ImGui.IsItemHovered();
        var max = p + new Vector2(width, height);
        var r = Theme.Radius.Control;
        var dl = ImGui.GetWindowDrawList();

        // The hover wash and the active fill both fade, so moving between rows glides instead of snapping.
        var hot = Motion.Hover(hoverKey, hovered);
        var on = Motion.Approach(activeKey, active ? 1f : 0f, 16f);
        if (hot > 0.01f && on < 0.99f)
            dl.AddRectFilled(p, max, Theme.U32(Theme.Wash(0.05f * hot * (1f - on))), r);
        if (on > 0.01f)
        {
            // Three-layer glow (6, 4, 2 px out), then the accent fill.
            for (var i = 0; i < NavGlowAlpha.Length; i++)
            {
                var spread = Theme.S(6f - 2f * i);
                dl.AddRectFilled(p - new Vector2(spread), max + new Vector2(spread), Theme.U32(Theme.AccentAlpha(NavGlowAlpha[i] * on)), r + spread);
            }
            dl.AddRectFilled(p, max, Theme.U32(Theme.AccentAlpha(on)), r);
        }
        if (hovered)
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

        var ink = Theme.Lerp(Theme.Lerp(Theme.Dim, Theme.Ink, hot), Theme.OnAccent, on);
        if (!enabled && !active)
            ink = ink with { W = DisabledAlpha };
        var iconBox = Theme.S(20f);
        DrawGlyphCentered(dl, icon, new Vector2(p.X + Theme.S(10f), p.Y), new Vector2(iconBox, height), ink);

        var textX = p.X + Theme.S(10f) + iconBox + Theme.S(8f);
        var maxW = max.X - textX - Theme.S(8f);
        var lineH = ImGui.GetTextLineHeight();
        if (subtitle == null)
        {
            dl.AddText(new Vector2(textX, p.Y + (height - lineH) * 0.5f), Theme.U32(ink), Fit(label, maxW));
        }
        else
        {
            float smallH;
            using (Fonts.Small.Push())
                smallH = ImGui.GetTextLineHeight();
            var top = p.Y + (height - lineH - smallH) * 0.5f;
            dl.AddText(new Vector2(textX, top), Theme.U32(ink), Fit(label, maxW));
            using (Fonts.Small.Push())
                dl.AddText(new Vector2(textX, top + lineH), Theme.U32(Theme.Lerp(Theme.Faint, Theme.OnAccent with { W = 0.8f }, on) with { W = ink.W * (on > 0.5f ? 0.8f : 1f) }), Fit(subtitle, maxW));
        }

        Gap(4f);
        return clicked && enabled;
    }
}
