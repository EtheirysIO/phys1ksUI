using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace phys1ksUI;

/// <summary>
/// The standard "Appearance" settings card every plugin shows on its Settings page: accent color swatches (colors, then
/// grays), the text size picker and colorblind mode. The plugin owns where the values are saved.
/// </summary>
internal static class Appearance
{
    private static readonly string[] TextSizeLabels = ["90%", "100%", "115%", "130%", "150%"];

    /// <summary>Draws the card. Returns true when something changed (save your config).</summary>
    public static bool DrawCard(ref AccentColor accent, ref float textScale, ref bool colorblind)
    {
        var changed = false;
        using (W.Card("phys1ksAppearance", "Appearance"))
        {
            ImGui.TextColored(Theme.Dim, "Accent color");
            ImGui.Dummy(new Vector2(0f, Theme.S(2f)));
            changed |= DrawAccentSwatches(ref accent);

            ImGui.Dummy(new Vector2(0f, Theme.Space.Gap));
            ImGui.TextColored(Theme.Dim, "Text size");
            ImGui.Dummy(new Vector2(0f, Theme.S(2f)));
            var current = textScale;
            var index = Array.FindIndex(Fonts.ScalePresets, p => Math.Abs(p - current) < 0.01f);
            if (index < 0)
                index = 1;
            if (W.Segmented("##textSize", TextSizeLabels, ref index, Theme.S(360f)))
            {
                textScale = Fonts.ScalePresets[index];
                Fonts.RequestScale(textScale);
                changed = true;
            }

            ImGui.Dummy(new Vector2(0f, Theme.Space.Gap));
            if (W.Toggle("Colorblind mode##phys1ksColorblind", ref colorblind))
            {
                Theme.SetColorblind(colorblind);
                changed = true;
            }
            ImGui.SameLine();
            W.HelpMark("For red/green color blindness: green (good, done, loaded) turns blue, and status dots get a check or a " +
                       "cross, so nothing depends on telling red from green.");

            ImGui.Dummy(new Vector2(0f, Theme.Space.Gap));
            W.TextWrapped($"Everything also follows Dalamud's global UI scale (now {Dalamud.Interface.Utility.ImGuiHelpers.GlobalScale:0.##}x, " +
                          "in /xlsettings > Look & Feel).", Theme.Faint);
        }
        return changed;
    }

    private static bool DrawAccentSwatches(ref AccentColor accent)
    {
        var swatch = Theme.S(28f);
        var gap = Theme.S(12f);
        var ring = Theme.S(3f);
        var dl = ImGui.GetWindowDrawList();
        var changed = false;

        // Leave room for the selection ring on the first swatch.
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ring);

        for (var i = 0; i < Theme.AccentChoices.Length; i++)
        {
            var choice = Theme.AccentChoices[i];
            if (i > 0)
                ImGui.SameLine(0f, Theme.IsGray(choice) && !Theme.IsGray(Theme.AccentChoices[i - 1]) ? gap * 2.5f : gap);

            var p = ImGui.GetCursorScreenPos();
            ImGui.PushID((int)choice);
            var clicked = ImGui.InvisibleButton("##accent", new Vector2(swatch, swatch));
            ImGui.PopID();
            var hovered = ImGui.IsItemHovered();
            var chosen = accent == choice;

            if (clicked && !chosen)
            {
                accent = choice;
                Theme.SetAccent(choice);
                changed = true;
            }

            var center = p + new Vector2(swatch * 0.5f);
            dl.AddCircleFilled(center, swatch * 0.5f, Theme.U32(Theme.AccentValue(choice)), 32);
            if (chosen)
                dl.AddCircle(center, swatch * 0.5f + ring, Theme.U32(Theme.Ink), 32, Theme.S(2f));
            else if (hovered)
                dl.AddCircle(center, swatch * 0.5f + ring, Theme.U32(Theme.Ink with { W = 0.3f }), 32, Theme.S(1.5f));

            if (hovered)
            {
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                W.Tooltip(Theme.AccentName(choice) + (choice == AccentColor.Orange ? " (default)" : string.Empty));
            }
        }
        return changed;
    }
}
