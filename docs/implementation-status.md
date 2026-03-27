# Patchwork — Implementation Status

Tracks every significant feature across the codebase: what is shipped, what is
planned, and where the design documents live.  Update this file whenever a planned
item is implemented or a new plan is recorded.

---

## Shipped

### Performance — loading pipeline

| Feature | Files | Notes |
|---------|-------|-------|
| T2D deferred GPU upload | `handlers/T2DLoader.cs` | Bytes stored at startup; `Texture2D` promoted lazily at scene load or via `WarmSprites` coroutine |
| WarmSprites coroutine | `handlers/T2DLoader.cs` | Spreads GPU uploads one-per-frame after scene load; PPU lookup from `Resources.FindObjectsOfTypeAll<Sprite>()` |
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

### Bug fixes

| Fix | Files | Notes |
|-----|-------|-------|
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

### Documentation

| Document | File | Status |
|----------|------|--------|
| Conflict handling (EN) | `docs/conflict-handling.md` | Complete |
| Conflict handling (ZH) | `docs/conflict-handling-zh.md` | Complete |
| Packed bundle format | `docs/packed-bundle-format.md` | **Plan only — not implemented** |
| GUI/UX redesign | `docs/gui-ux-redesign.md` | **Plan only — not implemented** |
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

## Planned — GUI / UX Redesign

> Design document: `docs/gui-ux-redesign.md`

Collapse 8 independent windows into two audience-appropriate entry points:
Pack Manager (end users) and Dev Hub (creators).

### Build sequence

| Step | Deliverable | Status |
|------|-------------|--------|
| 1 | `gui/StatusOverlay.cs` — corner badge for end users | Not started |
| 2 | `gui/DevHub.cs` — tabbed shell, empty pillar stubs | Not started |
| 3 | `gui/pillars/PerformancePillar.cs` — absorbs DevProfiler | Not started |
| 4 | `gui/pillars/AudioPillar.cs` — absorbs AudioLog + AudioList | Not started |
| 5 | `gui/pillars/TextPillar.cs` — absorbs TextLog + DialogueEditor | Not started |
| 6 | `gui/pillars/GraphicsPillar.cs` — absorbs SkinStatus + AnimationController UI | Not started |
| 7 | `gui/pillars/VideoPillar.cs` — stub, reads VideoHandler | Not started |
| 8 | Pack Manager footer button + keybind cleanup | Not started |

**Prerequisite check before Step 6:** Confirm that `AnimationController.cs` can be
cleanly split into a data/patch layer and a view layer (its Harmony patches on
`tk2dSpriteAnimator` must remain functional when the `Draw()` surface moves).

---

## Architecture Principles

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
