using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Dalamud.Bindings.ImGui;

namespace phys1ksUI;

/// <summary>
/// Puts ImGui's stacks back after a draw threw part-way.
/// <para>
/// How it works: C# disposes the <c>using</c> scopes between a throw and its <c>catch</c> BEFORE the catch runs, and those
/// scopes (cards, fold cards, combos, style scopes) would pop their ImGui state while whatever the page left open (a child
/// window, a raw table) is still current. So every kit catch site is written
/// <c>catch (Exception ex) when (KitRecovery.Catch())</c>: an exception filter runs before any of those disposals, and
/// sets <see cref="Unwinding"/>. While it is set the kit's scopes pop nothing: they only fix the kit's own counters and
/// hand what ImGui's recovery can't undo (indent, item width, a card's draw-list channels, a clip rect) to
/// <see cref="Defer"/>. The catch then calls <see cref="RecoverTo"/>, which ends everything down to the catch site's
/// window with ImGui's own ErrorCheckEndWindowRecover, runs the deferred undos that belong to that window, and clears
/// <see cref="Unwinding"/>.
/// </para>
/// Fonts pushed without a <c>using</c> are not recovered.
/// </summary>
internal static class KitRecovery
{
    private static readonly unsafe ImGuiErrorLogCallback Log = LogRecovery;
    private static readonly List<(uint Window, Action Undo)> Deferred = new();

    /// <summary>True between a kit catch site's filter and its <see cref="RecoverTo"/>: scopes must not pop ImGui state.</summary>
    public static bool Unwinding { get; private set; }

    /// <summary>The exception filter for kit catch sites: <c>catch (Exception ex) when (KitRecovery.Catch())</c>. Always true.</summary>
    public static bool Catch()
    {
        Unwinding = true;
        return true;
    }

    /// <summary>
    /// While <see cref="Unwinding"/>: an undo for state ImGui's recovery doesn't restore, run after recovery if
    /// <paramref name="windowId"/> is still the current window (a window recovery ended is reset by ImGui next frame).
    /// Undos run in the order they were deferred, which is innermost scope first.
    /// </summary>
    public static void Defer(uint windowId, Action undo) => Deferred.Add((windowId, undo));

    /// <summary>
    /// Recovers to the window with <paramref name="windowId"/> (see <see cref="W.CurrentWindowId"/>), which stays open:
    /// ends the child windows, popups and tooltips opened inside it, and in each closes tables, tab bars, tree nodes,
    /// groups and disabled blocks and pops IDs, style colors / vars and item flags back to what they were when it began.
    /// Then runs the deferred undos and ends the unwind.
    /// </summary>
    public static void RecoverTo(uint windowId)
    {
        try
        {
            EndWindowsDownTo(windowId);
            foreach (var (window, undo) in Deferred)
                if (W.CurrentWindowId() == window)
                    undo();
        }
        catch (Exception ex)
        {
            Kit.Log?.Warning($"ImGui state recovery failed: {ex.Message}");
        }
        finally
        {
            Deferred.Clear();
            Unwinding = false;
        }
    }

    private static void EndWindowsDownTo(uint windowId)
    {
        // Bounded: a window that can't be ended must not spin forever.
        for (var guard = 0; guard < 64; guard++)
        {
            var window = ImGuiP.GetCurrentWindow();
            if (window.IsNull)
                return;
            var id = window.ID;
            var flags = window.Flags;
            ImGuiP.ErrorCheckEndWindowRecover(Log);
            if (id == windowId || windowId == 0)
                return;
            // Ending a scrolling table also ends its inner child window: then this window is already gone.
            var after = ImGuiP.GetCurrentWindow();
            if (after.IsNull)
                return;
            if (after.ID != id)
                continue;
            if ((flags & ImGuiWindowFlags.Tooltip) != 0)
                ImGui.EndTooltip();
            else if ((flags & ImGuiWindowFlags.Popup) != 0)
                ImGui.EndPopup();
            else if ((flags & ImGuiWindowFlags.ChildWindow) != 0)
                ImGui.EndChild();
            else
                return; // top-level windows are ended by Dalamud's WindowSystem, never by us
        }
    }

    private static unsafe void LogRecovery(void* userData, byte* message)
        => Kit.Log?.Verbose($"ImGui recovery: {Marshal.PtrToStringUTF8((nint)message)}");
}
