# phys1ksUI

The shared look for phys1ks's Dalamud plugins. It started as Invenwhorey's design and is compiled as source into each
plugin, so every plugin gets the same window, fonts and widgets without shipping another DLL. Invenwhorey and MakeShopper
both run on it (`MIGRATE-INVENWHOREY.md` is the finished migration's notes, kept for history).

## Use it in a plugin

1. Import it in the plugin's csproj (the path is relative to the csproj):

   ```xml
   <Import Project="..\..\phys1ksUI\phys1ksUI.props" />
   ```

2. Save these in the plugin's configuration:
   - `AccentColor Accent = AccentColor.Orange`
   - `float TextScale = 1f`
   - `bool Colorblind`

   `AccentColor` is the kit's only public type, on purpose: the plugin's public `Configuration` class stores it, and
   everything else in the kit is `internal`.
3. In the plugin's constructor, before any window is added:

   ```csharp
   Kit.Initialize(PluginInterface, Log, TextureProvider, DataManager, config.TextScale, config.Accent, config.Colorblind);
   ```

   Call `Kit.Dispose()` last in the plugin's Dispose: after removing the plugin's own `UiBuilder.Draw` handler and
   `WindowSystem.RemoveAllWindows()`, so no window draws with the fonts it releases.
4. Derive the main window from `KitWindow`:
   - `Accent`, `PageTitle`, `DrawSidebarNav()` (use `W.NavRow`) and `DrawBody()` are required.
   - Optional:
     - `DrawHeaderRight(slot)` and `HeaderRightWidth` (page actions in the header; measure the width in the body font).
     - `DrawBodyTop()` (a page's own tab bar) and `PageKey` (what counts as a new page).
     - `GetRunningOperation()`, `GetStatusLines()` and `DrawOverlays()` (modals: `Modal.Draw`).
     - `ExtraFlags` (window flags: the shell sets `Flags` itself every frame).
5. Put `Appearance.DrawCard(ref accent, ref textScale, ref colorblind)` on the Settings page. Copy the three values in and out
   of your config, and save the config when it returns true.
6. Override `Colorblind` in the window (`protected override bool Colorblind => config.Colorblind;`).

## Files

| File | What's in it |
| --- | --- |
| `src/Theme.cs` | Colors, accents, washes and shadows, `S()` scaling, radii, spacing, `Theme.Push()`, color math, the colorblind shape. |
| `src/Fonts.cs` | Roboto at exact pixel sizes: Display, Title, Body, Label, Small, with the game's glyphs merged in. Also the text-size presets. |
| `src/Widgets.cs`, `src/Widgets.Extra.cs` | `W`: buttons, checkbox, toggle, segmented control, cards, chips and pills, banners, inputs, combo, dividers, tables, nav rows, tooltips, icons. |
| `src/Motion.cs` | Hover fades, eased values, tweens (in-out and out), reveals and pulses; all of it respects "reduce motion". |
| `src/Modal.cs` | `Modal.Draw`: themed modal dialogs over a hand-painted scrim. |
| `src/KitWindow.cs` | The window shell: sidebar with brand, nav and status block; header strip; body. It also minimizes to a title bar (the chevron or a double-click on the header) that shows what's running, with Cancel. Call `Expand()` when a command opens a page. Also `KitRecovery` (see "When a draw throws"). |
| `src/Appearance.cs` | The standard accent and text-size settings card. |
| `src/Kit.cs` | `AccentColor` and `Kit.Initialize` / `Kit.Dispose`. |

## Design rules

- **Dark, flat surfaces.** Panel `#141416` for the sidebar, header and popups; Field `#1c1c1e` for cards and inputs.
  Separate things with hairlines (`RuleHair`, `W.Divider()`), not boxes.
- **One accent, iOS Orange `#ff9f0a` by default.** Use it for the active nav row, primary buttons, toggles, checkboxes and
  progress. It is never used for warnings: Warning is yellow `#ffd60a`, Negative is `#ff453a` and Positive is `#30d158`.
- **Accent choices:**
  - Colors: Orange, Purple, Indigo, Blue, Teal, Green, Pink.
  - Grays: Graphite `#636366`, Gray `#8e8e93`, Silver `#c7c7cc`, White `#f2f2f7`.

  Put text and glyphs on an accent fill in `Theme.OnAccent`, which turns near-black on Silver and White. On a red fill use
  `Theme.OnNegative`.
- **Washes and shadows are tokens too.** White tints over dark surfaces are `Theme.Wash(alpha)` (0.04 row hover, 0.06-0.07
  neutral pill fill, 0.12 is `CardBorder`); drop shadows and scrims are `Theme.Shadow(alpha)`. No hand-written
  `new Vector4(1, 1, 1, a)`.
- **Colorblind mode** is for red/green color blindness:
  - `Theme.Positive` turns blue `#409cff`.
  - Status dots (`W.StatusDot`, both overloads, and the sidebar's status lines) get a check or a cross.
  - Chips and pills that mean good/bad pass `status: true`, and then carry the same check or cross.

  `Theme.ColorblindShape(color)` decides (by RGB, so a faded Positive still counts). Never show good/bad by color alone
  somewhere the user can't also read it. Always use `Theme.Positive` / `Theme.Negative`, never hard-coded green or red.
- **Text colors:**
  - Ink for content.
  - Dim for secondary text.
  - Faint for hints.
  - Slate tracked caps for card titles and table headers.
  - Link `#64d2ff` for clickable text.
- **Everything scales.** Give sizes in design pixels through `Theme.S(px)`, which follows both Dalamud's global scale and the
  text-size setting. Never hard-code pixels.
- **Pages are cards.** Use `W.Card("id", "Title", "note")`, or `W.FoldCard` for long optional parts.
- **Buttons:**
  - One Primary action per card.
  - Secondary for the rest.
  - Ghost for outlined accent actions.
  - `CompactButton` inside table rows.
- **Motion is quiet and quick.**
  - Hover highlights fade in and out (`Motion.Hover`).
  - The active nav row fades between rows.
  - A new page fades in with a 12 px upward slide over 260 ms. `KitWindow.PageKey` decides what counts as a new page. Draw a page's own tab bar in `DrawBodyTop()`, not `DrawBody()`, so the tabs stay still while the page changes.
  - Fold cards open and close over 220 ms; minimize eases over 280 ms.
  - Everything snaps instead when Dalamud's "reduce motion" setting is on (and pulsing dots hold still). Animate through
    `Motion` so this stays true. A duration of zero or less counts as already finished.
- **Depth, lightly.** Cards get a soft drop shadow and a faint highlight along the top edge. The background stays plain:
  no decorative shapes behind the window.
- **No per-frame garbage in the kit.** Glyph strings, upper-cased captions and header ids are cached; widget scratch lives
  on the stack. Keep new widgets that way (ids via `ImGui.PushID(i)`, not `$"##x{i}"`).
- **Wording:** keep labels short and plain, and put the details in tooltips (`W.Tooltip`, `W.HelpMark`).

## Widgets at a glance

Sizes are scaled pixels unless a parameter says "design px". Labels follow ImGui's `"Visible##id"` convention, and every
widget dims and ignores clicks inside `ImGui.BeginDisabled()`.

| Group | API |
| --- | --- |
| Buttons | `PrimaryButton`, `SecondaryButton`, `DangerButton`, `GhostButton`, `IconTextButton(icon, label, kind)`, `ButtonWidth`, `IconButton`, `RoundButton`, `CompactButton` |
| Choices | `Toggle(label, ref value)`, `ToggleWidth(label)`, `Checkbox(id, ref value, mixed = false, height = 0, tooltip, enabled)`, `Segmented(id, options, ref index, width)`, `SegmentedWidth` |
| Inputs | `SearchBox(id, ref text, hint, width, maxLength = 512, error = false)`, `TextInput(id, ref text, hint, width, maxLength = 256, error = false, flags = None)`, `Combo(id, preview, width, height = 0)` → `ComboScope` (`.Open`) with `ComboItem(label, selected)`, `Combo(id, items, ref index, width, height = 0)` |
| Surfaces | `Card`, `FoldCard`, `Avail()`, `NewSurface()`, `Spacer()`, `Divider(space = 0)`, `RightAlign(width)` |
| Text | `Heading`, `PageTitle`, `TextWrapped`, `Link`, `Stat`, `TrackedCaps(dl, pos, text, color)`, `TrackedCapsWidth`, `Fit(text, maxWidth)`, `Visible(label)` |
| Tags | `Chip(text, color, status = false)`, `ChipWidth`, `DrawChip(dl, pos, text, color, status = false)`; `Pill(text, bg, fg, status = false)`, `PillWidth`, `DrawPill(dl, pos, text, bg, fg, status = false, pad = null)`. The `Draw*` forms paint at a position (left edge, vertical center) without a layout item and return their width. |
| Status | `StatusDot(color, pulse)`, `StatusDot(dl, center, color, pulse, radius = 0)`, `Banner(text, color, dismissable = false, icon = null)`, `ProgressBar(dl, min, width, height, fraction)`, `Tooltip`, `HelpMark` |
| Tables | `Table(id, columns, flags, outerSize = default)` → `TableScope` (`.Open`); `FixedColumn(name, width, flags = None, userId = 0)`; `TableHeaders(trackedCaps = false, rowHeight = 0, labels = null)`; `TableHeadersWithCheckAll(ticked, total, ...same options)`. With tracked caps, sortable columns keep click-to-sort and the arrow. |
| Sidebar | `NavRow(id, icon, label, active, subtitle, enabled)` |
| Icons | `ItemIcon(itemId, hq, size)`, `Glyph(icon)` (cached glyph string), `DrawGlyphCentered`, `DrawGlyphAt(dl, icon, center, px, color)`, `DrawIconLabelCentered` |
| Theme | `Theme.Wash(alpha)`, `Theme.Shadow(alpha)`, `Theme.ColorblindShape(color)`, `Theme.SameRgb(a, b)` |
| Motion | `Approach`, `Hover`, `Reveal`, `Tween` (in-out), `TweenOut` (out), `Pulse`, `IsStale` |

## When a draw throws

`KitWindow` catches exceptions from the page body, the sidebar nav and the rest of the shell. `KitRecovery.RecoverTo`
then ends whatever the throwing code left open (child windows, popups, tables, tab bars, groups, IDs, disabled blocks,
style colors and vars) back to the window that was drawing, using ImGui's own `ErrorCheckEndWindowRecover`, so the frame
still ends cleanly. A throwing page shows an error banner in place of its rest; the header, sidebar and dialogs keep
working. Each distinct error is logged once, and again if it comes back after a clean frame. `Modal.Draw` recovers the
same way inside its dialog. The kit's own scopes (`Card`, `FoldCard`, `Table`) notice they are being unwound in the
wrong window and leave the pops to the recovery. Fonts pushed without a `using` aren't recovered: push fonts with `using`.

## Licenses

Roboto is © Google, under the Apache License 2.0 (`Fonts/LICENSE-Roboto.txt`, copied next to the plugin dll).
