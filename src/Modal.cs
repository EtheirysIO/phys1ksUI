using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace phys1ksUI;

/// <summary>
/// Themed modal dialogs (from Invenwhorey): centered on the screen, Panel background, a Display-font title and a round
/// close button, over a dark scrim.
/// <para>
/// Why the scrim is painted by hand: ImGui draws its modal dim at the END of the frame from the global style (a scoped
/// push can't reach it), and in this ImGui build it lands on top of the modal's own surface (only child windows escape
/// it), so the dialog came out dimmed too. While one of our modals is open ImGui's dim is made transparent and the scrim
/// and panel are painted inside the modal instead. The original color comes back on the first frame no modal of ours is
/// drawn (<see cref="BeginFrame"/>, hooked up by <see cref="Kit.Initialize"/>) and on unload.
/// </para>
/// </summary>
internal static class Modal
{
    private static readonly Vector4 Scrim = Theme.Shadow(0.6f);
    private static Vector4? savedDim;
    private static bool drawnThisFrame;

    /// <summary>
    /// Draws the modal while <paramref name="open"/> is true (set it to show the dialog). The close button sets it false;
    /// a body that closes itself should set the caller's flag false and call <see cref="ImGui.CloseCurrentPopup"/>.
    /// </summary>
    public static void Draw(string title, ref bool open, Action body, ImGuiWindowFlags flags = ImGuiWindowFlags.AlwaysAutoResize)
    {
        if (!open)
            return;

        ImGui.OpenPopup(title);
        ImGui.SetNextWindowPos(ImGui.GetMainViewport().GetCenter(), ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));

        bool began;
        // Window-level style is read at Begin; popped straight after so nested popups are unaffected. The background
        // and border are painted by PaintBackdrop (after the scrim), so ImGui's are off.
        using (new Theme.StyleScope()
                   .Color(ImGuiCol.PopupBg, Theme.Transparent)
                   .Color(ImGuiCol.Border, Theme.Transparent)
                   .Var(ImGuiStyleVar.WindowRounding, Theme.Radius.Window)
                   .Var(ImGuiStyleVar.WindowPadding, Theme.S(20f, 20f))
                   .Var(ImGuiStyleVar.WindowBorderSize, 0f))
        {
            began = ImGui.BeginPopupModal(title, ref open, flags | ImGuiWindowFlags.NoTitleBar);
        }
        if (!began)
            return;

        HideImGuiDim();
        PaintBackdrop();
        try
        {
            using var surface = W.NewSurface(); // the modal's cards aren't nested in whatever card opened it
            DrawHeader(title, ref open);
            body();
        }
        finally
        {
            // Unwinding to a kit catch site: its recovery ends this modal along with whatever the body left open.
            if (!KitRecovery.Unwinding)
                ImGui.EndPopup();
        }
    }

    /// <summary>
    /// A yes / no dialog: <paramref name="message"/> (and an optional Dim <paramref name="detail"/>), then the confirm
    /// button (Danger when <paramref name="danger"/>, else Primary) and Cancel. Draw it every frame; it shows while
    /// <paramref name="open"/> is true, and either choice (or the close button) sets it false. Returns true on the frame
    /// the confirm button is clicked. Text wraps at 26em.
    /// </summary>
    public static bool Confirm(string title, ref bool open, string message, string confirmLabel, bool danger = false,
        string? detail = null, string cancelLabel = "Cancel")
    {
        if (!open)
            return false;
        var confirmed = false;
        var stillOpen = open;
        Draw(title, ref stillOpen, () =>
        {
            ImGui.PushTextWrapPos(ImGui.GetFontSize() * 26f);
            ImGui.TextColored(Theme.Ink, message);
            if (!string.IsNullOrEmpty(detail))
                ImGui.TextColored(Theme.Dim, detail);
            ImGui.PopTextWrapPos();
            ImGui.Dummy(new Vector2(0f, Theme.S(6f)));

            var confirmId = confirmLabel + "##modalConfirm";
            if (danger ? W.DangerButton(confirmId) : W.PrimaryButton(confirmId))
            {
                confirmed = true;
                Close(ref stillOpen);
            }
            ImGui.SameLine();
            if (W.SecondaryButton(cancelLabel + "##modalCancel"))
                Close(ref stillOpen);
        });
        open = stillOpen;
        return confirmed;
    }

    /// <summary>Closes the modal being drawn from inside its body: clears the caller's flag and closes the popup.</summary>
    public static void Close(ref bool open)
    {
        open = false;
        ImGui.CloseCurrentPopup();
    }

    /// <summary>Once per frame before any window draws: puts ImGui's dim back if no modal of ours was drawn last frame.</summary>
    internal static void BeginFrame()
    {
        if (!drawnThisFrame)
            RestoreDim();
        drawnThisFrame = false;
    }

    /// <summary>
    /// Puts Dalamud's modal dim color back (no modal open, or unloading). The style is shared by every plugin, and another
    /// plugin using this kit may have hidden the dim too: only a dim that is still hidden is put back, so one plugin
    /// closing its modal can't write a stale value over another's.
    /// </summary>
    internal static void RestoreDim()
    {
        if (savedDim is not { } original)
            return;
        savedDim = null;
        var colors = ImGui.GetStyle().Colors;
        if (colors[(int)ImGuiCol.ModalWindowDimBg] == Vector4.Zero)
            colors[(int)ImGuiCol.ModalWindowDimBg] = original;
    }

    private static void HideImGuiDim()
    {
        drawnThisFrame = true;
        if (savedDim != null)
            return;
        var colors = ImGui.GetStyle().Colors;
        var current = colors[(int)ImGuiCol.ModalWindowDimBg];
        if (current == Vector4.Zero)
            return; // already hidden (another plugin's modal): nothing of ours to put back later
        savedDim = current;
        colors[(int)ImGuiCol.ModalWindowDimBg] = Vector4.Zero;
    }

    /// <summary>First in the modal's draw list: a full-screen scrim, then the panel on top. Content draws above both.</summary>
    private static void PaintBackdrop()
    {
        var dl = ImGui.GetWindowDrawList();
        var viewport = ImGui.GetMainViewport();
        var winPos = ImGui.GetWindowPos();
        var winMax = winPos + ImGui.GetWindowSize();

        dl.PushClipRect(viewport.Pos, viewport.Pos + viewport.Size, false);
        dl.AddRectFilled(viewport.Pos, viewport.Pos + viewport.Size, Theme.U32(Scrim));
        W.DrawPanel(dl, winPos, winMax);
        dl.PopClipRect();
    }

    private static void DrawHeader(string title, ref bool open)
    {
        var shown = W.Visible(title);
        var closeSize = Theme.S(26f);

        float titleH;
        using (Fonts.Display.Push())
        {
            titleH = ImGui.GetTextLineHeight();
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + MathF.Max(0f, (closeSize - titleH) * 0.5f));
            ImGui.TextColored(Theme.Ink, shown);
        }

        ImGui.SameLine();
        // Right-aligned within last frame's content width, never pushing past it (keeps auto-resize stable).
        var minX = ImGui.GetCursorPosX() + Theme.S(24f);
        var rightX = ImGui.GetWindowContentRegionMax().X - closeSize;
        ImGui.SetCursorPosX(MathF.Max(minX, rightX));
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() - MathF.Max(0f, (closeSize - titleH) * 0.5f));

        if (W.RoundButton("##modalClose", ImGui.GetCursorScreenPos(), closeSize, FontAwesomeIcon.Times, danger: true))
        {
            open = false;
            ImGui.CloseCurrentPopup();
        }

        ImGui.Dummy(new Vector2(0f, Theme.S(4f)));
    }
}
