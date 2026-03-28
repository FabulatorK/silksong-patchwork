# Patchwork — Implementation Status

Tracks every significant feature across the codebase: what is shipped, what is
planned, and where the design documents live.  Update this file whenever a planned
item is implemented or a new plan is recorded.

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
| AudioPillar | `gui/pillars/AudioPillar.cs`, `gui/AudioLog.cs`, `gui/AudioList.cs` | Two-pane: loaded clips (left) + live log (right); `AudioLog.DrawEntries()` + `AudioList.GetClipNames()` exposed |
| TextPillar | `gui/pillars/TextPillar.cs`, `gui/TextLog.cs`, `gui/DialogueEditor.cs` | Two-pane: accessed keys (left, clickable) + editor surface (right); click bridges to editor in-pillar |
| GraphicsPillar | `gui/pillars/GraphicsPillar.cs`, `gui/AnimationController.cs` | Sub-tabs: Overview (file scan, counts) + Animated (AnimationController content); `SkinStatus.cs` deleted |
| VideoPillar stub | `gui/pillars/VideoPillar.cs` | Placeholder until VideoHandler has content; reads `VideoHandler.VideoFileMap` |
| Pack Manager "Dev Tools →" footer | `gui/PackManagerWindow.cs` | Blue button in footer opens Dev Hub at Graphics tab |
| Keybind consolidation | `PatchworkConfig.cs`, `Plugin.cs` | Alpha1=PackManager; Alpha2–5 open DevHub at Graphics/Audio/Text/Performance; legacy shims kept one release |

### Documentation

| Document | File | Status |
|----------|------|--------|
| Conflict handling (EN) | `docs/conflict-handling.md` | Complete |
| Conflict handling (ZH) | `docs/conflict-handling-zh.md` | Complete |
| Packed bundle format | `docs/packed-bundle-format.md` | **Plan only — not implemented** |
| GUI/UX redesign | `docs/gui-ux-redesign.md` | **Implemented** — all 8 steps shipped |
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

### Legacy GUI shim cleanup  *(one release after GUI/UX redesign)*

The GUI/UX redesign kept two standalone windows as shims for one release cycle to
avoid hard breakage for users with custom keybinds.  Remove these after the next
release:

| Item | File(s) | What to remove |
|------|---------|----------------|
| DevProfiler standalone window | `gui/DevProfiler.cs`, `Plugin.cs`, `PatchworkConfig.cs` | `DevProfiler.Draw()`, `Plugin.ShowDevProfiler`, `ShowDevProfilerKey` config entry |
| AnimationController standalone window | `gui/AnimationController.cs`, `Plugin.cs`, `PatchworkConfig.cs` | `DrawAnimationController()`, `Plugin.ShowAnimationController`, `ShowAnimationControllerKey` config entry |
| Old Alpha1–7 keybind config entries | `PatchworkConfig.cs` | `ShowAudioLog`, `ShowAudioList`, `ShowAnimationController`, `ShowTextLog`, `ShowSkinStatus`, `ShowDialogueEditor` keys (all point to removed/merged windows) |

### Data layer independence violation

`AudioHandler` calls `AudioLog.LogAudio()` and `AudioList.LogAudio()`.
`DialogueHandler` calls `TextLog.LogText()` and `DialogueEditor.TrackText()`.

This violates Architecture Principle 4 (handlers must not reference GUI).  The pragmatic
fix during the GUI redesign was to keep the data-holding files as handler targets and
only extract their rendering.  Proper fix: replace direct calls with events/delegates
so handlers have zero GUI imports.

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
