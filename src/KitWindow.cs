using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;

namespace phys1ksUI;

/// <summary>
/// Screen-space rectangle in the header strip between the page title and the close button. Right-align content against
/// <see cref="Max"/>.X and center it on <see cref="CenterY"/>; widgets drawn here win input over the header drag area.
/// </summary>
internal readonly record struct HeaderSlot(Vector2 Min, Vector2 Max)
{
    public float Width => Max.X - Min.X;
    public float Height => Max.Y - Min.Y;
    public float CenterY => (Min.Y + Max.Y) * 0.5f;
}

/// <summary>A long-running job for the sidebar status block. <paramref name="Fraction"/> null = no progress bar.</summary>
internal sealed record RunningOperation(string Label, string? Detail = null, float? Fraction = null, Action? Cancel = null);

/// <summary>A small line at the bottom of the sidebar: a dot (or an icon) and gray text (dependency, character...).</summary>
internal readonly record struct StatusLine(string Text, Vector4 Color, FontAwesomeIcon? Icon = null, string? Tooltip = null);

/// <summary>
/// The phys1ksUI window: a borderless window with a sidebar (brand tile, scrolling nav, running-operation block, status
/// lines), a header strip (page title, a slot for page actions, drag, minimize, round close) and a padded body. Subclasses
/// draw the nav, the body, and optionally the header slot and overlays (popups, modals).
/// <para>
/// Minimize (the chevron, or double-click the header) folds the window up to a single title bar showing the plugin, what's
/// running (with its progress and Cancel) and restore / close. The height eases between the two over
/// <see cref="CollapseMs"/>.
/// </para>
/// </summary>
internal abstract class KitWindow : Window
{
    private const ImGuiWindowFlags BaseFlags =
        ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoCollapse;
    private const float CollapseMs = 280f;

    private readonly string brand;
    private readonly FontAwesomeIcon brandIcon;
    private readonly string version;
    private readonly Vector2 minimumSize;
    private IDisposable? themeScope;
    private bool paddingPushed;

    // Minimize to the title bar: 0 = open, 1 = folded up; eased from collapseFrom since collapseTick.
    private bool compact;
    private float collapse;
    private float collapseFrom;
    private long collapseTick;
    private bool sizeDriven;
    private Vector2 expandedSize; // Dalamud units (unscaled), remembered while open

    // Page transitions: a new page fades in while sliding up a little.
    private const float PageRevealMs = 260f;
    private string? shownPage;
    private long pageShownTick;

    /// <param name="windowName">ImGui window name (unique; add ###id to keep it stable).</param>
    /// <param name="brand">The plugin name shown in the sidebar.</param>
    /// <param name="brandIcon">The glyph on the accent tile.</param>
    /// <param name="minimumSize">Minimum size in design pixels at 100% text size (grows with the text size).</param>
    protected KitWindow(string windowName, string brand, FontAwesomeIcon brandIcon, Vector2 minimumSize)
        : base(windowName, BaseFlags)
    {
        this.brand = brand;
        this.brandIcon = brandIcon;
        this.minimumSize = minimumSize;
        version = VersionLabel(GetType());
    }

    /// <summary>"v1.2.3" from the plugin assembly (trailing .0 revision dropped).</summary>
    public string Version => version;

    /// <summary>Folded up to the title bar (or on its way there).</summary>
    public bool Compact => compact;

    /// <summary>Folds the window up to its title bar, or opens it back up.</summary>
    public void ToggleCompact()
    {
        compact = !compact;
        collapseFrom = collapse;
        collapseTick = Environment.TickCount64;
    }

    /// <summary>Opens the window back up if it's folded (call it when a command or button asks to show a page).</summary>
    public void Expand()
    {
        if (compact)
            ToggleCompact();
    }

    // ───────────────────────── For subclasses ─────────────────────────

    /// <summary>The saved accent (read every frame, so a change in Settings shows at once).</summary>
    protected abstract AccentColor Accent { get; }

    /// <summary>
    /// The saved colorblind mode (read every frame, like <see cref="Accent"/>). Required: a default would undo the mode
    /// <see cref="Appearance.DrawCard"/> just set, every frame.
    /// </summary>
    protected abstract bool Colorblind { get; }

    /// <summary>The big title in the header strip.</summary>
    protected abstract string PageTitle { get; }

    /// <summary>Identifies the page on show: when it changes, the body scrolls to the top and the new page fades in.</summary>
    protected virtual string PageKey => PageTitle;

    /// <summary>The sidebar's nav rows (<see cref="W.NavRow"/>), in a scrolling area under the brand.</summary>
    protected abstract void DrawSidebarNav();

    /// <summary>The page, inside the padded, scrolling body.</summary>
    protected abstract void DrawBody();

    /// <summary>
    /// Drawn at the top of the body before the page and left out of the page transition: the page's own tab bar goes
    /// here, so switching tabs doesn't make the tabs themselves slide and flicker.
    /// </summary>
    protected virtual void DrawBodyTop() { }

    /// <summary>Page actions in the header, right of the title.</summary>
    protected virtual void DrawHeaderRight(HeaderSlot slot) { }

    /// <summary>How wide the header actions are (scaled px): a long title is trimmed so they always fit.</summary>
    protected virtual float HeaderRightWidth => 0f;

    /// <summary>What's running now (buying, moving...), shown with a progress bar and Cancel at the bottom of the sidebar.</summary>
    protected virtual RunningOperation? GetRunningOperation() => null;

    /// <summary>The small lines at the very bottom of the sidebar.</summary>
    protected virtual IReadOnlyList<StatusLine> GetStatusLines() => [];

    /// <summary>Popups and modals, drawn after everything else in the window.</summary>
    protected virtual void DrawOverlays() { }

    /// <summary>More window flags (the shell sets <see cref="Window.Flags"/> itself every frame, so set extra ones here).</summary>
    protected virtual ImGuiWindowFlags ExtraFlags => ImGuiWindowFlags.None;

    // ───────────────────────── Window ─────────────────────────

    public override void PreDraw()
    {
        Theme.SetAccent(Accent);
        Theme.SetColorblind(Colorblind);
        ApplyCollapse();
        themeScope = Theme.Push();
        // Full-bleed: the shell lays out its own padding.
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        paddingPushed = true;
    }

    /// <summary>
    /// Size, constraints and flags for this frame. Open: the user sizes the window (minimum grows with the text size).
    /// Folding or folded: the height is driven from the remembered open height down to the header's, and resizing is off.
    /// Dalamud scales Size and SizeConstraints by the global scale itself, so these are in unscaled units.
    /// </summary>
    private void ApplyCollapse()
    {
        if (expandedSize == Vector2.Zero)
        {
            // Not drawn open yet (folded before the first frame): the open size is unknown, so there is nothing to fold
            // from. Hold it open and restart the fold's clock until the first frame measures the size, then fold.
            collapse = collapseFrom = 0f;
            collapseTick = Environment.TickCount64;
        }
        else
        {
            collapse = Motion.Tween(collapseFrom, compact ? 1f : 0f, collapseTick, CollapseMs);
        }

        var wasSizeDriven = sizeDriven;
        sizeDriven = (compact || collapse > 0f) && expandedSize != Vector2.Zero;
        var headerUnits = Theme.Space.HeaderHeight / ImGuiHelpers.GlobalScale;
        var minimum = minimumSize * Fonts.Scale;

        Flags = (sizeDriven ? BaseFlags | ImGuiWindowFlags.NoResize : BaseFlags) | ExtraFlags;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = sizeDriven ? new Vector2(minimum.X, headerUnits) : minimum,
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };

        if (sizeDriven)
        {
            Size = new Vector2(expandedSize.X, expandedSize.Y + (headerUnits - expandedSize.Y) * collapse);
            SizeCondition = ImGuiCond.Always;
        }
        else if (wasSizeDriven)
        {
            Size = expandedSize; // back to exactly the size it had
            SizeCondition = ImGuiCond.Always;
        }
        else if (SizeCondition == ImGuiCond.Always)
        {
            SizeCondition = ImGuiCond.FirstUseEver; // hand sizing back to the user
        }
    }

    public override void PostDraw()
    {
        if (paddingPushed)
        {
            ImGui.PopStyleVar();
            paddingPushed = false;
        }
        themeScope?.Dispose();
        themeScope = null;
    }

    public override void Draw()
    {
        drawFailed = false;
        var windowId = W.CurrentWindowId();
        try
        {
            DrawShell();
        }
        catch (Exception ex) when (KitRecovery.Catch())
        {
            // Whatever the draw left open (child windows, popups, tables, IDs, style pushes) is closed back to this
            // window, so Dalamud's End() and PostDraw's pops match.
            KitRecovery.RecoverTo(windowId);
            ReportDrawError(ex);
        }
        // A clean frame forgets the last error, so one that comes back later is logged again.
        if (!drawFailed)
            lastDrawError = null;
    }

    private (Type Type, string Message, System.Reflection.MethodBase? Site)? lastDrawError;
    private bool drawFailed;

    /// <summary>
    /// A draw that throws throws every frame: log each distinct error once, not 60 times a second. Errors are told apart
    /// by type, message and throwing method (cheap; no stack trace is built per frame).
    /// </summary>
    private void ReportDrawError(Exception ex)
    {
        drawFailed = true;
        var key = (ex.GetType(), ex.Message, ex.TargetSite);
        if (lastDrawError == key)
            return;
        lastDrawError = key;
        Kit.Log?.Error(ex, $"{brand} window draw failed");
    }

    /// <summary>Shown in the body in place of the rest of a page that threw.</summary>
    private static void DrawBodyError(Exception ex)
    {
        W.Banner($"This page hit an error and couldn't finish drawing: {ex.Message}", Theme.Negative,
                 icon: FontAwesomeIcon.ExclamationTriangle);
        W.TextWrapped("The details are in the Dalamud log (/xllog).", Theme.Faint);
    }

    private void DrawShell()
    {
        W.ResetFrame();
        using var bodyFont = Fonts.Body.Push();
        // Popups, tooltips and modals opened inside get normal padding back.
        using var padding = new Theme.StyleScope().Var(ImGuiStyleVar.WindowPadding, Theme.S(10f, 8f));

        var winPos = ImGui.GetWindowPos();
        var winSize = ImGui.GetWindowSize();
        var sidebarW = Theme.Space.SidebarWidth;
        var headerH = Theme.Space.HeaderHeight;

        if (collapse >= 0.999f)
        {
            DrawCompactBar(winPos, winSize.X, headerH);
            DrawOverlays();
            return;
        }
        if (!sizeDriven)
            expandedSize = winSize / ImGuiHelpers.GlobalScale;

        // While folding, everything but the header strip fades out.
        using (FadeWithCollapse())
            DrawSidebar(winPos, new Vector2(sidebarW, winSize.Y));

        var rightMin = new Vector2(winPos.X + sidebarW, winPos.Y);
        var rightW = MathF.Max(1f, winSize.X - sidebarW);
        DrawHeaderStrip(rightMin, rightW, headerH);

        using (FadeWithCollapse())
        {
            // Below the header strip's 1 px hairline.
            var rule = Theme.S(1f);
            ImGui.SetCursorScreenPos(new Vector2(rightMin.X, rightMin.Y + headerH + rule));
            var bodyH = MathF.Max(1f, winSize.Y - headerH - rule);
            // The child's padding stays pushed until EndChild: ImGui's recovery pops style back to what it was at
            // BeginChild, so a var popped right after BeginChild would leave the page's pushes one short (a leak).
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(Theme.Space.BodyPad));
            var bodyVisible = ImGui.BeginChild("##kitBody", new Vector2(rightW, bodyH), false, ImGuiWindowFlags.AlwaysUseWindowPadding);
            var bodyId = W.CurrentWindowId();
            // Popups and tooltips the page opens get normal padding back (recovery pops this along with the page).
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Theme.S(10f, 8f));
            var innerPushed = true;
            try
            {
                if (bodyVisible)
                    DrawPageWithReveal();
            }
            catch (Exception ex) when (KitRecovery.Catch())
            {
                // Close what the page left open back to the body, so EndChild ends the body; the header, sidebar and
                // overlays still draw, and the page shows the error in place of its rest.
                KitRecovery.RecoverTo(bodyId);
                innerPushed = false;
                ReportDrawError(ex);
                DrawBodyError(ex);
            }
            finally
            {
                if (innerPushed)
                    ImGui.PopStyleVar();
                ImGui.EndChild();
                ImGui.PopStyleVar();
            }

            if (!sizeDriven)
                DrawResizeDots(winPos + winSize);
        }
        // Outside the fade: a modal opened while the window folds is drawn at full strength.
        DrawOverlays();
    }

    /// <summary>Draws the body; on a new page, scrolls to the top and fades the page in with a small upward slide.</summary>
    private void DrawPageWithReveal()
    {
        var key = PageKey;
        if (key != shownPage)
        {
            shownPage = key;
            pageShownTick = Environment.TickCount64;
            ImGui.SetScrollY(0f);
        }

        DrawBodyTop();
        var reveal = Motion.Reveal(pageShownTick, PageRevealMs);
        if (reveal >= 1f)
        {
            DrawBody();
            return;
        }
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + (1f - reveal) * Theme.S(12f));
        using (new Theme.StyleScope().Var(ImGuiStyleVar.Alpha, MathF.Max(0.001f, reveal * ImGui.GetStyle().Alpha)))
            DrawBody();
    }

    private IDisposable? FadeWithCollapse()
        => collapse > 0f ? new Theme.StyleScope().Var(ImGuiStyleVar.Alpha, MathF.Max(0.001f, (1f - collapse) * ImGui.GetStyle().Alpha)) : null;

    // ───────────────────────── Minimized: the title bar ─────────────────────────

    /// <summary>The whole window folded to one bar: brand, what's running (or the page), and restore / close.</summary>
    private void DrawCompactBar(Vector2 min, float width, float height)
    {
        var dl = ImGui.GetWindowDrawList();
        var max = new Vector2(min.X + width, min.Y + height);
        W.DrawPanel(dl, min, max);

        var gutter = Theme.Space.Gutter;
        var button = Theme.S(28f);
        var gap = Theme.S(6f);
        var buttonY = min.Y + (height - button) * 0.5f;
        var closePos = new Vector2(max.X - Theme.S(12f) - button, buttonY);
        var expandPos = closePos - new Vector2(button + gap, 0f);
        var op = GetRunningOperation();
        var cancelPos = expandPos - new Vector2(button + gap, 0f);
        var buttonsLeft = op?.Cancel != null ? cancelPos.X : expandPos.X;

        DrawBrand(dl, new Vector2(min.X + gutter, min.Y + (height - Theme.S(30f)) * 0.5f));
        float brandW;
        using (Fonts.Title.Push())
            brandW = Theme.S(40f) + W.TrackedCapsWidth(brand, 0.06f);
        var x = min.X + gutter + brandW + Theme.S(18f);
        var right = buttonsLeft - Theme.S(14f);
        var midY = min.Y + height * 0.5f;

        if (op != null)
        {
            // Pulsing dot, "Buying · detail", and the progress bar on the right if there is one.
            W.StatusDot(dl, new Vector2(x + Theme.S(4f), midY), Theme.Accent, pulse: true);
            var textX = x + Theme.S(16f);
            if (op.Fraction is { } fraction)
            {
                var barW = MathF.Min(Theme.S(140f), MathF.Max(0f, (right - textX) * 0.35f));
                if (barW > Theme.S(40f))
                {
                    var barH = Theme.S(5f);
                    var barMin = new Vector2(right - barW, midY - barH * 0.5f);
                    W.ProgressBar(dl, barMin, barW, barH, fraction);
                    right = barMin.X - Theme.S(12f);
                }
            }
            var text = op.Detail != null ? $"{op.Label}  ·  {op.Detail}" : op.Label;
            var lineH = ImGui.GetTextLineHeight();
            dl.AddText(new Vector2(textX, midY - lineH * 0.5f), Theme.U32(Theme.Ink), W.Fit(text, right - textX));
        }
        else
        {
            var lineH = ImGui.GetTextLineHeight();
            dl.AddText(new Vector2(x, midY - lineH * 0.5f), Theme.U32(Theme.Dim), W.Fit(PageTitle, right - x));
        }

        if (op?.Cancel is { } cancel && W.RoundButton("##kitCompactCancel", cancelPos, button, FontAwesomeIcon.Stop, "Cancel", danger: true))
            cancel();
        if (W.RoundButton("##kitExpand", expandPos, button, FontAwesomeIcon.ChevronDown, "Restore (or double-click the bar)"))
            ToggleCompact();
        if (W.RoundButton("##kitClose", closePos, button, FontAwesomeIcon.Times, "Close", danger: true))
            IsOpen = false;

        DragArea("##kitCompactDrag", min, new Vector2(buttonsLeft - min.X - Theme.S(4f), height));
    }

    /// <summary>
    /// An invisible handle that drags the window; double-click folds or unfolds it. Submitted after the widgets on top of
    /// it: ImGui gives the hover to the first item under the mouse, so those widgets win and double-clicking one of them
    /// (the page tabs, rename) doesn't fold the window.
    /// </summary>
    private void DragArea(string id, Vector2 pos, Vector2 size)
    {
        ImGui.SetCursorScreenPos(pos);
        ImGui.InvisibleButton(id, new Vector2(MathF.Max(1f, size.X), size.Y));
        if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
            ToggleCompact();
        if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
            ImGui.SetWindowPos(ImGui.GetWindowPos() + ImGui.GetIO().MouseDelta);
    }

    // ───────────────────────── Sidebar ─────────────────────────

    private void DrawSidebar(Vector2 min, Vector2 size)
    {
        var dl = ImGui.GetWindowDrawList();
        var max = min + size;
        dl.AddRectFilled(min, max, Theme.U32(Theme.Panel), Theme.Radius.Window, ImDrawFlags.RoundCornersLeft);
        W.Hairline(dl, new Vector2(max.X - Theme.S(1f), min.Y), max);

        var gutter = Theme.Space.Gutter;
        var headerH = Theme.Space.HeaderHeight;
        var innerW = size.X - gutter * 2f - Theme.S(1f);

        DrawBrand(dl, new Vector2(min.X + gutter, min.Y + (headerH - Theme.S(30f)) * 0.5f));
        W.Hairline(dl, new Vector2(min.X, min.Y + headerH), new Vector2(max.X - Theme.S(1f), min.Y + headerH + Theme.S(1f)));

        var op = GetRunningOperation();
        var lines = GetStatusLines();
        var statusH = StatusBlockHeight(op, lines);

        // The nav scrolls between the brand and the status block; the glow on the active row needs a little margin.
        var navTop = min.Y + headerH + Theme.S(1f);
        var navH = MathF.Max(Theme.S(40f), max.Y - statusH - navTop);
        ImGui.SetCursorScreenPos(new Vector2(min.X, navTop));
        // Pushed until EndChild, like the body (see DrawShell): recovery measures from BeginChild.
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(gutter));
        ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarSize, Theme.S(6f));
        var visible = ImGui.BeginChild("##kitNav", new Vector2(size.X - Theme.S(1f), navH), false, ImGuiWindowFlags.AlwaysUseWindowPadding);
        var navId = W.CurrentWindowId();
        // The nav's own popups and scrolling children get the normal padding and scrollbar back.
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Theme.S(10f, 8f));
        ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarSize, Theme.S(10f));
        var innerPushed = true;
        try
        {
            if (visible)
                DrawSidebarNav();
        }
        catch (Exception ex) when (KitRecovery.Catch())
        {
            KitRecovery.RecoverTo(navId);
            innerPushed = false;
            ReportDrawError(ex);
        }
        finally
        {
            if (innerPushed)
                ImGui.PopStyleVar(2);
            ImGui.EndChild();
            ImGui.PopStyleVar(2);
        }

        DrawStatusBlock(dl, new Vector2(min.X + gutter, max.Y - statusH), innerW, op, lines);
    }

    private void DrawBrand(ImDrawListPtr dl, Vector2 p)
    {
        var tile = Theme.S(30f);
        var tileMax = p + new Vector2(tile, tile);
        var r = Theme.Radius.Small;

        // Fake gradient: darker base, lighter accent on the top 55%, soft drop shadow.
        dl.AddRectFilled(p + new Vector2(0f, Theme.S(2f)), tileMax + new Vector2(0f, Theme.S(2f)), Theme.U32(Theme.Shadow(0.35f)), r);
        dl.AddRectFilled(p, tileMax, Theme.U32(Theme.Darken(Theme.Accent, 0.12f)), r);
        dl.AddRectFilled(p, new Vector2(tileMax.X, p.Y + tile * 0.55f), Theme.U32(Theme.Lighten(Theme.Accent, 0.08f)), r, ImDrawFlags.RoundCornersTop);
        W.DrawGlyphCentered(dl, brandIcon, p, new Vector2(tile, tile), Theme.OnAccent);

        var textX = p.X + tile + Theme.S(10f);
        float nameH;
        using (Fonts.Title.Push())
        {
            nameH = ImGui.GetTextLineHeight();
            W.TrackedCaps(dl, new Vector2(textX, p.Y - Theme.S(1f)), brand, Theme.Ink, 0.06f);
        }

        using (Fonts.Label.Push())
        {
            var vs = ImGui.CalcTextSize(version);
            var vMin = new Vector2(textX, p.Y + nameH + Theme.S(1f));
            var vMax = vMin + new Vector2(vs.X + Theme.S(8f), vs.Y + Theme.S(2f));
            dl.AddRectFilled(vMin, vMax, Theme.U32(Theme.Wash(0.06f)), Theme.Radius.Chip);
            dl.AddText(vMin + new Vector2(Theme.S(4f), Theme.S(1f)), Theme.U32(Theme.Slate), version);
        }
    }

    private static (float Body, float Small) LineHeights()
    {
        var body = ImGui.GetTextLineHeight();
        using (Fonts.Small.Push())
            return (body, ImGui.GetTextLineHeight());
    }

    private static float StatusBlockHeight(RunningOperation? op, IReadOnlyList<StatusLine> lines)
    {
        if (op == null && lines.Count == 0)
            return 0f;
        var (body, small) = LineHeights();
        var gutter = Theme.Space.Gutter;
        var rowGap = Theme.S(6f);
        var h = gutter * 2f;
        if (op != null)
        {
            h += body + (op.Detail != null ? small : 0f);
            if (op.Fraction.HasValue)
                h += rowGap + Theme.S(4f);
            if (op.Cancel != null)
                h += Theme.S(10f) + Theme.S(30f);
            if (lines.Count > 0)
                h += gutter;
        }
        if (lines.Count > 0)
            h += lines.Count * small + (lines.Count - 1) * rowGap;
        return h;
    }

    private void DrawStatusBlock(ImDrawListPtr dl, Vector2 p, float width, RunningOperation? op, IReadOnlyList<StatusLine> lines)
    {
        if (op == null && lines.Count == 0)
            return;
        var (body, small) = LineHeights();
        var gutter = Theme.Space.Gutter;
        var rowGap = Theme.S(6f);
        var x = p.X;
        var y = p.Y;

        W.Hairline(dl, new Vector2(x - gutter, y), new Vector2(x + width + gutter, y + Theme.S(1f)));
        y += gutter;

        if (op != null)
        {
            ImGui.SetCursorScreenPos(new Vector2(x, y));
            W.StatusDot(Theme.Accent, pulse: true);
            dl.AddText(new Vector2(x + Theme.S(16f), y), Theme.U32(Theme.Ink), W.Fit(op.Label, width - Theme.S(16f)));
            y += body;

            if (op.Detail != null)
            {
                using (Fonts.Small.Push())
                    dl.AddText(new Vector2(x + Theme.S(16f), y), Theme.U32(Theme.Dim), W.Fit(op.Detail, width - Theme.S(16f)));
                y += small;
            }

            if (op.Fraction is { } fraction)
            {
                y += rowGap;
                var barH = Theme.S(4f);
                W.ProgressBar(dl, new Vector2(x, y), width, barH, fraction);
                y += barH;
            }

            if (op.Cancel != null)
            {
                y += Theme.S(10f);
                ImGui.SetCursorScreenPos(new Vector2(x, y));
                if (W.GhostButton("Cancel##kitCancel", new Vector2(width, Theme.S(30f))))
                    op.Cancel();
                y += Theme.S(30f);
            }

            if (lines.Count > 0)
                y += gutter;
        }

        using (Fonts.Small.Push())
        {
            for (var i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                if (i > 0)
                    y += rowGap;
                var shape = line.Icon ?? Theme.ColorblindShape(line.Color);
                if (shape is { } icon)
                {
                    W.DrawGlyphCentered(dl, icon, new Vector2(x - Theme.S(1f), y), new Vector2(Theme.S(9f), small), line.Color);
                }
                else
                {
                    var r = Theme.S(3.5f);
                    dl.AddCircleFilled(new Vector2(x + r, y + small * 0.5f), r, Theme.U32(line.Color), 16);
                }
                dl.AddText(new Vector2(x + Theme.S(16f), y), Theme.U32(line.Icon != null ? Theme.Faint : Theme.Dim),
                           W.Fit(line.Text, width - Theme.S(16f)));

                if (line.Tooltip != null && ImGui.IsWindowHovered(ImGuiHoveredFlags.RootAndChildWindows) &&
                    ImGui.IsMouseHoveringRect(new Vector2(x, y), new Vector2(x + width, y + small)))
                    W.Tooltip(line.Tooltip);
                y += small;
            }
        }
    }

    // ───────────────────────── Header strip ─────────────────────────

    private void DrawHeaderStrip(Vector2 min, float width, float height)
    {
        var dl = ImGui.GetWindowDrawList();
        var max = new Vector2(min.X + width, min.Y + height);
        dl.AddRectFilled(min, max, Theme.U32(Theme.Panel), Theme.Radius.Window, ImDrawFlags.RoundCornersTopRight);
        W.Hairline(dl, new Vector2(min.X, max.Y), new Vector2(max.X, max.Y + Theme.S(1f)));

        var closeSize = Theme.S(28f);
        var closePos = new Vector2(max.X - Theme.S(16f) - closeSize, min.Y + (height - closeSize) * 0.5f);
        var minimizePos = closePos - new Vector2(closeSize + Theme.S(6f), 0f);

        var titleX = min.X + Theme.S(20f);
        var slotLeft = minimizePos.X - Theme.S(10f);
        // Measured in the body font, the one the header actions are drawn in.
        var actionsW = HeaderRightWidth;
        float titleW;
        using (Fonts.Display.Push())
        {
            // Long titles (a job name) are trimmed so page actions always keep some room.
            var room = slotLeft - titleX;
            var reserved = actionsW > 0f ? actionsW + Theme.S(16f) : 0f;
            var title = W.Fit(PageTitle, MathF.Max(Theme.S(60f), MathF.Min(room * 0.6f, room - reserved)));
            var ts = ImGui.CalcTextSize(title);
            titleW = ts.X;
            dl.AddText(new Vector2(titleX, MathF.Round(min.Y + (height - ts.Y) * 0.5f)), Theme.U32(Theme.Ink), title);
        }

        var slot = new HeaderSlot(new Vector2(titleX + titleW + Theme.S(16f), min.Y), new Vector2(slotLeft, max.Y));
        if (slot.Width > 0f)
            DrawHeaderRight(slot);

        if (W.RoundButton("##kitMinimize", minimizePos, closeSize, FontAwesomeIcon.ChevronUp, "Minimize to the title bar (or double-click the header)"))
            ToggleCompact();
        if (W.RoundButton("##kitClose", closePos, closeSize, FontAwesomeIcon.Times, "Close", danger: true))
            IsOpen = false;

        // Drag handle: the whole strip left of the buttons (double-click folds the window), under the page's widgets.
        DragArea("##kitDrag", min, new Vector2(minimizePos.X - min.X - Theme.S(4f), height));
    }

    /// <summary>Subtle 3-dot grip in the bottom-right corner (ImGui's own grip stays usable).</summary>
    private static void DrawResizeDots(Vector2 windowMax)
    {
        var dl = ImGui.GetWindowDrawList();
        var step = Theme.S(4f);
        var r = Theme.S(1.2f);
        var col = Theme.U32(Theme.Faint with { W = 0.6f });
        var corner = windowMax - new Vector2(Theme.S(8f), Theme.S(8f));
        dl.AddCircleFilled(corner, r, col, 8);
        dl.AddCircleFilled(corner - new Vector2(step, 0f), r, col, 8);
        dl.AddCircleFilled(corner - new Vector2(0f, step), r, col, 8);
    }

    private static string VersionLabel(Type type)
    {
        var v = type.Assembly.GetName().Version;
        if (v == null)
            return "v?";
        return v.Revision > 0 ? $"v{v.Major}.{v.Minor}.{v.Build}.{v.Revision}" : $"v{v.Major}.{v.Minor}.{v.Build}";
    }
}
