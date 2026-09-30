# Moving Invenwhorey onto phys1ksUI

This is for whoever is working in `N:\FFXIV\Invenwhorey`. Invenwhorey's current UI is now the shared design system for all of phys1ks's
plugins. It lives in `N:\FFXIV\phys1ksUI`, and MakeShopper already runs on it. Invenwhorey should use it too, so a fix to the kit
fixes every plugin.

**The kit is a superset of Invenwhorey's current UI code.** Checked against the working tree on 2026-09-29:

| Kit file | What it is |
| --- | --- |
| `src/Theme.cs` | `UI/Theme/Theme.cs`, plus `Theme.Link`, four gray accents and colorblind mode (see step 3). |
| `src/Fonts.cs` | `UI/Theme/Fonts.cs`. It uses `Kit.Log` instead of `Plugin.Log`. |
| `src/Widgets.cs` | `UI/Theme/Widgets.cs`. It uses `Kit.Textures` / `Kit.Data` instead of `Plugin.TextureProvider` / `Plugin.DataManager`, and `W` is now `partial`. |
| `src/Modal.cs` | `UI/ImGuiHelpers.cs`, including the dialog-dimming fix. |
| `src/KitWindow.cs` | `UI/InventoryWindow.Shell.cs`, turned into a base class. |
| `src/Appearance.cs` | The accent swatches and text-size card from `InventoryWindow.Settings.cs`. |
| `src/Widgets.Extra.cs` | New widgets. |

`src/Widgets.Extra.cs` adds:
- `FoldCard` and `CompactButton`.
- `Banner` and `Chip`.
- `Spacer` and `TextWrapped`.
- `Link` and `Stat`.
- `RightAlign`.
- `Table`, `FixedColumn` and `TableHeaders`.
- `NavRow`, with an optional subtitle.

Nothing visual should change. This is a move, not a redesign. Read `README.md` in the kit for the design rules.

**Don't fork the kit.** If a widget is missing or wrong, change it in `N:\FFXIV\phys1ksUI\src` so every plugin gets the change. Tell the
user, because MakeShopper builds against the same files.

## How the kit is shared

- It is source, not a DLL. `phys1ksUI.props` compiles `src\**\*.cs` into the plugin under namespace `phys1ksUI`. Everything in it is
  `internal`.
- The props file also embeds Roboto as `phys1ksUI.Fonts.Roboto-*.ttf` and copies `LICENSE-Roboto.txt` next to the dll, so it ends up
  in `latest.zip`.

## Steps

### 1. csproj (`Invenwhorey.csproj`, repo root)

- Add this, next to the other ItemGroups:

  ```xml
  <!-- phys1ksUI: the shared look (source, Roboto, license). -->
  <Import Project="..\phys1ksUI\phys1ksUI.props" />
  ```

- Remove `<EmbeddedResource Include="Data\Fonts\*.ttf" />` and `<None Include="Data\Fonts\LICENSE-Roboto.txt" />`, then delete
  `Data\Fonts\`. The kit ships the same Roboto v2 files. `Fonts.cs` finds them by the suffix `Fonts.Roboto-Medium.ttf`, and
  Invenwhorey's own `Invenwhorey.Data.Fonts.Roboto-Medium.ttf` matches that suffix too. Leave both in and you get two matching
  resources.
- Keep `Data\PatchRanges.csv`.

### 2. Delete the local copies

Delete these files:
- `UI/Theme/Theme.cs`
- `UI/Theme/Fonts.cs`
- `UI/Theme/Widgets.cs`
- `UI/ImGuiHelpers.cs`

Before you delete `ImGuiHelpers.cs`, check it only holds the modal code. It did when checked.

Delete `Models/AccentColor.cs`. The kit defines `phys1ksUI.AccentColor` with the same first seven members in the same order: Orange,
Purple, Indigo, Blue, Teal, Green, Pink. Saved configs keep their value. The kit appends Graphite, Gray, Silver and White after them.

In every UI file that used them, add `using phys1ksUI;`. `W`, `Theme`, `Fonts`, `ButtonKind`, `Modal` and `AccentColor` all come from
there.

Watch the two helper classes: `ImGuiHelpers.GlobalScale` in `InventoryWindow.Settings.cs` is **Dalamud's**
`Dalamud.Interface.Utility.ImGuiHelpers`. It keeps working once the local `ImGuiHelpers` class is gone.

### 3. Configuration

Keep the property names `AccentColor` and `TextScale`. Only the enum's type changes to `phys1ksUI.AccentColor`. The
`Enum.IsDefined` guard can stay.

Add `public bool Colorblind { get; set; }` for colorblind mode. In that mode `Theme.Positive` turns blue instead of green, and status
dots also get a check or a cross.

Two Theme members changed from fields to properties:
- `Theme.Positive` now follows colorblind mode.
- `Theme.OnAccent` is near-black on the light gray accents, and white otherwise.

Anything on a **red** fill should use the new `Theme.OnNegative`, which is always white, not `OnAccent`. The kit's own widgets
already do.

### 4. Plugin.cs

- Replace `Fonts.Initialize(PluginInterface.UiBuilder, Configuration.TextScale);` with the line below. Put it **before**
  `UiBuilder.Draw += DrawUI`.

  ```csharp
  Kit.Initialize(PluginInterface, Log, TextureProvider, DataManager, Configuration.TextScale, Configuration.AccentColor,
                 Configuration.Colorblind);
  ```

- `Kit.Initialize` also hooks the per-frame modal-dim reset. Remove `ImGuiHelpers.BeginFrame();` from `DrawUI`, which then just
  calls `windowSystem.Draw()`.
- In `Dispose`, replace `ImGuiHelpers.RestoreModalDim();` and `Fonts.Dispose();` with `Kit.Dispose();`. It restores the dim color
  and disposes the fonts.

### 5. Dialogs

Rename `ImGuiHelpers.DrawCenteredModal(title, ref open, body[, flags])` to `Modal.Draw(title, ref open, body[, flags])`. The
signature and behavior are identical, and there are 9 call sites:
- Dialogs ×5
- Filters ×2
- Armory ×1
- Organizer ×1

### 6. The window: `InventoryWindow : KitWindow`

`KitWindow` (`src/KitWindow.cs`) is `InventoryWindow.Shell.cs` turned into a base class.

**Accessibility:** it is `internal`, so `public partial class InventoryWindow` must become `internal partial class InventoryWindow`,
or you get CS0060. `Plugin.InventoryWindow` is already private, so nothing else changes.

**Constructor:**

```csharp
public InventoryWindow(Plugin plugin)
    : base("Invenwhorey", "Invenwhorey", FontAwesomeIcon.Boxes, new Vector2(1000, 600))
```

- The first argument is the same ImGui window name as today, so saved position and size carry over.
- The last argument is the minimum size at 100% text. The kit scales it by the text size, like the current `PreDraw` does.
- The kit sets the window flags (NoTitleBar, NoScrollbar, NoScrollWithMouse, NoCollapse).
- Remove the `SizeConstraints` block from the constructor.

**Delete from `InventoryWindow.Shell.cs`:**
- `PreDraw` and `PostDraw`: the kit sets the accent, calls `ApplyPendingScale`, sets size constraints, pushes the theme and zeroes
  window padding.
- `Draw`, `DrawShell`, `DrawSidebar`, `DrawBrand`, `DrawNavRow`, `DrawSidebarStatus`, `DrawHeaderStrip`, `DrawRoundCloseButton` and
  `DrawResizeDots`.
- The private `HeaderSlot` record. The kit's `phys1ksUI.HeaderSlot` has the same `Min`/`Max`/`Width`/`Height`/`CenterY`.
- `VersionLabel()`. Use the inherited `Version` property, for example in the About card: `$"Invenwhorey {Version}"`.

**Keep** the `Page` enum, `NavItems`, `activePage`, and the page hooks
(`partial void DrawInventoryHeaderRight(HeaderSlot slot)` and so on).

**Implement these overrides:**

```csharp
protected override AccentColor Accent => plugin.Configuration.AccentColor;

protected override bool Colorblind => plugin.Configuration.Colorblind;

protected override string PageTitle => PageTitleText(activePage);

// Nav rows now flow in a scrolling child, full width; the kit draws the active glow/fill.
protected override void DrawSidebarNav()
{
    foreach (var (page, icon, label) in NavItems)
        if (W.NavRow(page.ToString(), icon, label, activePage == page))
            activePage = page;
}

protected override void DrawBody()
{
    bool loggedIn = Plugin.ObjectTable.LocalPlayer != null;
    DrawActivePage(loggedIn); // unchanged
}

protected override void DrawHeaderRight(HeaderSlot slot)
{
    switch (activePage) { /* same switch as the current DrawHeaderRight */ }
}

// Map the old tuple: (Label, Detail, Fraction, Cancel) -> new RunningOperation(label, detail, fraction, cancel).
// Fraction is float? now: null hides the progress bar; the Invenwhorey handlers always pass a number, which keeps the bar.
protected override RunningOperation? GetRunningOperation() { /* same three ifs as today, returning new RunningOperation(...) */ }

protected override IReadOnlyList<StatusLine> GetStatusLines()
{
    bool chestOpen = inventoryService.IsFCChestWindowOpen();
    var lines = new List<StatusLine>
    {
        new(chestOpen ? "FC chest open" : "FC chest closed", chestOpen ? Theme.Positive : Theme.Faint),
    };
    if (Plugin.ObjectTable.LocalPlayer is { } player)
        lines.Add(new StatusLine(player.Name.TextValue, Theme.Faint, FontAwesomeIcon.User));
    return lines;
}

protected override void DrawOverlays()
{
    if (Plugin.ObjectTable.LocalPlayer != null)
        DrawDialogs(); // was at the end of DrawShell
}
```

**Per-frame work.** The old `DrawShell` called `UpdatePendingChocoboBagOpen()` and `TrackMoveCompletion()` every frame while logged
in. Keep that by overriding `Draw` and then calling the base:

```csharp
public override void Draw()
{
    if (Plugin.ObjectTable.LocalPlayer != null)
    {
        UpdatePendingChocoboBagOpen();
        TrackMoveCompletion();
    }
    base.Draw(); // the kit's shell (it catches and logs draw exceptions)
}
```

**Minimize to the title bar comes free.** It's the chevron by the close button, or a double-click on the header. Where Invenwhorey opens its window from a command or the installer (`OnCommand`, `DrawMainUI`, `DrawConfigUI`), also call `InventoryWindow.Expand()` so a minimized window opens back up.

**Optional:** override `protected override float HeaderRightWidth` if a page's header actions are wide. The kit then trims a long
page title so the actions never slide under the close button.

**What the kit's shell does beyond Invenwhorey's:**
- The nav scrolls when it doesn't fit.
- A status line's tooltip only shows when the window itself is hovered.
- Card nesting state is reset every frame, so one exception can't leave all cards drawn as outlines.
- `W.Avail()` is right inside table cells.

### 7. Settings page

Replace `DrawAccentSwatches()` and the text-size segmented control in `InventoryWindow.Settings.cs` with the kit's card. Keep the
About card:

```csharp
var accent = plugin.Configuration.AccentColor;
var textScale = plugin.Configuration.TextScale;
var colorblind = plugin.Configuration.Colorblind;
if (Appearance.DrawCard(ref accent, ref textScale, ref colorblind))
{
    plugin.Configuration.AccentColor = accent;
    plugin.Configuration.TextScale = textScale;
    plugin.Configuration.Colorblind = colorblind;
    plugin.Configuration.Save();
}
```

`Appearance.DrawCard` does three things itself:
- It applies the accent and colorblind mode at once.
- It calls `Fonts.RequestScale`.
- It shows the Dalamud global-scale note.

The swatches are the seven colors, then the four grays.

Check any place where Invenwhorey itself draws good/bad **only** by color. An example is a green/red dot drawn by hand rather than
through `StatusLine`. Those should use `Theme.Positive` / `Theme.Negative`, so they follow colorblind mode.

## Check it

1. `dotnet build -c Release` should come out clean. Look for leftover references to `InventoryViewer.UI.W` / `Theme` / `Fonts`, or
   to `ImGuiHelpers.DrawCenteredModal`.
2. Check the build output. `latest.zip` should contain `LICENSE-Roboto.txt`. The dll should hold exactly two font resources:
   `phys1ksUI.Fonts.Roboto-Regular.ttf` and `phys1ksUI.Fonts.Roboto-Medium.ttf`.
3. In game, check each of these:
   - Every page, and every page's header actions.
   - Dialogs: dimmed backdrop, dialog itself not dimmed, round close button.
   - Moves and discards, with the progress bar and Cancel in the sidebar.
   - The FC chest dot.
   - Accent swatches and text sizes (the whole UI rescales).
   - Close and reopen: the window keeps its position.
4. Don't commit or push unless the user asks. When the user asks for commits: no Co-Authored-By line and no Claude footer, and stage
   files by name. The user's standing rules say the same.

## Later

- If you add or change a widget, add it in `phys1ksUI/src` (or `Widgets.Extra.cs`) and give it a line in `README.md`.
- Make the change once, in the kit; MakeShopper builds against the same source.
