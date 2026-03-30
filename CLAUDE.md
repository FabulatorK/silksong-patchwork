# Patchwork — CLAUDE.md

AI assistant reference for the Patchwork codebase. Read this before modifying any file.

---

## Project Overview

**Patchwork v2.5.0** is a BepInEx 5 plugin for Hollow Knight: Silksong that provides a
full asset-replacement framework: sprites (tk2d + Texture2D), audio, video, and text.
It also ships a developer toolset (animation inspector, audio/text logs, profiler).

- **Target runtime**: `netstandard2.1`, Unity 6000.0.50, BepInEx 5.x
- **Game engine toolkit**: 2D Toolkit (tk2d) for most in-game sprites
- **Distribution**: Thunderstore (`manifest.json`)

---

## Repository Layout

```
Patchwork.csproj          project file (single output: Patchwork.dll)
Plugin.cs                 BepInEx entry point — lifecycle, Update, OnGUI
PatchworkConfig.cs        all ConfigEntry<T> bindings (keybinds, toggles, tuning)

handlers/
  SpriteLoader.cs         tk2d atlas patching via composite RenderTextures
  SpriteDumper.cs         extract individual sprite PNGs from tk2d collections
  T2DLoader.cs            Texture2D sprite replacement (in-place + individual)
  T2DHandler.cs           Harmony postfixes routing to T2DLoader
  T2DSpritesheets.cs      partial of T2DLoader: in-place PNG swap + vanilla backup
  T2DDumper.cs            dump T2D atlases and sprite frames to disk
  SceneTraverser.cs       batch scene loader used for full-game dump
  AudioHandler.cs         audio clip replacement and hot-reload
  VideoHandler.cs         cinematic video URL override
  DialogueHandler.cs      YAML-based text/localisation override

gui/
  GUIHelper.cs            IMGUI scaling (1080p base), styles, input blocking
  StatusOverlay.cs        always-on HUD badge (packs / conflicts / sprites / clips)
  DevHub.cs               tabbed developer window (6 pillars, Alpha2–7 keybinds)
  PackManagerWindow.cs    pack list editor, profiles, conflict view, condition editor
  AnimationController.cs  frame inspector for sprite editing
  AudioLog.cs / AudioList.cs  real-time audio activity + pre-loaded clip list
  TextLog.cs / DialogueEditor.cs  text key log + override editor
  DevProfiler.cs          GC stats and frame timing
  pillars/                one file per Dev Hub tab (Dashboard, Graphics, Text,
                          Audio, Video, Performance)

packs/
  PackManager.cs          discover, order, enable/disable, hot-reload packs
  PackInfo.cs             single pack metadata + DNF condition evaluator
  PackCondition.cs        condition types (Scene, Crest, Nail, PlayerData, PackActive)
  PackStats.cs            per-pack asset file counts (cached)

util/
  TexUtil.cs              PNG load/save, RenderTexture readback, RotateMaterial shader
  T2DUtil.cs              T2D texture detection, name cleaning, sprite key building
  SpriteUtil.cs           sprite rect extraction + flip mode handling
  ConflictTracker.cs      record/query asset override conflicts
  PlayerDataCatalog.cs    reflection search over PlayerData fields/properties
  GcUtil.cs               heap pre-warming, manual GC triggers
  FileCache.cs            PNG file read cache
  PackRamCache.cs         pin pack assets into RAM
  RawKeyboardLeakBlocker.cs  prevent game input while Patchwork UI has keyboard focus
  IOUtil.cs               path helpers
  StringUtil.cs           trimming, sanitization
  IsExternalInit.cs       C# 11 record/init compatibility shim

watchers/
  SpriteFileWatcher.cs    watch Sprites/ and Spritesheets/ for hot-reload
  AudioFileWatcher.cs     watch Sounds/
  TextFileWatcher.cs      watch Text/

docs/                     design notes, changelog, feature checklist (non-code)
assetbundle/              patchwork.assetbundle — contains Rotate.shader
```

---

## Namespaces

| Namespace | Location |
|---|---|
| `Patchwork` | root (Plugin.cs) |
| `Patchwork.Handlers` | handlers/ |
| `Patchwork.GUI` | gui/ |
| `Patchwork.GUI.Pillars` | gui/pillars/ |
| `Patchwork.Packs` | packs/ |
| `Patchwork.Util` | util/ |
| `Patchwork.Watchers` | watchers/ |

---

## Core Architecture

### tk2d Sprite Replacement (`SpriteLoader.cs`)

The game uses 2D Toolkit: each `tk2dSpriteCollectionData` owns one or more `Material`s
whose `mainTexture` is a packed atlas. Patchwork composites replacement sprites directly
onto `RenderTexture` copies of those atlases.

**Key data structures** (all static, never freed):
```
_originalTextures[collection][material]    RenderTexture — vanilla backup, baked once
LoadedAtlasesTextures[collection][material] RenderTexture — custom atlas (mat.mainTexture)
LoadedAtlases[collection]                  HashSet<string> — materials processed this cycle
LoadedSprites[collection][material]        HashSet<string> — sprites drawn this cycle
_spriteFileIndex / _sheetFileIndex         case-insensitive path → (fullPath, packName)
```

**Lifecycle per `Reload()`**:
1. `RebuildFileIndex()` — re-scan all pack dirs
2. Clear `LoadedAtlases` and `LoadedSprites` (not `LoadedAtlasesTextures`)
3. `FindObjectsOfTypeAll<tk2dSpriteCollectionData>()` → `LoadCollection` each

**`LoadCollection` per material**:
1. If `mat.mainTexture` is not yet an RT → blit into new vanilla backup RT (once ever)
2. If backup RT missing → skip (will retry when `Init()` fires)
3. If material not yet processed this cycle → find custom spritesheet, blit into atlas RT
   (reuse existing RT in-place; only reallocate on dimension change)
4. Draw individual custom sprites on top using `Graphics.DrawTextureImpl`
5. `Destroy(spriteTex)` immediately after draw (prevents GPU texture leak)

**Critical invariant**: `LoadedAtlasesTextures` is **never cleared**. RTs are reused
in-place so `mat.mainTexture` always points to a live object, regardless of which
collections `FindObjectsOfTypeAll` happens to return.

**Critical invariant**: `_originalTextures` stores **`RenderTexture`**, not raw `Texture`.
Runtime RTs survive `AssetBundle.Unload(true)`; raw Texture references become fake-null
after bundle unload, causing silent transparent blits.

### Texture2D Replacement (`T2DLoader.cs` + `T2DSpritesheets.cs`)

Two modes:

| Mode | Trigger | Mechanism |
|---|---|---|
| Spritesheet override | replacement PNG matches texture name | `tex.LoadImage()` in-place |
| Individual sprite | replacement PNG matches sprite name | new `Sprite` object, set on renderers |

T2D textures are identified by format suffix in name (`-BC7-`, `DXT5|BC3-`). The
"clean name" strips the suffix and hash, e.g.:
`sactx-0-2048x2048-BC7-Hornet-abc123` → `Hornet`

Vanilla texture bytes are captured before the first swap (blit → RT → ReadPixels →
EncodeToPNG) and stored in `_originalTextureData[texName]`. Pack disable calls
`tex.LoadImage(vanillaBytes)` to restore.

Harmony patches (`T2DHandler.cs`) intercept `SpriteRenderer.sprite`, `Image.sprite`,
and `Material.mainTexture` setters to apply replacements inline.

### Audio (`AudioHandler.cs`)

Patches `AudioSource.PlayHelper` and `PlayOneShotHelper`. On first play of a clip,
checks `_soundIndex` for a replacement file. Loads via `UnityWebRequest` (sync wait).
`_originalClips` captures vanilla clip before first override for revert on disable.

### Video (`VideoHandler.cs`)

Patches `EmbeddedCinematicVideoPlayer` constructor and `CinematicPlayer.StartVideo`.
Sets `videoPlayer.url = "file:///" + localPath`. Supported formats: `.mp4 .webm .ogv
.mov .avi .m4v .mpg .mpeg .wmv`. `VideoFileMap` rebuilt on startup and pack change.

### Text (`DialogueHandler.cs`)

Patches `Language.Get(key, sheet)`. Override files at
`Text/[Sheet]/[LANG].yml` (simple `KEY: "value"` YAML). Stale key detection warns when
an override key is never requested during a session.

---

## Pack System

### Discovery

Two sources, both scanned at startup and on Rescan:
- **Thunderstore packs**: `BepInEx/plugins/*/Patchwork/` (parent dir name = pack name)
- **Local packs**: `Patchwork/Packs/*/`

### State Files (next to `Patchwork.dll`)

| File | Content |
|---|---|
| `packs.txt` | `+\|/abs/path` or `-\|/abs/path` per line |
| `packs-conditions.txt` | tab-delimited: `path\tcondition\|condition\|...` |
| `packs-stats.txt` | cached asset file counts |
| `Profiles/[Name].txt` | saved pack configurations |

### Conditions (`PackCondition.cs`)

Conditions use Disjunctive Normal Form (OR of AND groups). Types:

| Type | Meaning |
|---|---|
| `Scene` | exact scene name |
| `SceneContains` | scene name substring |
| `PackActive` | another pack is enabled |
| `CrestEquipped` | player crest ID (base name or variant) |
| `NailUpgrade` | nail level with operators (`>=`, `<=`, `>`, `<`, or exact name) |
| `PlayerData` | any `PlayerData` field/property via reflection |

Serialisation: `type|negate|join|value` (pipe-delimited, one per condition).

### Pack Changes

`PackManager.Apply(staged)` → `TriggerFullReload()` → calls each handler's `Reload()`.
Hot-reload conditions polled every ~2 s in `Plugin.Update()`.

---

## GUI

### Scaling

All sizes and positions use `GUIHelper.Scaled(n)` or `GUIHelper.ScaledRect(...)`.
Base resolution is 1080p height. Scale clamped 0.75×–2.5×. Styles (`GUIHelper.Window`,
`GUIHelper.Button`, etc.) are recalculated whenever font size changes.

### Input Blocking

`GUIHelper` tracks focused IMGUI control name. When a text field has focus, all
`InputSystem` action maps are disabled via reflection so game hotkeys don't fire
during typing. Restored on blur or plugin destroy.
`RawKeyboardLeakBlocker` handles the raw keyboard path.

### Status Overlay

Bottom-left HUD badge. Click opens Pack Manager.
Persisted across sessions via `PatchworkConfig._ShowStatusOverlay` (`ConfigEntry<bool>`).
Access via `Plugin.ShowStatusOverlay` property (get/set auto-saves to disk).

### Pack Manager

Staged editing model: user edits are applied to `_staged` list; nothing changes until
"Apply" is clicked. "Discard" reverts `_staged` to current live state.

---

## Hot Reload

File watchers run on background threads and set boolean flags (`ReloadSprites`,
`ReloadAudio`, `ReloadText`, `ReloadVideos`). `Plugin.Update()` checks these flags each
frame and calls the appropriate handler `Reload()` on the main thread.

---

## Memory Rules

These are non-negotiable. Violating them causes hard-to-reproduce VRAM leaks or
transparent/missing textures after long sessions.

1. **Never store a raw `Texture` reference long-term.** `AssetBundle.Unload(true)` can
   destroy the native object, leaving a fake-null C# reference. Always blit into a
   runtime-created `RenderTexture` for any backup that must outlive the frame.

2. **Always `Destroy(tex)` after blitting a temporary `Texture2D`.** `LoadFromPNG`
   creates a `Texture2D` with `DontUnloadUnusedAsset`. If not destroyed, every
   `Reload()` permanently leaks one GPU texture per sprite.

3. **`DontUnloadUnusedAsset` only blocks `Resources.UnloadUnusedAssets()`.** It does
   NOT protect against `AssetBundle.Unload(true)`. Use runtime RTs for long-lived data.

4. **`Object.Destroy` is safe to call immediately after submitting draw commands.**
   The GPU has finished reading by end-of-frame.

5. **Instance ID sets (`ReplacedTextureIds`, `SkippedTextureIds`) must be cleared on
   scene unload.** Unity recycles instance IDs; stale entries cause missed replacements.

6. **`LoadedAtlasesTextures` is never cleared.** Reuse RTs in-place. Destroying an RT
   that `mat.mainTexture` points to produces a one-frame transparent flash that can
   persist on scene-scoped collections not present in the next `FindObjectsOfTypeAll`.

---

## Harmony Patching Conventions

- Apply patches in handler `ApplyPatches(Harmony harmony)` called from `Plugin.Awake()`
- Prefix = intercept before game code (can skip original with `return false`)
- Postfix = run after game code (mutate `__result` or trigger side-effects)
- Never use `__instance` fields that may be null; null-check defensively
- Deferred patches (requiring game state) go in a startup coroutine in `Plugin.cs`

---

## Asset File Layout (inside a pack)

```
PackRoot/
  Sprites/[CollectionName]/[MaterialName]/[SpriteName].png   individual tk2d sprites
  Spritesheets/[CollectionName]/[MaterialName].png           full atlas override
  Sounds/[ClipName].ogg  (or .wav, .mp3, .aiff)             audio replacement
  Videos/[CinematicName].mp4  (or .webm, .ogv, etc.)        video replacement
  Text/[Sheet]/[LANG].yml                                    text overrides
```

File index keys are **normalised to forward slashes and case-insensitive**.

---

## Key Conventions

- **Sprite key format** (T2D): `cleanTexName/spriteName` — prevents collisions across
  atlas variants that share sprite names.
- **Material name abbreviation**: `mat.name.Split(' ')[0]` used for file lookup keys
  (Unity appends ` (Instance)` etc.).
- **Conflict tracking**: call `ConflictTracker.Record(type, key, winnerPack, loserPack)`
  whenever a later pack would override a key already claimed by a higher-priority pack.
- **Logging prefix convention**: `[ClassName] message` — e.g., `[SpriteLoader] Reload complete`.
- **No `FindObjectsOfType` on hot paths** (expensive). Cache results or use tracked sets.
- **No `Resources.Load` for user assets** — all user content is loaded from explicit
  file paths, never from the Unity asset database.

---

## Build

```bash
dotnet build Patchwork.csproj -c Release
```

Output: `bin/Release/netstandard2.1/Patchwork.dll`
No CI config in this repo. Deploy by copying DLL + `patchwork.assetbundle` +
`manifest.json` into `BepInEx/plugins/Patchwork/`.

---

## Recent Work (last significant commits)

| Commit | Change |
|---|---|
| `ace4d28` | `_originalTextures` → backup RenderTextures (survive `AssetBundle.Unload`) |
| `bf2f59d` | Destroy sprite Texture2D after blit (fix GPU leak → hard-restart failure) |
| `771d4f5` | `ShowStatusOverlay` backed by `ConfigEntry<bool>` (persist across sessions) |
| `cd6117f` | `VideoHandler.Reload()` + eager video scan on startup and pack change |
| `c0d2407` | T2D individual sprite revert: protect vanilla sprite with `DontUnloadUnusedAsset` |
| `6a6f28a` | tk2d atlas RT reuse in-place (fix all-objects transparency on toggle) |

---

## Open / In-Progress Design Areas

- **Conditional sprite system**: pre-bake N variant RTs per material; activate via
  pointer swap (`mat.mainTexture = variantRT`). Zero GPU cost at condition-switch time.
  Candidate map: `conditionKey → collection → material → spriteName → byte[]` with
  lazy compositing on condition change.
- **Apply/Revert as pointer swaps**: separate baking from activation so pack ON/OFF
  becomes `mat.mainTexture = customRT` / `mat.mainTexture = vanillaRT` instead of a
  full blit cycle. `_originalTextures` (vanilla RT) and `LoadedAtlasesTextures` (custom
  RT) are already both in memory — just need the control-flow split.
