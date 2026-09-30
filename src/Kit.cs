using Dalamud.Interface;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace phys1ksUI;

/// <summary>The user-selectable UI accent: colors, then grays. Orange is the default; hex values are in <see cref="Theme.AccentValue"/>.</summary>
public enum AccentColor
{
    Orange,
    Purple,
    Indigo,
    Blue,
    Teal,
    Green,
    Pink,

    // Grays (appended so saved values keep their meaning).
    Graphite,
    Gray,
    Silver,
    White,
}

/// <summary>
/// phys1ksUI: the shared look for phys1ks's Dalamud plugins (from Invenwhorey's design). Call <see cref="Initialize"/>
/// once in the plugin's constructor (before any window draws) and <see cref="Dispose"/> in its Dispose.
/// </summary>
internal static class Kit
{
    internal static IPluginLog? Log { get; private set; }
    internal static ITextureProvider? Textures { get; private set; }
    internal static IDataManager? Data { get; private set; }
    private static IUiBuilder? uiBuilder;

    /// <param name="textScale">The saved text size (1 = 100%); see <see cref="Appearance"/>.</param>
    /// <param name="accent">The saved accent color.</param>
    /// <param name="colorblind">The saved colorblind mode (see <see cref="Theme.SetColorblind"/>).</param>
    public static void Initialize(IDalamudPluginInterface pluginInterface, IPluginLog log, ITextureProvider textures, IDataManager data,
                                  float textScale = 1f, AccentColor accent = AccentColor.Orange, bool colorblind = false)
    {
        Theme.SetColorblind(colorblind);
        Log = log;
        Textures = textures;
        Data = data;
        Theme.SetAccent(accent);
        Fonts.Initialize(pluginInterface.UiBuilder, textScale);
        // Before the plugin's own Draw handler (it subscribes after this), so it runs ahead of every window.
        if (uiBuilder != null)
            uiBuilder.Draw -= Modal.BeginFrame; // initialized twice: don't hook twice
        uiBuilder = pluginInterface.UiBuilder;
        uiBuilder.Draw += Modal.BeginFrame;
    }

    /// <summary>Dalamud's "reduce motion" setting: animations jump straight to their end.</summary>
    internal static bool ReducedMotion => uiBuilder?.ShouldUseReducedMotion ?? false;

    public static void Dispose()
    {
        if (uiBuilder != null)
            uiBuilder.Draw -= Modal.BeginFrame;
        uiBuilder = null;
        Modal.RestoreDim();
        Fonts.Dispose();
        Log = null;
        Textures = null;
        Data = null;
    }
}
