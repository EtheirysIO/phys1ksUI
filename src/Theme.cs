using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace phys1ksUI;

/// <summary>
/// Design tokens for phys1ks's plugins (phys1ksUI): palette, accent, radii, spacing, and the global
/// ImGui style push. Every pixel value goes through <see cref="S"/> so it follows Dalamud's
/// global UI scale and the Settings text size. Font sizes do NOT: the font atlas already scales them (see <see cref="Fonts"/>).
/// </summary>
internal static class Theme
{
    // ───────────────────────── Palette ─────────────────────────

    public static readonly Vector4 Ground = Hex(0x000000);        // window background
    public static readonly Vector4 Panel = Hex(0x141416);         // sidebar, header, popups
    public static readonly Vector4 Field = Hex(0x1c1c1e);         // cards, inputs, table
    public static readonly Vector4 Raised = Hex(0x2c2c2e);        // hover
    public static readonly Vector4 Raised2 = Hex(0x3a3a3c);       // pressed
    public static readonly Vector4 RuleHair = Hex(0x2c2c2e);
    public static readonly Vector4 RuleStrong = Hex(0x38383a);
    public static readonly Vector4 BorderControl = Hex(0x48484a);
    public static readonly Vector4 CardBorder = Wash(0.12f);
    public static readonly Vector4 Ink = Hex(0xf5f5f7);
    public static readonly Vector4 Dim = Hex(0xa8a8ad);
    public static readonly Vector4 Faint = Hex(0x7c7c82);
    public static readonly Vector4 Slate = Hex(0x94a3b8);         // tracked-caps headings
    /// <summary>Text and glyphs on a red (Negative) fill: always white.</summary>
    public static readonly Vector4 OnNegative = Hex(0xffffff);
    public static readonly Vector4 Negative = Hex(0xff453a);
    public static readonly Vector4 Warning = Hex(0xffd60a);       // NOT orange: orange is the accent
    public static readonly Vector4 HQGold = Hex(0xe0b95a);
    public static readonly Vector4 Link = Hex(0x64d2ff);         // clickable text (item names...)
    public static readonly Vector4 Transparent = new(0f, 0f, 0f, 0f);

    /// <summary>
    /// A white wash at <paramref name="alpha"/>: hover and selection tints over dark surfaces, neutral pill fills, the
    /// highlight along a card's top edge. Typical alphas: 0.04 hover, 0.06-0.07 neutral fill, 0.12 border.
    /// </summary>
    public static Vector4 Wash(float alpha) => new(1f, 1f, 1f, alpha);

    /// <summary>A black shade at <paramref name="alpha"/>: drop shadows (0.07 per card layer, 0.35 under the brand tile) and scrims.</summary>
    public static Vector4 Shadow(float alpha) => new(0f, 0f, 0f, alpha);

    // ───────────────────────── Accent ─────────────────────────

    private static AccentColor currentAccent;
    private static Vector4 accent;
    private static Vector4 accentHover;
    private static Vector4 accentPressed;
    private static Vector4 onAccent;

    // One path computes an accent's shades, so the default looks the same as picking it again later.
    static Theme() => ApplyAccent(AccentColor.Orange);

    /// <summary>The accent the UI currently draws with.</summary>
    public static AccentColor CurrentAccent => currentAccent;
    public static Vector4 Accent => accent;
    public static Vector4 AccentHover => accentHover;
    public static Vector4 AccentPressed => accentPressed;
    public static Vector4 AccentAlpha(float alpha) => accent with { W = alpha };

    /// <summary>Text and glyphs on an accent fill: white, or near-black on the light gray accents.</summary>
    public static Vector4 OnAccent => onAccent;

    /// <summary>Every selectable accent, in picker order.</summary>
    public static readonly AccentColor[] AccentChoices =
    {
        AccentColor.Orange, AccentColor.Purple, AccentColor.Indigo, AccentColor.Blue,
        AccentColor.Teal, AccentColor.Green, AccentColor.Pink,
        AccentColor.Graphite, AccentColor.Gray, AccentColor.Silver, AccentColor.White,
    };

    /// <summary>The grays, drawn as their own group in the picker.</summary>
    public static bool IsGray(AccentColor c) => c >= AccentColor.Graphite;

    public static Vector4 AccentValue(AccentColor c) => c switch
    {
        AccentColor.Purple => Hex(0xbf5af2),
        AccentColor.Indigo => Hex(0x5e5ce6),
        AccentColor.Blue => Hex(0x0a84ff),
        AccentColor.Teal => Hex(0x40c8e0),
        AccentColor.Green => Hex(0x30d158),
        AccentColor.Pink => Hex(0xda5bd6),
        AccentColor.Graphite => Hex(0x636366),
        AccentColor.Gray => Hex(0x8e8e93),
        AccentColor.Silver => Hex(0xc7c7cc),
        AccentColor.White => Hex(0xf2f2f7),
        _ => Hex(0xff9f0a),
    };

    public static string AccentName(AccentColor c) => c.ToString();

    /// <summary>Applies an accent (cheap no-op when unchanged). Called every frame from PreDraw.</summary>
    public static void SetAccent(AccentColor c)
    {
        if (c != currentAccent)
            ApplyAccent(c);
    }

    /// <summary>
    /// Luminance above which an accent counts as light (near-black text on it, darker on hover): only Silver and White.
    /// The colors (Orange 0.46, Green 0.47, Teal 0.48) stay below it and keep white text.
    /// </summary>
    private const float LightAccent = 0.5f;

    private static void ApplyAccent(AccentColor c)
    {
        currentAccent = c;
        accent = AccentValue(c);
        var light = Luminance(accent) > LightAccent;
        // Light accents can't get lighter on hover, so they darken a little instead.
        accentHover = light ? Darken(accent, 0.08f) : Lighten(accent, 0.12f);
        accentPressed = Darken(accent, light ? 0.2f : 0.15f);
        onAccent = light ? Hex(0x1c1c1e) : Hex(0xffffff);
    }

    /// <summary>Relative luminance (0 black .. 1 white) of an sRGB color.</summary>
    private static float Luminance(Vector4 c)
    {
        static float Lin(float v) => v <= 0.04045f ? v / 12.92f : MathF.Pow((v + 0.055f) / 1.055f, 2.4f);
        return 0.2126f * Lin(c.X) + 0.7152f * Lin(c.Y) + 0.0722f * Lin(c.Z);
    }

    // ───────────────────────── Colorblind mode ─────────────────────────

    private static bool colorblind;
    private static readonly Vector4 Green = Hex(0x30d158);
    private static readonly Vector4 SafeBlue = Hex(0x409cff);

    /// <summary>
    /// Good / done / loaded. Green normally; blue in colorblind mode, because red and green look alike with red/green
    /// color blindness (protanopia, deuteranopia) while blue and red stay far apart.
    /// </summary>
    public static Vector4 Positive => colorblind ? SafeBlue : Green;

    /// <summary>Whether colorblind mode is on: <see cref="Positive"/> is blue, and status dots also carry a check or a cross.</summary>
    public static bool Colorblind => colorblind;

    /// <summary>Turns colorblind mode on or off. Called every frame from KitWindow.PreDraw.</summary>
    public static void SetColorblind(bool on) => colorblind = on;

    /// <summary>
    /// In colorblind mode, the shape that carries a good / bad color: a check for <see cref="Positive"/>, a cross for
    /// <see cref="Negative"/> (compared by RGB, so faded or tinted versions of them count), otherwise null. Always null
    /// with colorblind mode off.
    /// </summary>
    public static FontAwesomeIcon? ColorblindShape(Vector4 color)
        => !colorblind ? null
         : SameRgb(color, Positive) ? FontAwesomeIcon.Check
         : SameRgb(color, Negative) ? FontAwesomeIcon.Times
         : null;

    /// <summary>Whether two colors have the same RGB (alpha ignored).</summary>
    public static bool SameRgb(Vector4 a, Vector4 b)
        => MathF.Abs(a.X - b.X) < 0.002f && MathF.Abs(a.Y - b.Y) < 0.002f && MathF.Abs(a.Z - b.Z) < 0.002f;

    // ───────────────────────── Scale / geometry ─────────────────────────

    /// <summary>Scales a design pixel value by Dalamud's global UI scale and the Settings text size, so layouts grow with the text.</summary>
    public static float S(float px) => px * Dalamud.Interface.Utility.ImGuiHelpers.GlobalScale * Fonts.Scale;

    public static Vector2 S(float x, float y) => new Vector2(x, y) * (Dalamud.Interface.Utility.ImGuiHelpers.GlobalScale * Fonts.Scale);

    /// <summary>Corner radii (already scaled).</summary>
    public static class Radius
    {
        public static float Window => S(18f);
        public static float Card => S(12f);
        public static float Control => S(10f);
        public static float Small => S(8f);
        public static float Chip => S(6f);
        public static float Pill => S(999f);
    }

    /// <summary>Spacing and fixed sizes (already scaled).</summary>
    public static class Space
    {
        public static float Tight => S(8f);
        public static float Gap => S(10f);
        public static float Gutter => S(12f);
        public static float CardPad => S(14f);
        public static float ButtonHeight => S(34f);
        public static float RowHeight => S(30f);
        public static float SidebarWidth => S(210f);
        public static float HeaderHeight => S(52f);
        public static float NavRowHeight => S(36f);
        public static float BodyPad => S(16f);
    }

    // ───────────────────────── Color math ─────────────────────────

    public static Vector4 Hex(uint rgb, float alpha = 1f) => new(
        ((rgb >> 16) & 0xFF) / 255f,
        ((rgb >> 8) & 0xFF) / 255f,
        (rgb & 0xFF) / 255f,
        alpha);

    /// <summary>
    /// Packs a color for DrawList calls, multiplied by the current style alpha so custom widgets
    /// dim correctly inside BeginDisabled() blocks like native ones do.
    /// </summary>
    public static uint U32(Vector4 c)
    {
        float alpha = ImGui.GetStyle().Alpha;
        return ImGui.ColorConvertFloat4ToU32(c with { W = c.W * alpha });
    }

    public static Vector4 Lerp(Vector4 a, Vector4 b, float t) => Vector4.Lerp(a, b, Math.Clamp(t, 0f, 1f));

    /// <summary>Smoothstep easing.</summary>
    public static float Ease(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    /// <summary>Blend towards white in linear light (keeps hue).</summary>
    public static Vector4 Lighten(Vector4 c, float amount) => MixLinear(c, 1f, amount);

    /// <summary>Blend towards black in linear light.</summary>
    public static Vector4 Darken(Vector4 c, float amount) => MixLinear(c, 0f, amount);

    private static Vector4 MixLinear(Vector4 c, float target, float amount)
    {
        static float ToLinear(float v) => MathF.Pow(v, 2.2f);
        static float ToSrgb(float v) => MathF.Pow(Math.Max(0f, v), 1f / 2.2f);

        float t = Math.Clamp(amount, 0f, 1f);
        float lt = ToLinear(Math.Clamp(target, 0f, 1f));
        return new Vector4(
            ToSrgb(ToLinear(c.X) + (lt - ToLinear(c.X)) * t),
            ToSrgb(ToLinear(c.Y) + (lt - ToLinear(c.Y)) * t),
            ToSrgb(ToLinear(c.Z) + (lt - ToLinear(c.Z)) * t),
            c.W);
    }

    // ───────────────────────── Global style push ─────────────────────────

    /// <summary>
    /// Pushes every theme color and style var. Dispose the result to pop exactly what was pushed
    /// (disposing twice is a no-op).
    /// </summary>
    public static IDisposable Push()
    {
        // Pushed one by one (no per-frame arrays): the accent-derived colors change at runtime, so a static table
        // of values would go stale.
        var colors = 0;
        void C(ImGuiCol col, Vector4 value)
        {
            ImGui.PushStyleColor(col, value);
            colors++;
        }

        C(ImGuiCol.WindowBg, Ground);
        C(ImGuiCol.ChildBg, Transparent);
        C(ImGuiCol.PopupBg, Panel);
        C(ImGuiCol.FrameBg, Field);
        C(ImGuiCol.FrameBgHovered, Raised);
        C(ImGuiCol.FrameBgActive, Raised);
        C(ImGuiCol.Button, Field);
        C(ImGuiCol.ButtonHovered, Raised);
        C(ImGuiCol.ButtonActive, Raised2);
        C(ImGuiCol.Header, Raised);
        C(ImGuiCol.HeaderHovered, Raised);
        C(ImGuiCol.HeaderActive, AccentAlpha(0.33f));
        C(ImGuiCol.CheckMark, Accent);
        C(ImGuiCol.SliderGrab, Accent);
        C(ImGuiCol.SliderGrabActive, AccentPressed);
        C(ImGuiCol.NavHighlight, Accent);
        C(ImGuiCol.ScrollbarBg, Transparent);
        C(ImGuiCol.ScrollbarGrab, RuleStrong);
        C(ImGuiCol.ScrollbarGrabHovered, BorderControl);
        C(ImGuiCol.ScrollbarGrabActive, Accent);
        C(ImGuiCol.Separator, RuleHair);
        C(ImGuiCol.SeparatorHovered, RuleStrong);
        C(ImGuiCol.SeparatorActive, Accent);
        C(ImGuiCol.Border, BorderControl);
        C(ImGuiCol.BorderShadow, Transparent);
        C(ImGuiCol.Text, Ink);
        C(ImGuiCol.TextDisabled, Faint);
        C(ImGuiCol.TableHeaderBg, Panel);
        C(ImGuiCol.TableRowBg, Transparent);
        C(ImGuiCol.TableRowBgAlt, Wash(0.025f));
        C(ImGuiCol.TableBorderLight, RuleHair);
        C(ImGuiCol.TableBorderStrong, RuleHair);
        C(ImGuiCol.Tab, Field);
        C(ImGuiCol.TabHovered, Raised);
        C(ImGuiCol.TabActive, Accent);
        C(ImGuiCol.TabUnfocused, Field);
        C(ImGuiCol.TabUnfocusedActive, AccentAlpha(0.6f));
        C(ImGuiCol.ResizeGrip, Transparent);
        C(ImGuiCol.ResizeGripHovered, AccentAlpha(0.4f));
        C(ImGuiCol.ResizeGripActive, AccentAlpha(0.4f));
        C(ImGuiCol.PlotHistogram, Accent);
        C(ImGuiCol.PlotHistogramHovered, AccentHover);
        C(ImGuiCol.TextSelectedBg, AccentAlpha(0.35f));

        var vars = 0;
        void V(ImGuiStyleVar v, float value)
        {
            ImGui.PushStyleVar(v, value);
            vars++;
        }
        void V2(ImGuiStyleVar v, Vector2 value)
        {
            ImGui.PushStyleVar(v, value);
            vars++;
        }

        V(ImGuiStyleVar.FrameRounding, Radius.Control);
        V(ImGuiStyleVar.WindowRounding, Radius.Window);
        V(ImGuiStyleVar.ChildRounding, Radius.Card);
        V(ImGuiStyleVar.PopupRounding, Radius.Control);
        V(ImGuiStyleVar.GrabRounding, Radius.Pill);
        V(ImGuiStyleVar.ScrollbarRounding, Radius.Pill);
        V(ImGuiStyleVar.TabRounding, Radius.Small);
        V(ImGuiStyleVar.ScrollbarSize, S(10f));
        V(ImGuiStyleVar.WindowBorderSize, 0f);
        V(ImGuiStyleVar.FrameBorderSize, 0f);
        V(ImGuiStyleVar.PopupBorderSize, 0f);
        V2(ImGuiStyleVar.FramePadding, S(12f, 7f));
        V2(ImGuiStyleVar.ItemSpacing, S(10f, 8f));
        V2(ImGuiStyleVar.CellPadding, S(8f, 5f));

        return new StyleScope(colors, vars);
    }

    /// <summary>
    /// Counted pop for pushes made by this class or by callers. Use <see cref="StyleScope.Color"/> /
    /// <see cref="StyleScope.Var"/> to push more onto an existing scope.
    /// </summary>
    public sealed class StyleScope : IDisposable
    {
        private int colors;
        private int vars;
        private bool disposed;

        public StyleScope(int colors = 0, int vars = 0)
        {
            this.colors = colors;
            this.vars = vars;
        }

        public StyleScope Color(ImGuiCol col, Vector4 value)
        {
            ImGui.PushStyleColor(col, value);
            colors++;
            return this;
        }

        public StyleScope Var(ImGuiStyleVar v, float value)
        {
            ImGui.PushStyleVar(v, value);
            vars++;
            return this;
        }

        public StyleScope Var(ImGuiStyleVar v, Vector2 value)
        {
            ImGui.PushStyleVar(v, value);
            vars++;
            return this;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (KitRecovery.Unwinding)
                return; // the catch site's recovery pops style back to its window's start
            if (vars > 0) ImGui.PopStyleVar(vars);
            if (colors > 0) ImGui.PopStyleColor(colors);
        }
    }
}
