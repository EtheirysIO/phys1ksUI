# phys1ksUI — maintainer notes for Claude

phys1ksUI is the shared UI kit (theme, fonts, widgets, window shell) for phys1ks's Dalamud plugins. `README.md` is the
human guide (setup and design rules). This file is for whoever changes the kit: how it's wired, the rules a change must
follow, the API map, and a changelog. **Keep this file true.** Any change to the kit's API or behavior updates the API
map and adds a changelog line in the same commit.

## How it's shared

- **Source, not a DLL.** `phys1ksUI.props` compiles `src/**/*.cs` into each plugin (namespace `phys1ksUI`), embeds
  `Fonts/Roboto-{Regular,Medium}.ttf` as `phys1ksUI.Fonts.Roboto-*.ttf` (exact names; `Fonts.cs` opens them by name)
  and copies `LICENSE-Roboto.txt` next to the plugin dll. Importing the props twice is harmless (items exclude
  themselves).
- **Everything is `internal`** except `AccentColor`, which a plugin's public `Configuration` stores.
- **Consumers** (all import the props by relative path, so the kit must stay at `N:\FFXIV\phys1ksUI`):
  - Invenwhorey: `N:\FFXIV\Invenwhorey\Invenwhorey.csproj`
  - MakeShopper: `N:\FFXIV\MakeShopper\MakeShopper\MakeShopper.csproj`
  - PuppetMaster: `N:\FFXIV\PuppetMaster\Source\PuppetMaster\PuppetMaster.csproj`
- **One copy, fixed once.** A plugin never forks or patches kit code locally. A missing or wrong widget is fixed here,
  and every plugin gets it on its next build.
- **Who edits.** One session at a time. If you're working in a plugin repo and need a kit change, ask the session that
  owns the kit (or the user) rather than editing `src/` from two places at once.

## Making a change

1. **Look before you cut.** Before renaming, removing or changing a signature, grep both consumers:
   `grep -rn "W.Thing\|Theme.Thing" N:/FFXIV/Invenwhorey N:/FFXIV/MakeShopper N:/FFXIV/PuppetMaster/Source/PuppetMaster --include=*.cs`. Adding optional
   parameters at the end is safe; reordering or removing isn't. Anything consumers call is listed under
   "Consumers depend on" below.
2. **Build every consumer.** Each must come out 0 errors, 0 warnings:
   ```
   dotnet build N:/FFXIV/Invenwhorey/Invenwhorey.csproj -c Debug
   dotnet build N:/FFXIV/MakeShopper/MakeShopper/MakeShopper.csproj -c Debug
   dotnet build N:/FFXIV/PuppetMaster/Source/PuppetMaster/PuppetMaster.csproj -c Debug
   ```
3. **Update the docs:** this file's API map and changelog, and `README.md`'s "Widgets at a glance" if a widget changed.
4. **Commit** in `N:\FFXIV\phys1ksUI` (git, branch `main`), with the user's rules:
   - Stage files by name; never `git add -A` or `git add .`.
   - No `Co-Authored-By` line and no Claude footer.
   - Only commit when the user asks.
5. **Tell the user** which plugins need a rebuild, and anything they must change. Compile-clean isn't tested: behavior
   changes need an in-game check.

## Files

| File | What's in it |
| --- | --- |
| `src/Kit.cs` | `AccentColor`, `Kit.Initialize` / `Kit.Dispose`, the services (`Kit.Log`, `Kit.Textures`, `Kit.Data`), `Kit.ReducedMotion`, and the per-frame hook. |
| `src/Theme.cs` | Palette, accent and colorblind state, `S()` scaling, `Radius`, `Space`, color math, `Theme.Push()`, `StyleScope`. |
| `src/Fonts.cs` | The type scale as one `FontSet` of Dalamud font handles (Roboto plus the game's Axis glyphs; FontAwesome), text-size presets, and rebuilds. |
| `src/Motion.cs` | Eased values by ImGui id (swept when stale), tweens, reveal, pulse, easing curves. Everything honors "reduce motion". |
| `src/Widgets.cs` | `W` part 1: buttons, toggle, checkbox, segmented control, card, text, pills, status dot, inputs, item icons, shared drawing helpers. |
| `src/Widgets.Extra.cs` | `W` part 2: fold card, compact button, banner, chips, divider, links, stat, tables, combo, nav rows. |
| `src/Widgets.Forms.cs` | `W` part 3: text area, number input, list row. |
| `src/Modal.cs` | `Modal.Draw` (themed modal over a hand-painted scrim), `Modal.Confirm`, `Modal.Close`, and the shared modal-dim bookkeeping. |
| `src/KitWindow.cs` | The window shell base class: sidebar, header strip, body, minimize to a title bar, per-area error handling. Also `HeaderSlot`, `RunningOperation`, `StatusLine`. |
| `src/Recovery.cs` | `KitRecovery`: puts ImGui's stacks back after a draw throws (see "Error recovery"). |
| `src/Appearance.cs` | `Appearance.DrawCard`: the standard accent, text-size and colorblind settings card. |

## Frame lifecycle

1. **`Kit.BeginFrame`**, on `UiBuilder.Draw`, which `Kit.Initialize` subscribes before the plugin's own handler. It
   runs for every window, KitWindow or not:
   1. `Fonts.ApplyPendingScale()` builds a requested text size. It builds the new `FontSet` first, then swaps it in and
      disposes the old one; if the build fails, the old size stays.
   2. `W.ResetFrame()` zeroes card and table nesting.
   3. `Modal.BeginFrame()` puts ImGui's modal dim back if none of our modals drew last frame.
2. **`KitWindow.PreDraw`**:
   1. Applies the plugin's `Accent` and `Colorblind`.
   2. Eases the minimize animation (`ApplyCollapse` sets `Flags`, `Size` and `SizeConstraints` in unscaled units;
      Dalamud applies the global scale).
   3. `Theme.Push()`, then zero window padding.
3. **`KitWindow.Draw`**:
   1. `W.ResetFrame()`, then push the Body font and popup padding.
   2. Sidebar: brand, a scrolling nav child, the status block.
   3. Header strip: title, `DrawHeaderRight(slot)`, minimize and close, then the drag area, submitted last so the
      widgets on top of it win the hover.
   4. Body child, with the page reveal.
   5. `DrawOverlays()`, at full strength even while folding.
4. **`KitWindow.PostDraw`** pops the padding, then the theme.

Text-size changes: `Appearance.DrawCard` calls `Fonts.RequestScale`, and the next frame's hook applies it. Fonts are never
rebuilt while one is pushed.

## Error recovery (read before writing any scope or `try`)

C# disposes the `using` scopes between a throw and its `catch` **before** the catch runs. If a page throws with a
child window or a raw `BeginTable` open, a card's `Dispose` would otherwise pop and merge in the wrong place. That
corrupts the draw-list splitter or underflows the popup stack.

**The model:**
- **Catch sites.** Every kit catch site is `catch (Exception ex) when (KitRecovery.Catch())`, then
  `KitRecovery.RecoverTo(windowIdCapturedBeforeTheTry)`. The sites are `KitWindow.Draw`, the body, and the nav. The
  filter runs before any inner disposal and sets `KitRecovery.Unwinding`.
- **Scopes while unwinding.** They pop no ImGui state. They only fix the kit's own counters (`cardDepth`,
  `cardRightPad`, `tableDepth`). Anything ImGui's recovery won't restore goes to `KitRecovery.Defer(windowId, undo)`:
  indent, item width, a card's channel merge, a fold card's clip rect.
- **`RecoverTo`** ends child windows, popups and tooltips down to the target, calling ImGui's
  `ErrorCheckEndWindowRecover` in each. That ends tables, tab bars, trees, groups and disabled blocks, and pops IDs,
  style colors and vars, and item flags back to where each window began. It then runs the deferred undos whose window
  is current, and clears `Unwinding`.
- **Outside a kit catch site** (a plugin's own try/catch), scopes fall back to comparing `W.CurrentWindowId()` with the
  window they started in, and skip their pops when it differs.

**Rules for new code:**
- A new disposable scope checks `KitRecovery.Unwinding` first. While it's set, update kit counters, `Defer` what ImGui
  won't undo, and return. `Theme.StyleScope` already skips its pops, so compose with it.
- A `finally` that calls an ImGui End* must skip while `Unwinding` (see `Modal.Draw`), unless it runs after its own
  catch site has already recovered (the body and nav `EndChild`s).
- A new catch site uses the filter form, and captures the window id before the `try`.
- Fonts: always `using (Fonts.X.Push())`. Recovery doesn't pop fonts.

Not yet exercised in game: throw from a page on purpose (inside a card, a raw table, a combo, a fold card while it
animates) and confirm there's no assert and the error banner shows.

## Conventions for widgets

- **Sizes.** Design pixels go through `Theme.S(px)` (global scale × text scale). Font sizes don't: the atlas scales
  them. Radii and spacing come already scaled from `Theme.Radius.*` and `Theme.Space.*`.
- **Colors.**
  - Use tokens only (`Theme.*`). Tints are `Theme.Wash(a)`, shades `Theme.Shadow(a)`, and alpha changes
    `Theme.Fade(c, mul)`.
  - Good and bad are `Theme.Positive` / `Theme.Negative`. Status visuals call `Theme.ColorblindShape(color)` so
    colorblind mode can add a check or cross.
  - Draw-list colors always go through `Theme.U32`, which multiplies in the style alpha, so `BeginDisabled`, fades and
    reveals dim custom drawing too.
- **Interactive widgets:**
  - Use `InvisibleItem(id, size, enabled)` for the hit box, then `Motion.Hover(ImGui.GetID(id), hovered)`, then draw,
    then `HoverFeedback(hovered, tooltip)`.
  - Disabled widgets draw at `DisabledAlpha` (0.4) via `Theme.Fade`, and return false.
- **Labels** follow `"Visible##id"`: `W.Visible(label)` is the text (cached), and the whole label is the ID. Loops use
  `ImGui.PushID(i)`, never `$"##x{i}"`.
- **Widths:**
  - Fill widths use `W.Avail()`, which takes off enclosing card padding and is correct inside table cells, never raw
    `GetContentRegionAvail().X`.
  - Inputs use `FillWidth(width, min)`: greater than 0 is exact, and 0 or less fills (plus width).
- **No per-frame garbage.** Cache strings (`Glyph`, `Visible`, `Upper` via `Cached`), keep scratch in `stackalloc`
  spans, and measure with spans (`CalcTextSize(text.AsSpan(..))`). No LINQ, closures or interpolation in draw paths.
- **Motion.**
  - Call `Motion.Approach` / `Hover` once per key per frame, since each call advances the ease.
  - Keys are ImGui IDs, and neighbouring keys like `key + 1` are fine.
  - Tween durations of 0 or less count as done.
- **Draw-list helpers** (`DrawGlyphCentered`, `DrawGlyphAt`, `TrackedCaps`, `DrawChip`, `DrawPill`,
  `StatusDot(dl, …)`, `Hairline`, `DrawPanel`) expect the **current window's** draw list. Pushing a font only sets the
  texture on that list.
- **Text.** User text is drawn with `ImGui.Text*` / `AddText`. Dalamud's bindings send these to `TextUnformatted`, so
  `%` in item names is safe.

## Known quirks, kept for compatibility

Consumers rely on these. Don't "fix" one without migrating both plugins in the same change.

- **Width 0 means different things.** It means auto for buttons (`size`) and `Segmented(width: 0)`, but fill for
  `SearchBox`, `TextInput` and `Combo`. `Segmented` with any negative width just fills.
- **Units differ.**
  - `FixedColumn(width)` and `KitWindow`'s `minimumSize` take design pixels.
  - `RoundButton`, `IconButton`, `ItemIcon`, input widths, `Divider(space)`, `TableHeaders(rowHeight)`,
    `DrawPill(pad)` and `HeaderRightWidth` take scaled pixels.
- **Parameter order differs:** it's `Toggle(label, ref v, enabled, tooltip)` but
  `Checkbox(id, ref v, mixed, height, tooltip, enabled)`.
- **`Modal.Draw` takes an `Action`,** so callers allocate a closure per frame while the modal is open.
- **`enabled` and `tooltip` are uneven:**
  - `RoundButton` has no `enabled`.
  - `Segmented`, the inputs, `Combo`, `Banner`, `Chip`, `Pill`, `Stat` and `NavRow` have no tooltip.
- **`Banner` and `HelpMark` IDs** come from their text unless you pass `id:`.

## Backlog (not built yet)

These are what the plugins still hand-roll with raw ImGui:
- a slider
- a popup or context-menu scope that uses `NewSurface`
- modal footer buttons (a right-aligned button row)
- a progress bar that sits in normal layout
- `Banner` presets for info, warning and error
- a submit-on-Enter `TextInput`
- a pulsing pill background
- a `ThemedWindow` base for non-shell windows

Also, the props path is relative (`..\phys1ksUI`), so an official-repo or CI build would need the kit vendored as a
submodule.

## API map

`W` is `internal static partial class W` (`Widgets.cs` + `Widgets.Extra.cs` + `Widgets.Forms.cs`). Signatures show defaults; `px` means
scaled pixels unless it says design px.

**Kit and window**
- `Kit`:
  - `Initialize(pluginInterface, log, textures, data, textScale = 1, accent = Orange, colorblind = false)`.
  - `Dispose()`: call it last, after removing your Draw handler and `RemoveAllWindows()`.
  - `Log`, `Textures`, `Data`, `ReducedMotion`.
- `KitWindow(windowName, brand, brandIcon, minimumSize /* design px @100% */)`:
  - Required overrides: `Accent`, `Colorblind`, `PageTitle`, `DrawSidebarNav()`, `DrawBody()`.
  - Optional overrides: `PageKey`, `DrawBodyTop()`, `DrawHeaderRight(HeaderSlot)`, `HeaderRightWidth`,
    `GetRunningOperation()`, `GetStatusLines()`, `DrawOverlays()`, `ExtraFlags`.
  - Public members: `Version`, `Compact`, `ToggleCompact()`, `Expand()`.
- Records:
  - `HeaderSlot(Min, Max)`, with `Width`, `Height` and `CenterY`.
  - `RunningOperation(Label, Detail?, Fraction?, Cancel?)`.
  - `StatusLine(Text, Color, Icon?, Tooltip?)`.
- `Modal.Draw(title, ref open, Action body, flags = AlwaysAutoResize)`. A body that closes itself calls
  `Modal.Close(ref open)` (clears the flag and closes the popup).
- `Modal.Confirm(title, ref open, message, confirmLabel, danger = false, detail?, cancelLabel = "Cancel")`: a yes / no
  dialog (confirm button first, Danger when `danger`). True on the frame confirm is clicked; either choice clears `open`.
- `Appearance.DrawCard(ref accent, ref textScale, ref colorblind)` returns true when something changed; save your
  config then.
- `KitRecovery`: `Catch()`, `Unwinding`, `Defer(windowId, undo)`, `RecoverTo(windowId)`. Kit-internal.

**Theme**
- Palette:
  - Surfaces: `Ground`, `Panel`, `Field`, `Raised`, `Raised2`.
  - Rules and borders: `RuleHair`, `RuleStrong`, `BorderControl`, `CardBorder`.
  - Text: `Ink`, `Dim`, `Faint`, `Slate`, `Link`.
  - Status: `Negative`, `OnNegative`, `Warning`, `HQGold`, `Transparent`, and `Positive`, which turns blue in
    colorblind mode.
- Alpha: `Wash(a)`, `Shadow(a)`, `Fade(c, mul)`.
- Accent:
  - `Accent`, `AccentHover`, `AccentPressed`, `AccentAlpha(a)`, `OnAccent`.
  - `AccentChoices`, `IsGray`, `AccentValue`, `AccentName`, `SetAccent`.
- Colorblind: `Colorblind`, `SetColorblind`, `ColorblindShape(color)`, `SameRgb`.
- Scale: `S(px)`, `S(x, y)`, `Radius.{Window, Card, Control, Small, Chip, Pill}`,
  `Space.{Tight, Gap, Gutter, CardPad, ButtonHeight, RowHeight, SidebarWidth, HeaderHeight, NavRowHeight, BodyPad}`.
- Color math: `Hex`, `U32`, `Lerp`, `Lighten`, `Darken`.
- Styling: `Push()`, and `StyleScope` with `.Color()`, `.Var()` and `Dispose()`.

**Fonts**
- Handles: `Display` (24), `Title` (18), `Label` (13), `Body` (16), `Small` (14), `Icon` (16, FontAwesome), in design
  px. The `*Px` constants hold these.
- `ScalePresets` (0.9, 1, 1.15, 1.3, 1.5), `Scale`.
- `Initialize`, `RequestScale`, `ApplyPendingScale`, `Dispose`.

**Motion**
- `Approach(key, target, speed = 18, snap)`, `Hover(key, hovered)`.
- `Reveal(tick, ms)`, `Tween` / `TweenOut(from, to, tick, fullMs)`, `Pulse()`.
- Easing: `EaseSmooth`, `EaseInOutCubic`, `EaseOutCubic`.
- `StaleAfterFrames`, `IsStale`.

**W: buttons**
- `PrimaryButton`, `SecondaryButton`, `DangerButton`, `GhostButton(label, Vector2? size, enabled = true, tooltip)`:
  - size.X > 0 is exact, 0 is auto, less than 0 fills plus X.
  - size.Y of 0 is `Space.ButtonHeight`.
- `IconTextButton(icon, label, ButtonKind, size, enabled, tooltip)`, `ButtonWidth(label, icon?)`.
- `IconButton(icon, id, tooltip, danger, enabled, size = frame height)`.
- `RoundButton(id, screenPos, size, icon, tooltip, danger)`.
- `CompactButton(label, accent = false, enabled, tooltip)`.

**W: choices**
- `Toggle(label, ref value, enabled, tooltip)`, `ToggleWidth(label)`.
- `Checkbox(id, ref value, mixed = false, height = 0, tooltip, enabled)`.
- `Segmented(id, string[] options, ref index, width = -1)`, `SegmentedWidth(options)`.

**W: inputs**
- `SearchBox(id, ref text, hint, width, maxLength = 512, error = false)`.
- `TextInput(id, ref text, hint, width, maxLength = 256, error = false, flags)`. `maxLength` is UTF-8 bytes, clamped
  to 1..65536.
- `Combo(id, preview, width, height = 0)` returns a `ComboScope` with `.Open`. Fill it with
  `ComboItem(label, selected)`.
- `Combo(id, IReadOnlyList<string> items, ref index, width, height = 0)`.
- `TextArea(id, ref text, width, height = 0 /* 4 lines */, hint?, maxLength = 2048, error = false, flags)`: multi-line,
  with a hint drawn while empty. The input stays the last item.
- `NumberInput(id, ref int value, min, max, step = 1, width = 0, suffix?)`: InputInt with steppers, clamped; true when
  the value changed.

**W: surfaces**
- `Card(id, title?, rightNote?)` returns an `IDisposable`. Nested cards draw as an outline.
- `FoldCard(id, title, rightNote, out open, defaultOpen = true, noteColor?)`.
- `Avail()`, `NewSurface()`, `Spacer()`, `Divider(space = 0)`, `RightAlign(width)`.
- Draw-list helpers: `Hairline(dl, min, max)`, `DrawPanel(dl, min, max)`.

**W: text**
- `Heading(text)`, `TextWrapped(text, color?)`, `Link(text, tooltip)` (true when clicked).
- `Stat(value, caption, color?)`.
- `TrackedCaps(dl, pos, text, color, tracking = 0.12)`, `TrackedCapsWidth`.
- `Fit(text, maxWidth)`, `Visible(label)`.
- `Tooltip(text)`, `HelpMark(text, id?)`.

**W: tags and status**
- `Chip(text, color?, status = false)`, `ChipWidth`, `DrawChip(dl, pos, …)`.
- `Pill(text, bg, fg, status = false)`, `PillWidth(text, fg?, status)`, `DrawPill(dl, pos, text, bg, fg, status, pad?)`.
- The `Draw*` forms place the left edge on the vertical center and return their width.
- `StatusDot(color, pulse)`, `StatusDot(dl, center, color, pulse, radius = 0)`.
- `Banner(text, color, dismissable = false, icon?, id?)` returns true when dismissed.
- `ProgressBar(dl, min, width, height, fraction)`.

**W: tables**
- `Table(id, columns, flags = TableFlags, outerSize = default)` returns a `TableScope` with `.Open`.
- `FixedColumn(name, width /* design px */, flags, userId)`.
- `TableHeaders(trackedCaps = false, rowHeight = 0, labels?)`.
- `TableHeadersWithCheckAll(ticked, total, …same)` returns a `bool?`.

**W: nav and icons**
- `NavRow(id, icon, label, active, subtitle?, enabled = true)`.
- `ListRow(id, label, selected, subtitle?, dot?, dotPulse = false, trailing?, tooltip?, enabled = true)`: a full-width
  selectable row for lists inside a page; accent wash and left bar when selected. True when clicked.
- `ItemIcon(itemId, hq, size)`: HQ is +1,000,000, collectable is +500,000, and event items are 2,000,000 and up.
- `Glyph(icon)`, `DrawGlyphCentered(dl, icon, min, size, color)`, `DrawGlyphAt(dl, icon, center, px, color)`.

**Consumers depend on** (keep these signatures, or migrate the plugins in the same change):
- Everything in the README's "Widgets at a glance".
- In particular, from Invenwhorey:
  - `Checkbox`
  - `DrawChip`, `DrawPill`, `ChipWidth`
  - `TableHeaders(trackedCaps, rowHeight, labels)`
  - `Combo`, `Divider`, `TextInput`
  - `SearchBox(error)`, `Banner(icon)`
  - positioned `StatusDot`
  - `Theme.Wash`, `TrackedCaps`
  - `Theme.Space.RowHeight`
  - `KitWindow.Expand()`

## Changelog

Newest first. One line per change, naming anything consumers must do.

- 2026-10-01: PuppetMaster joins as a consumer. New: `W.TextArea`, `W.NumberInput`, `W.ListRow` (`Widgets.Forms.cs`),
  `Modal.Confirm`, `Modal.Close`. Additions only; nothing for the other plugins to change.

- 2026-09-30:
  - Review pass:
    - Error recovery now uses an exception filter and deferred undos (`Recovery.cs`).
    - Fill widths honor card padding.
    - `KitWindow.Colorblind` is **abstract** (plugins must override it; both do).
    - Kit's frame hook applies text size and card reset for every window.
    - Fonts rebuild atomically and are looked up by exact resource name.
    - `ItemIcon` handles event items.
    - `TableHeadersWithCheckAll` has per-column IDs.
    - `Banner` / `HelpMark` take `id:`.
    - `Fit` is surrogate-safe.
    - New helpers: `HoverFeedback`, `TrailingLabel`, `Theme.Fade`, `Gap`, `Hairline`, `DrawPanel`,
      `Motion.EaseSmooth`.
    - Removed: `W.PageTitle`, `Fonts.IconLarge`, `DrawGlyphCentered(large:)`, `Theme.CurrentAccent`, `Theme.Ease`.
    - The props file is safe to import twice.
    - The kit is now a git repo.
  - The fc-chest session added `Checkbox`, `Combo`, `TextInput`, `Divider`, `DrawChip` / `DrawPill` / `ChipWidth`,
    colorblind status glyphs, table header options, `SearchBox(error)`, `Banner(icon)`, positioned `StatusDot`,
    `Theme.Wash` / `Shadow`, and the first `KitRecovery`.
- 2026-09-29: The kit is extracted from Invenwhorey. MakeShopper and Invenwhorey both import it.
