# Patchwork — Implementation Status

Tracks every significant feature across the codebase: what is shipped, what is
planned, and where the design documents live.  Update this file whenever a planned
item is implemented or a new plan is recorded.

---

## State of the Mod (v2.5.0)

**Patchwork** is a BepInEx 5 plugin for Hollow Knight: Silksong that replaces
game assets at runtime — sprites, textures, audio, video, and text — without
modifying original files. It ships a pack management system (discovery,
ordering, enable/disable, conditional activation) and a full creator toolset.

### What works

| System | State | Key files |
|--------|-------|-----------|
| **tk2d sprite replacement** | Stable. Composites custom PNGs onto `RenderTexture` copies of vanilla atlases. Instance-key collision fix, `materialId`-based lookup, and `_collectionsWithFiles` gate all shipped. | `SpriteLoader.cs` |
| **Texture2D replacement** | Stable. Two modes: spritesheet (in-place `LoadImage`) and individual (new `Sprite` on renderers). Deferred GPU upload, IHDR dimension read, vanilla backup/restore. | `T2DLoader.cs`, `T2DSpritesheets.cs`, `T2DHandler.cs` |
| **Audio replacement** | Stable. Patches `PlayHelper`/`PlayOneShotHelper`. Loads via `UnityWebRequest` on first play. Full clip inventory sweep for the browser. | `AudioHandler.cs` |
| **Video replacement** | Stable. Patches `EmbeddedCinematicVideoPlayer` + `CinematicPlayer.StartVideo`. File URL override. | `VideoHandler.cs` |
| **Text/dialogue replacement** | Stable. YAML at `Text/[Sheet]/[LANG].yml`. Patches `Language.Get`. Load order: root first, then active packs by priority. | `DialogueHandler.cs` |
| **Pack system** | Stable. Thunderstore + local discovery. Enable/disable, priority ordering, profiles with conditions, per-pack asset stats. State files beside the DLL. | `PackManager.cs`, `PackInfo.cs`, `PackCondition.cs`, `PackStats.cs` |
| **Conditions** | Stable. DNF (OR-of-AND groups). Types: Scene, SceneContains, PackActive, CrestEquipped, NailUpgrade, PlayerData (reflection). Scene-transition or hot-reload trigger. | `PackCondition.cs`, `PlayerDataCatalog.cs` |
| **Hot reload** | Stable. File watchers on background threads set boolean flags; `Plugin.Update` dispatches to handler `Reload()` on main thread. Text watcher rebuilds when active pack set changes. | `SpriteFileWatcher.cs`, `AudioFileWatcher.cs`, `TextFileWatcher.cs` |
| **GUI — Pack Manager (IMGUI)** | Stable. Cassette-label visual identity, staged edits, condition editor with DNF brackets, profiles, conflicts, card borders. | `PackManagerWindow.cs` |
| **GUI — Pack Manager (Canvas)** | Built, untested in-game. Full feature parity with IMGUI version. Flag-toggled via `UseCanvasPackManager` config. Retained-mode, FabricUI-based. | `CanvasPackManager.cs`, `FabricUI.cs` |
| **GUI — Status Overlay** | Stable. Always-on bottom-left badge (packs, conflicts, sprite/clip counts). Click opens Pack Manager. Horizontal cassette strip. | `StatusOverlay.cs` |
| **GUI — Dev Hub** | Stable. Tabbed shell with 6 pillars (Dashboard, Graphics, Audio, Text, Video, Performance). Alpha2-7 keybinds. | `DevHub.cs`, `pillars/` |
| **GUI — Graphics pillar** | Stable. Sub-tabs: Animation (frame inspector + atlas preview) and T2D Textures (scene browser, edit/dump, live preview with UV highlight). | `GraphicsPillar.cs`, `AnimationController.cs`, `T2DTextureController.cs` |
| **GUI — Audio pillar** | Stable. Two-pane: searchable clip browser with virtual scroll (left) + live play log with source paths (right). | `AudioPillar.cs`, `AudioLog.cs`, `AudioList.cs` |
| **GUI — Text pillar** | Stable. Two-pane: accessed keys (left, clickable) + editor surface (right). Direct sheet/key/open row for untriggered keys. | `TextPillar.cs`, `TextLog.cs`, `DialogueEditor.cs` |
| **GUI — Video pillar** | Stable. Replacement table + live cinematic trigger log. | `VideoPillar.cs` |
| **GUI — Performance pillar** | Stable. FPS, frame timing, GC stats, reload actions. Absorbs DevProfiler. | `PerformancePillar.cs`, `DevProfiler.cs` |
| **Performance** | Stable. Deferred GPU upload, file index O(1) lookups, `FileCache`, mono heap pre-warm, scene-transition GC, setter-call gating. | Various |
| **FabricUI toolkit** | Shipped. General-purpose Canvas factory: panels, buttons, text, scroll views, input fields, layout groups, toggles, 9-slice rounded rects. | `FabricUI.cs` |

### What's in progress

| Item | State | Notes |
|------|-------|-------|
| **Canvas Pack Manager testing** | Untested | Built with full IMGUI parity; needs in-game validation before becoming the default |
| **Canvas StatusOverlay** | Planned | Migrate badge to Canvas panel with `RawImage` cassette strip |
| **Dev Hub visual polish** | Planned | Apply cassette strip; retune tab bar colours to design tokens |

### What's planned but not started

| Item | Design doc | Notes |
|------|-----------|-------|
| **Packed bundle format** (`.pwpk`) | `docs/packed-bundle-format.md` | Binary pack format for end-user distribution. v1: RGBA32 sequential. v2: GPU-native compression. v3: scene-based streaming |
| **Fabricwork rename** | `docs/ui-visual-design.md` | Fork name. Backward-compatible discovery of both `Fabricwork/` and `Patchwork/` paths |
| **Pack encryption** | `docs/DESIGN_NOTES.md` | Password-protected packs + LSB watermarking. Raises bar against casual redistribution |
| **Conditional sprite system** | `CLAUDE.md` | Pre-bake N variant RTs per material; condition switch = pointer swap, zero GPU cost |
| **Apply/Revert pointer swaps** | `CLAUDE.md` | `_originalTextures` and `LoadedAtlasesTextures` both in memory; pack toggle should be pointer swap, not blit |
| **Material colour tint tooling** | `docs/DESIGN_NOTES.md` | Expose per-material shader properties in AnimationController so creators can pre-compensate for TC's colour corrections |

### Architecture invariants

These are non-negotiable and apply to all future work:

1. **Self-containment** — no external native plugins, no NuGet packages, no Unity Editor requirement
2. **Dual-path transparency** — dev mode and packed mode must produce identical visual results
3. **No coverage regression** — every migration step must leave all existing functionality reachable
4. **Data layer independence** — handlers never import or call GUI; GUI reads from handlers via events
5. **Audience separation** — end-user surfaces (Pack Manager, StatusOverlay) usable without creator tooling
6. **Memory rules** — never store raw `Texture` long-term; `Destroy(tex)` after every temporary blit; `LoadedAtlasesTextures` never cleared; clear instance ID sets on scene unload

---

## Shipped

### Performance — loading pipeline

| Feature | Files | Notes |
|---------|-------|-------|
| T2D deferred GPU upload | `handlers/T2DLoader.cs` | Bytes stored at startup; `Texture2D` promoted lazily on first scene encounter (one-frame vanilla flash accepted trade-off; `WarmSprites` removed) |
| PNG IHDR dimension read | `handlers/T2DSpritesheets.cs` | Reads width/height from 24-byte header; no GPU upload for metadata |
| Sprite file index | `handlers/SpriteLoader.cs` | `RebuildFileIndex()` builds two `Dictionary<string,(FullPath,Pack)>` at reload time; `FindSprite`/`FindSpritesheet` are O(1) |
| FileCache | `util/FileCache.cs` | Timestamp-fingerprinted byte cache; `ReadBytes` checks `GetLastWriteTimeUtc` before re-reading |
| Audio reverse index | `handlers/AudioHandler.cs` | `RebuildSoundIndex()` builds `Dictionary<string,string>` (filename → path); `HasAudioReplacements` gates all sweeps |

### Performance — runtime

| Feature | Files | Notes |
|---------|-------|-------|
| Mono heap pre-warm | `util/GcUtil.cs`, `Plugin.cs` | Allocates and releases a configurable block at startup to raise GC high-water mark before gameplay |
| Scene-transition GC collect | `util/GcUtil.cs`, `Plugin.cs` | `GC.Collect(Optimized)` on `sceneUnloaded`; lands during loading screen |
| Main menu stutter fix | `handlers/T2DHandler.cs` | `PerfTickFrame()` (which allocates a format string + calls `LogWarning` when >20 setter calls/frame) now returns immediately unless `ShowDevProfiler` is true |
| `CheckForUninitializedSprites` moved to scene load | `Plugin.cs` | Removes recurring `FindObjectsByType` sweep every 30 frames (~0.5 s); single call on `sceneLoaded` is sufficient since Harmony setter postfixes track mid-scene spawns immediately |

### Bug fixes

| Fix | Files | Notes |
|-----|-------|-------|
| T2D individual sprite priority broken by revert sweep | `handlers/T2DLoader.cs` | Revert sweep now sets `_enforcing = true` and looks up `_loadedSprites` first — prevents Harmony setter chain (`OnSpriteSet` → `TrySwapTexture` + `HandleLoad`) from re-entering during the sweep and corrupting priority |
| T2D revert block skipped with no active pack | `handlers/T2DLoader.cs` | Removed `!HasT2DReplacements` gate; base-path T2D files kept `_preloadedBytes` non-empty even with no pack, causing the entire revert sweep to be skipped |
| T2D spritesheet and audio revert cascade on pack disable | `handlers/T2DLoader.cs`, `handlers/T2DSpritesheets.cs`, `handlers/AudioHandler.cs` | `TryRestoreTexture` now guarded with `preSkipped` check; `_originalClips` now saved before `source.clip` assignment to avoid Harmony postfix guard blocking the save |
| T2D Inventory/UI sprites black after pack disable | `handlers/T2DLoader.cs`, `handlers/T2DSpritesheets.cs` | Added revert sweep (vanilla lookup by name) before old sprites destroyed; `PruneStaleOriginals` now preserves entries covered by active `SpritesheetOverrides` |
| `_confirmedSpriteNames` accumulation across scenes | `handlers/T2DLoader.cs` | Cleared in `PruneSceneState` (on `sceneUnloaded`) — was previously only cleared by `ReloadSpritesInScene`, so names grew unboundedly during normal play |
| `_originalTextureData` not freed on pack disable | `handlers/T2DSpritesheets.cs`, `handlers/T2DLoader.cs` | After the restore pass in `ReloadSpritesInScene`, `_originalTextureData` is cleared immediately when `SpritesheetOverrides` is empty — `PruneStaleOriginals` skips live textures so without this the bytes lingered until next scene unload |
| `AudioHandler` `ContainsKey`+indexer race | `handlers/AudioHandler.cs` | Both `LoadAudio` overloads now use `TryGetValue`; `_soundIndex` population uses `TryAdd` |
| `FindSprite` double enumeration | `handlers/T2DLoader.cs` | `Any()`+`First()` replaced with single `FirstOrDefault()`; warns if multiple T2D files match the same sprite name in a pack |
| T2D effects stuck after pack reload | `handlers/T2DLoader.cs` | `ReloadSpritesInScene` now clears `_knownRenderers`, `_knownImages`, `_confirmedSpriteNames`, `_trackedSpriteNames` before rebuild — stale tracking from the old pack can no longer interfere |
| `Object` ambiguous reference | `handlers/SpriteLoader.cs` | Qualified as `UnityEngine.Object.Destroy` after `using System` was added |
| Missing `using System.IO` | `util/TexUtil.cs` | Required for `Path.Combine` |
| Missing `using System` | `handlers/SpriteLoader.cs`, `packs/PackCondition.cs` | `StringComparer`, `StringComparison`, `Globalization` types |
| `GUIHelper.BoxStyle` undefined | `gui/PackManagerWindow.cs` | Replaced with `UnityEngine.GUI.skin.box` |
| Main menu FPS drop from T2D perf logger | `handlers/T2DHandler.cs` | See stutter fix above |

### Conditions system

| Feature | Files | Notes |
|---------|-------|-------|
| NailUpgrade condition | `packs/PackCondition.cs`, `gui/PackManagerWindow.cs` | Reads `PlayerData.nailUpgrades` (int 0–4); supports `>=/<=/>/</==` prefix and named levels (`old`/`sharpened`/`channelled`/`coiled`/`pure`) |
| PlayerData generic condition | `packs/PackCondition.cs`, `util/PlayerDataCatalog.cs`, `gui/PackManagerWindow.cs` | Reflection catalog of all `bool/int/float/string` fields and properties on `PlayerData`; expression format `fieldName` (bool) or `fieldName >= target`; search UI with scrollable filtered list |

### GUI — condition editor

| Feature | Files | Notes |
|---------|-------|-------|
| Far-left AND-group bracket | `gui/PackManagerWindow.cs` | Bracket moved from between the two join columns to the outermost left of the group, wrapping both columns and content |
| Ghost "add condition" row | `gui/PackManagerWindow.cs` | Replaces the `+ Add condition` button with a faded condition-shaped row; green-tinted `+ scene` chip is the only interactive element |

### GUI — Dev Hub unified window

| Feature | Files | Notes |
|---------|-------|-------|
| StatusOverlay corner badge | `gui/StatusOverlay.cs`, `Plugin.cs` | Always-on HUD; shows active packs, conflicts, sprite/clip counts; clicking opens Pack Manager |
| DevHub tabbed shell | `gui/DevHub.cs`, `Plugin.cs`, `PatchworkConfig.cs` | Single tabbed window; Alpha2-5 open at Graphics/Audio/Text/Performance tabs; `ShowDevHub` / `DevHubTab` in Plugin |
| PerformancePillar | `gui/pillars/PerformancePillar.cs`, `gui/DevProfiler.cs` | Absorbs DevProfiler content; `DevProfiler.DrawPillarContent()` exposed; DevProfiler.Draw() kept as transition shim |
| AudioPillar | `gui/pillars/AudioPillar.cs`, `gui/AudioLog.cs`, `gui/AudioList.cs` | Two-pane: clip browser with virtual scroll + search (left) + live play log with source path (right); clipboard copy on click; log-to-browser focus |
| TextPillar | `gui/pillars/TextPillar.cs`, `gui/TextLog.cs`, `gui/DialogueEditor.cs` | Two-pane: accessed keys (left, clickable) + editor surface (right); click bridges to editor in-pillar |
| GraphicsPillar | `gui/pillars/GraphicsPillar.cs`, `gui/AnimationController.cs`, `gui/T2DTextureController.cs` | Sub-tabs: Animation (frame inspector + atlas preview) + T2D Textures (scene browser, edit/dump workflow, live preview) |
| VideoPillar | `gui/pillars/VideoPillar.cs`, `handlers/VideoHandler.cs` | Replacement table + live cinematic trigger log; `VideoHandler.OnCinematicTriggered` event fires `(name, hasReplacement)` from Harmony postfix |
| Pack Manager "Dev Tools →" footer | `gui/PackManagerWindow.cs` | Blue button in footer opens Dev Hub at Graphics tab |
| Keybind consolidation | `PatchworkConfig.cs`, `Plugin.cs` | Alpha1=PackManager; Alpha2–5 open DevHub at Graphics/Audio/Text/Performance; legacy shims kept one release |

### T2D Texture Browser

| Feature | Files | Notes |
|---------|-------|-------|
| T2DSceneEntry snapshot struct | `util/T2DUtil.cs` | Immutable per-texture record: cleanName, rawName, NativeTexture, W×H, isT2D, spriteNames, override flags |
| `T2DLoader.GetSceneTextureEntries()` | `handlers/T2DLoader.cs` | Walks `_knownRenderers` + `_knownImages`, groups by texture ID, annotates with spritesheet/individual override counts; sorted T2D-first |
| `T2DLoader.FindSceneSprite()` | `handlers/T2DLoader.cs` | Returns first live Sprite matching (cleanTexName, spriteName) — used for UV highlight in preview |
| T2DTextureController | `gui/T2DTextureController.cs` | Two-pane browser: searchable atlas list (left) + live preview + sprite list + edit/dump buttons (right); 2 s refresh cooldown |
| Atlas preview + UV highlight | `gui/T2DTextureController.cs` | `GUI.DrawTexture` on NativeTexture; yellow overlay on selected sprite's textureRect (Y-flipped for IMGUI) |
| Edit Atlas | `gui/T2DTextureController.cs` | Dumps atlas PNG if needed → copies to `Sprites/T2D/{name}/{name}.png` → `Process.Start` |
| Edit Sprite | `gui/T2DTextureController.cs` | Dumps individual sprite if needed → copies to `Sprites/T2D/{atlas}/{sprite}.png` → `Process.Start` |
| AnimationController frame preview | `gui/AnimationController.cs` | Atlas thumbnail below Edit buttons for selected animator; yellow highlight on current frame's UV quad |
| GraphicsPillar sub-tabs | `gui/pillars/GraphicsPillar.cs` | Animation tab (existing) + T2D Textures tab (new) |
| T2DDumper standalone filter fix | `handlers/T2DDumper.cs` | Collects all tk2d material texture IDs before standalone sweep — excludes atlas textures even when their names don't match the `-BC7-` pattern |
| T2DDumper duplicate fix | `handlers/T2DDumper.cs` | `dumpedSpriteKeys` HashSet prevents writing the same T2D sprite file multiple times per `DumpAllT2DSprites()` call |

### Audio browser

| Feature | Files | Notes |
|---------|-------|-------|
| Full clip inventory sweep | `handlers/AudioHandler.cs` | `GetClipInventory()` uses `Resources.FindObjectsOfTypeAll<AudioClip/AudioSource>()` — finds clips that never fire a play event; mirrors T2D approach |
| `AudioClipEntry` model | `handlers/AudioHandler.cs` | Immutable snapshot: `ClipName`, `LengthSeconds`, `Channels`, `Frequency`, `HasReplacement`, `SourcePaths` |
| `IsAudioBrowserActive` gate | `handlers/AudioHandler.cs` | Set true by `AudioPillar.Draw()`, reset false each `OnGUI` pass; sweep is zero-cost when tab is closed |
| Virtual scroll in clip browser | `gui/pillars/AudioPillar.cs` | Only renders visible rows (IMGUI scroll views do not virtualise natively); fixes 200fps→40fps regression with 200+ clips |
| Cached filter list | `gui/pillars/AudioPillar.cs`, `gui/AudioList.cs` | `_filteredEntries` rebuilt only when `_searchFilter` or `AudioList.Version` changes, not every frame |
| Log source path + focus | `gui/AudioLog.cs` | Each play entry stores `GameObjectPath`; clicking focuses and scrolls the browser to that clip |
| `GetGameObjectPath` deduplicated | `handlers/AudioHandler.cs` | Single `internal static` implementation; `AudioLog` delegates to it |

### tk2d sprite loader — correctness

| Feature | Files | Notes |
|---------|-------|-------|
| Instance key collision fix | `handlers/SpriteLoader.cs` | `InstanceKey(coll) = name + "\x00" + instanceID`; all runtime dicts keyed by ikey, not collection name |
| Vanilla texture name disambiguation | `handlers/SpriteLoader.cs` | `_vanillaTexNames[ikey+"\x00"+matname]` captured before first replacement; used as path discriminator for same-named collections |
| `_collectionsWithFiles` gate | `handlers/SpriteLoader.cs` | Built from first segment of every file index key; skips atlas RT swap entirely for collections with zero replacement files |
| `def.materialId` fix | `handlers/SpriteLoader.cs`, `handlers/SpriteDumper.cs` | Replaces `def.material == mat` reference equality with `def.materialId == matIndex` — prevents sprite-batch misses when Unity creates material instances |
| `ResolveKey()` helper | `handlers/T2DLoader.cs` | Centralises `_spriteNameToKey` fallback used by `CheckSprite` and `TryGetReplacement`; fixes uninit sweep missing atlas-qualified keys |

### GUI — visual identity and Pack Manager layout

| Feature | Files | Notes |
|---------|-------|-------|
| Cassette-label colour palette | `gui/GUIHelper.cs` | 13 design tokens: `ColAccent` (orange), `ColConfirm` (teal-green), `ColDanger` (magenta-red), `ColBone` (mask off-white), `ColPop1`/`ColPop2` (specular green/blue). Strip colours (`StripRed`, `StripOrange`, `StripGreen`, `StripBlue`, `StripBone`) at 40/10/5/5/40 band ratios |
| Vertical cassette strip | `gui/GUIHelper.cs`, `gui/PackManagerWindow.cs` | `DrawCassetteStripVertical()` — 14px strip on Pack Manager left edge; content indented past it |
| Horizontal cassette strip | `gui/GUIHelper.cs`, `gui/StatusOverlay.cs` | `DrawCassetteStripHorizontal()` — 2px strip along StatusOverlay bottom edge with alpha fade over final 40% |
| Card border system | `gui/GUIHelper.cs` | `DrawBorder()` — thin rect outline; used for pack row enabled/condition indicators |
| Pack Manager layout overhaul | `gui/PackManagerWindow.cs` | Top bar: Rescan + ×. Pack rows: ✓ square toggle, ⚙ gear for conditions, full card border (teal=enabled, orange=has conditions). Action bar: status count + Discard/Apply at bottom. Footer: Status Overlay banner (70%) + Dev Tools ⚒ square |
| StatusOverlay retune | `gui/StatusOverlay.cs` | Pack count colour → teal `#33AA88`; background from `ColSurface` token; horizontal cassette strip replaces old blue accent stripe |
| UI visual design doc | `docs/ui-visual-design.md` | Living design document for visual direction and future UI work |
| Procedural strip textures | `gui/GUIHelper.cs` | `GenerateStripTexture()` — rounded caps, AA diagonal band transitions, depth gradient. Cached, regenerated on resolution change. Single `DrawTexture` call replaces dozens of IMGUI rects |
| Borderless window style | `gui/GUIHelper.cs` | `BorderlessWindowStyle` — zero-padding flat dark background; Pack Manager draws its own chrome |
| FabricUI Canvas toolkit | `gui/FabricUI.cs` | General-purpose Canvas element factory (630 lines). Panels, buttons, text, scroll views, input fields, layout groups, toggles, separators, rounded-rect 9-slice sprites. Resolution-independent. Hover via `ColorTint` with 0.08s fade |

### GUI — Canvas migration (IN PROGRESS)

| Item | Files | Status |
|------|-------|--------|
| FabricUI toolkit | `gui/FabricUI.cs` | **Shipped** |
| Canvas Pack Manager | `gui/CanvasPackManager.cs` | **Shipped** — full feature parity with IMGUI PackManagerWindow: pack list with toggle/arrows/gear, condition editor (DNF clauses with AND-group brackets, type cycling, crest/nail pickers, free-text input), profiles (load/save/delete), conflicts foldout, action bar (Discard/Apply with staging), footer (Status Overlay toggle + Dev Tools), cassette strip via RawImage, draggable panel, rounded-rect background |
| Plugin.cs integration | `Plugin.cs`, `HotkeyController.cs`, `PatchworkConfig.cs`, `gui/StatusOverlay.cs` | **Shipped** — `UseCanvasPackManager` config flag; keybind routes to Canvas or IMGUI; StatusOverlay click routes correctly; cursor handling covers Canvas PM |
| Canvas StatusOverlay | `gui/StatusOverlay.cs` | **Planned** |

### GUI — deprecated

| Item | Notes |
|------|-------|
| `PackRamCache` (Pin to RAM) | Removed — duplicated bytes already held by `FileCache`. Saved only a `stat()` syscall per file. `util/PackRamCache.cs` deleted; all references removed from `FileCache`, `PackManager`, `GcUtil`, `ReloadCoordinator`, `PackManagerWindow` |

### GUI — tooltip + polish

| Feature | Files | Notes |
|---------|-------|-------|
| IMGUI tooltip system | `gui/GUIHelper.cs` | `TT(label, tooltip)` wraps `GUIContent`; `DrawTooltip()` renders semi-transparent box near cursor; `BeginOnGUI` clears `GUI.tooltip` at Repaint start to prevent stale Layout-pass phantom tooltips |
| T2D browser sprite filter | `gui/T2DTextureController.cs` | Inline search field in the sprite list header; clears on atlas selection change |
| `T2DLog` atlas-level dedup | `gui/T2DLog.cs` | One log entry per atlas; sprite name updated within 2 s cooldown before re-bumping to top — prevents animated backgrounds flooding the log |
| Cursor auto-show | `Plugin.cs` | `LateUpdate` forces `Cursor.visible = true` / `CursorLockMode.None` when DevHub or PackManager is open |

### Pack system — profiles and persistence

| Feature | Files | Notes |
|---------|-------|-------|
| Profile conditions | `packs/PackManager.cs` | `SaveProfile` appends trigger + serialised conditions tab-separated; `StageProfile` restores them; backward compatible with old profiles |
| Window position persistence | `PatchworkConfig.cs`, `gui/DevHub.cs`, `gui/PackManagerWindow.cs`, `Plugin.cs` | `DevHubX/Y`, `DevHubTab`, `PackManagerX/Y` ConfigEntries; loaded on first draw, written in `OnDestroy` |

### Technical debt resolved

| Item | Files changed | Notes |
|------|---------------|-------|
| Legacy standalone window shim removal | `gui/DevProfiler.cs`, `gui/AnimationController.cs`, `gui/AudioLog.cs`, `gui/AudioList.cs`, `gui/TextLog.cs`, `Plugin.cs` | Removed `Draw()`/`DrawAnimationController()`/`DrawAudioLog()`/`DrawAudioList()`/`DrawTextLog()` and their window-only fields; pillar APIs unchanged |
| Data layer independence | `handlers/AudioHandler.cs`, `handlers/DialogueHandler.cs`, `Plugin.cs` | Replaced direct GUI calls with `OnAudioPlayed`, `OnTextAccessed` events; `OnAudioSourceLoaded` removed (superseded by `GetClipInventory`); subscribers wired in `Plugin.Awake()` |

### Documentation

| Document | File | Status |
|----------|------|--------|
| Conflict handling (EN) | `docs/conflict-handling.md` | Complete |
| Conflict handling (ZH) | `docs/conflict-handling-zh.md` | Complete |
| Packed bundle format | `docs/packed-bundle-format.md` | **Plan only — not implemented** |
| GUI/UX redesign | `docs/gui-ux-redesign.md` | **Implemented** — all 8 steps shipped |
| UI visual design | `docs/ui-visual-design.md` | **Living document** — cassette palette, layout principles, future direction |
| This file | `docs/implementation-status.md` | Living document |

---

## Planned — Packed Bundle Format  (`.pwpk`)

> Design document: `docs/packed-bundle-format.md`

A dual-path loading strategy: dev mode (current hot-reload pipeline) for asset
creators; packed mode (pre-processed binary bundle) for end users distributing
finished packs.

### v1 — RGBA32, sequential load

| Item | Owner file(s) | Status |
|------|---------------|--------|
| `.pwpk` binary format (header + entry table + key block + data block) | `util/PackedBundleLoader.cs` | Not started |
| `PackedBundleLoader` — index reader, `LoadAll()`, `GetSprite(key)` | `util/PackedBundleLoader.cs` | Not started |
| `PackBuilder` — in-game packer (PNG decode → RGBA32 → write) | `util/PackBuilder.cs` | Not started |
| Pack Manager "Build Pack" button | `gui/PackManagerWindow.cs` | Not started |
| `SpriteLoader` packed path | `handlers/SpriteLoader.cs` | Not started |
| `T2DLoader` packed path | `handlers/T2DLoader.cs` | Not started |
| `AudioHandler` packed path | `handlers/AudioHandler.cs` | Not started |
| `Plugin.cs` — skip file watchers for packed packs | `Plugin.cs` | Not started |
| Manifest sidecar (`pack.pwpk.json`) | `util/PackBuilder.cs` | Not started |

### v2 — GPU-native compression

| Item | Status |
|------|--------|
| `Texture2D.Compress()` in packer (Unity runtime, no external deps) | Not started |
| `pixel_format` byte records actual `TextureFormat` value post-compress | Not started |
| Optional `DeflateStream` wrapper on data block | Not started |

### v3 — Streaming / partial load

| Item | Status |
|------|--------|
| Scene manifest pre-computed at pack time | Not started |
| Load only assets referenced in the current scene | Not started |

---

## Technical Debt

### ~~Legacy GUI shim cleanup~~  *(resolved)*

All standalone window shim methods and their window-only fields have been removed:
`DevProfiler.Draw()`, `AnimationController.DrawAnimationController()`,
`AudioLog.DrawAudioLog()`, `AudioList.DrawAudioList()`, `TextLog.DrawTextLog()`,
`Plugin.ShowDialogueEditor`.  Each file retains only its pillar-facing API
(`DrawPillarContent`, `DrawEntries`, `GetClipNames`, `LogAudio`, etc.).

### ~~Data layer independence violation~~  *(resolved)*

`AudioHandler` and `DialogueHandler` no longer import or call GUI classes directly.
Both now fire static events; `Plugin.Awake()` wires the GUI subscribers:

| Event | Subscribers wired in Plugin.Awake() |
|-------|-------------------------------------|
| `AudioHandler.OnAudioPlayed` | `AudioLog.LogAudio` |
| `AudioHandler.OnAudioSourceLoaded` | `AudioList.LogAudio` |
| `DialogueHandler.OnTextAccessed` | `TextLog.LogText`; `DialogueEditor.TrackText` (gated on `ShowDevHub`) |

---



These apply to all future work and should be respected when implementing any planned
item above:

1. **Self-containment** — no external native plugins, no NuGet packages, no Unity
   Editor requirement.  All encoding, decoding, compression, and checksumming use
   the Unity runtime API or the .NET BCL only.

2. **Dual-path transparency** — dev mode and packed mode must produce identical
   visual results.  The packer runs in-game after the creator has verified the pack
   in dev mode, guaranteeing pixel-perfect agreement.

3. **No coverage regression** — every migration step (GUI pillars, packed paths)
   must leave all existing functionality reachable.  Nothing is removed until its
   replacement is verified working.

4. **Data layer independence** — handlers (`SpriteLoader`, `AudioHandler`,
   `T2DLoader`, `DialogueHandler`, `VideoHandler`) must remain free of GUI
   references.  GUI pillars read from handlers; handlers never call GUI.

5. **Audience separation** — end-user surfaces (Pack Manager, Status Overlay) must
   remain usable without understanding any creator tooling.  Dev Hub is invisible
   unless explicitly opened.
