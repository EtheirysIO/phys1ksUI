using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Dalamud.Interface;
using Dalamud.Interface.GameFonts;
using Dalamud.Interface.ManagedFontAtlas;

namespace phys1ksUI;

/// <summary>
/// Font handles for the UI type scale, built from embedded Roboto at exact pixel sizes (never
/// stretched). The game's Axis face is merged in behind Roboto for characters Roboto lacks
/// (Greek, CJK punctuation, fullwidth forms, game private-use glyphs), so item names render.
///
/// Sizes are design pixels; Dalamud's atlas (FontScaleMode.Default) applies the global UI scale.
/// All handles are created in <see cref="Initialize"/> (at plugin load, so the first frame has no
/// default-font flash) and released in <see cref="Dispose"/>.
///
/// Usage: <c>using (Fonts.Title.Push()) { ... }</c>. Before Initialize (never in practice) the
/// accessors throw. A handle that is still building pushes nothing (the current font stays), so
/// for a few frames after a text-size change icons may draw in the body font — cosmetic only.
/// </summary>
internal static class Fonts
{
    // Base sizes at 100% text size. Body matches Dalamud's default font (~16px).
    public const float DisplayPx = 24f;   // semibold: page title
    public const float TitlePx = 18f;     // semibold: card titles
    public const float LabelPx = 13f;     // semibold: tracked caps
    public const float BodyPx = 16f;      // regular: default for everything
    public const float SmallPx = 14f;     // regular: help, chips
    public const float IconPx = 16f;      // FontAwesome
    public const float IconLargePx = 20f; // FontAwesome

    /// <summary>Text size presets offered in Settings (multipliers on the base sizes).</summary>
    public static readonly float[] ScalePresets = { 0.9f, 1.0f, 1.15f, 1.3f, 1.5f };

    /// <summary>Current text size multiplier (Configuration.TextScale).</summary>
    public static float Scale { get; private set; } = 1f;

    private static IUiBuilder? builder;
    private static float? pendingScale;

    /// <summary>The largest Axis size Dalamud ships; asking for more fails the whole handle.</summary>
    private const float MaxGameGlyphPx = 36f;

    private static IFontHandle? display, title, label, body, small, icon, iconLarge;
    private static readonly List<IFontHandle> Owned = new();
    private static byte[]? regularBytes, mediumBytes;

    /// <summary>
    /// Characters Roboto does not carry, borrowed from the game's Axis face. Latin is left out on
    /// purpose so Roboto's own glyphs are not replaced. ImGui glyph range: pairs, zero-terminated.
    /// </summary>
    private static readonly ushort[] GameFallbackGlyphs =
    {
        0x0370, 0x03FF,  // Greek and Coptic
        0x0400, 0x04FF,  // Cyrillic
        0x20A0, 0x20BF,  // Currency
        0x2100, 0x214F,  // Letterlike symbols
        0x2190, 0x21FF,  // Arrows
        0x2200, 0x22FF,  // Mathematical operators
        0x2460, 0x24FF,  // Enclosed alphanumerics
        0x25A0, 0x25FF,  // Geometric shapes
        0x2600, 0x26FF,  // Misc symbols
        0x2700, 0x27BF,  // Dingbats
        0x3000, 0x30FF,  // CJK symbols/punctuation, Hiragana, Katakana
        0xE020, 0xE0FF,  // Game private-use glyphs (SeIconChar: HQ mark, auto-translate brackets...)
        0xFF00, 0xFFEF,  // Halfwidth and fullwidth forms
        0,
    };

    /// <summary>
    /// What Roboto itself provides: Latin, Latin-1, Latin Extended-A/B (EU client item names) and
    /// general punctuation (dashes, quotes, the ellipsis used by W.Fit).
    /// </summary>
    private static readonly ushort[] RobotoGlyphs =
    {
        0x0020, 0x024F,
        0x2000, 0x206F,
        0x20AC, 0x20AC,
        0,
    };

    public static IFontHandle Display => display ?? throw NotReady();
    public static IFontHandle Title => title ?? throw NotReady();
    public static IFontHandle Label => label ?? throw NotReady();
    public static IFontHandle Body => body ?? throw NotReady();
    public static IFontHandle Small => small ?? throw NotReady();
    public static IFontHandle Icon => icon ?? throw NotReady();
    public static IFontHandle IconLarge => iconLarge ?? throw NotReady();

    public static bool IsInitialized => body != null;

    /// <summary>Creates every handle in one go (one atlas build). Safe to call twice.</summary>
    public static void Initialize(IUiBuilder uiBuilder, float scale = 1f)
    {
        if (IsInitialized) return;
        builder = uiBuilder;
        Build(ClampScale(scale));
    }

    public static float ClampScale(float scale) => float.IsFinite(scale) ? Math.Clamp(scale, 0.75f, 2f) : 1f;

    /// <summary>
    /// Requests new text size. Handles are rebuilt at the start of the next frame
    /// (<see cref="ApplyPendingScale"/>), never while they may be pushed.
    /// </summary>
    public static void RequestScale(float scale)
    {
        scale = ClampScale(scale);
        if (Math.Abs(scale - Scale) > 0.001f)
            pendingScale = scale;
    }

    /// <summary>Call before any font is pushed this frame (window PreDraw).</summary>
    public static void ApplyPendingScale()
    {
        if (pendingScale is not { } scale || builder == null) return;
        pendingScale = null;
        DisposeHandles();
        Build(scale);
    }

    private static void Build(float scale)
    {
        Scale = scale;
        var atlas = builder!.FontAtlas;
        display = Text(atlas, DisplayPx * scale, semibold: true);
        title = Text(atlas, TitlePx * scale, semibold: true);
        label = Text(atlas, LabelPx * scale, semibold: true);
        body = Text(atlas, BodyPx * scale, semibold: false);
        small = Text(atlas, SmallPx * scale, semibold: false);
        icon = IconFont(atlas, IconPx * scale);
        iconLarge = IconFont(atlas, IconLargePx * scale);
    }

    public static void Dispose()
    {
        DisposeHandles();
        builder = null;
    }

    private static void DisposeHandles()
    {
        foreach (var handle in Owned)
        {
            try { handle.Dispose(); }
            catch (Exception ex) { Kit.Log?.Warning($"Font handle dispose failed: {ex.Message}"); }
        }
        Owned.Clear();
        display = title = label = body = small = icon = iconLarge = null;
    }

    private static IFontHandle Text(IFontAtlas atlas, float px, bool semibold)
    {
        var bytes = Typeface(semibold);
        var handle = atlas.NewDelegateFontHandle(tk => tk.OnPreBuild(pre =>
        {
            var face = pre.AddFontFromMemory(bytes, new SafeFontConfig { SizePx = px, GlyphRanges = RobotoGlyphs }, "Roboto");
            pre.AddGameGlyphs(new GameFontStyle(GameFontFamily.Axis, MathF.Min(px, MaxGameGlyphPx)),
                GameFallbackGlyphs, face);
        }));
        Owned.Add(handle);
        return handle;
    }

    private static IFontHandle IconFont(IFontAtlas atlas, float px)
    {
        var handle = atlas.NewDelegateFontHandle(tk => tk.OnPreBuild(pre =>
            pre.AddFontAwesomeIconFont(new SafeFontConfig { SizePx = px })));
        Owned.Add(handle);
        return handle;
    }

    /// <summary>Embedded Roboto bytes (phys1ksUI.props embeds them as phys1ksUI.Fonts.Roboto-*.ttf).</summary>
    private static byte[] Typeface(bool semibold)
    {
        ref var slot = ref semibold ? ref mediumBytes : ref regularBytes;
        if (slot != null) return slot;

        var suffix = semibold ? "Fonts.Roboto-Medium.ttf" : "Fonts.Roboto-Regular.ttf";
        var assembly = typeof(Fonts).Assembly;
        var name = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Embedded font {suffix} is missing from the assembly.");

        using var stream = assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Embedded font {name} could not be opened.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return slot = buffer.ToArray();
    }

    private static InvalidOperationException NotReady() =>
        new("Fonts.Initialize() has not been called.");
}
