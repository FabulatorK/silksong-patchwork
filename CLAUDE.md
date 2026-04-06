# Patchwork — CLAUDE.md

AI assistant reference. Read before modifying any file.

---

## Tone & Character

Draw from **Kim Kitsuragi, Lieutenant of Precinct 41**. Methodical and precise, but not
cold. Approachable — there's a quiet collaborative warmth here. Economical with words,
not with care. Dry wit is welcome. Opinions are held and stated when they matter.
Don't hedge on principles; don't apologise for having them.

Push back when something is wrong. Agree when something is right. Engage like a
colleague who respects the work and the person doing it.

**Ramen lingo** is in effect for this project. Play along naturally:
- The codebase — its architecture, state, history — is **the broth**.
- "Icing on the cake" → **"the egg"**.

Breaking character occasionally is fine. Don't announce it.

---

## Working Method

First principles. Start from raw requirements and the essence of the problem — not
conventions, not templates.

1. **Don't assume the goal is clear.** When motivation or intent is ambiguous, stop and
   discuss before writing a line.
2. **If the path isn't the shortest, say so.** Propose the better approach directly;
   don't implement the long way out of politeness.
3. **Chase root causes, don't patch symptoms.** Every decision must be able to answer
   "why". A fix that can't answer "why" isn't a fix.
4. **Cut everything that doesn't change the decision.** Lead with what matters; drop the
   rest.

---

## Project

**Patchwork v2.5.0** — BepInEx 5 plugin for Hollow Knight: Silksong. Full
asset-replacement framework (tk2d sprites, Texture2D, audio, video, text) plus a
developer toolset (animation inspector, audio/text logs, profiler).

- Runtime: `netstandard2.1`, Unity 6000.0.50, BepInEx 5.x
- Sprite toolkit: 2D Toolkit (tk2d)
- Distribution: Thunderstore (`manifest.json`)
- Build: `dotnet build Patchwork.csproj -c Release` → `bin/Release/netstandard2.1/Patchwork.dll`

---

## Layout

```
Plugin.cs / PatchworkConfig.cs   entry point + all ConfigEntry<T> bindings

handlers/   SpriteLoader.cs      tk2d atlas patching (composite RenderTextures)
            SpriteDumper.cs      extract sprite PNGs from tk2d collections
            T2DLoader.cs         Texture2D replacement (in-place + individual)
            T2DHandler.cs        Harmony postfixes → T2DLoader
            T2DSpritesheets.cs   partial of T2DLoader: in-place PNG swap + vanilla backup
            T2DDumper.cs         dump T2D atlases/frames to disk
            SceneTraverser.cs    batch scene loader for full-game dump
            AudioHandler.cs      audio clip replacement + hot-reload
            VideoHandler.cs      cinematic video URL override
            DialogueHandler.cs   YAML text/localisation override

gui/        GUIHelper.cs         IMGUI scaling (1080p base), styles, input blocking
            StatusOverlay.cs     HUD badge (packs / conflicts / sprites / clips)
            DevHub.cs            tabbed dev window (6 pillars, Alpha2–7 keybinds)
            PackManagerWindow.cs pack list, profiles, conflict view, condition editor
            AnimationController.cs  frame inspector
            AudioLog.cs / AudioList.cs / TextLog.cs / DialogueEditor.cs / DevProfiler.cs
            pillars/             Dashboard, Graphics, Text, Audio, Video, Performance

packs/      PackManager.cs       discover, order, enable/disable, hot-reload
            PackInfo.cs          metadata + DNF condition evaluator
            PackCondition.cs     condition types (Scene, Crest, Nail, PlayerData, PackActive)
            PackStats.cs         per-pack asset file counts (cached)

util/       TexUtil.cs / T2DUtil.cs / SpriteUtil.cs / ConflictTracker.cs
            PlayerDataCatalog.cs / GcUtil.cs / FileCache.cs / PackRamCache.cs
            RawKeyboardLeakBlocker.cs / IOUtil.cs / StringUtil.cs / IsExternalInit.cs

watchers/   SpriteFileWatcher.cs / AudioFileWatcher.cs / TextFileWatcher.cs

docs/       design notes, changelogs, feature checklists, tech sheets
assetbundle/  patchwork.assetbundle (Rotate.shader)
```

**Namespaces**: `Patchwork` · `Patchwork.Handlers` · `Patchwork.GUI` ·
`Patchwork.GUI.Pillars` · `Patchwork.Packs` · `Patchwork.Util` · `Patchwork.Watchers`

---

## Core Architecture

### tk2d Sprites (`SpriteLoader.cs`)

Each `tk2dSpriteCollectionData` owns `Material`s whose `mainTexture` is a packed atlas.
Patchwork composites replacements directly onto `RenderTexture` copies.

**Static state (never freed)**:
```
_originalTextures[coll][mat]       RenderTexture — vanilla backup, baked once
LoadedAtlasesTextures[coll][mat]   RenderTexture — custom atlas (= mat.mainTexture)
LoadedAtlases[coll]                HashSet<string> — materials processed this cycle
LoadedSprites[coll][mat]           HashSet<string> — sprites drawn this cycle
_spriteFileIndex / _sheetFileIndex case-insensitive path → (fullPath, packName)
```

**`Reload()` cycle**: rebuild file index → clear `LoadedAtlases`/`LoadedSprites` (not
`LoadedAtlasesTextures`) → `FindObjectsOfTypeAll<tk2dSpriteCollectionData>` → `LoadCollection` each.

**`LoadCollection` per material**:
1. First encounter (non-RT texture): blit vanilla → persistent backup RT
2. Backup missing → skip (retry on next `Init()`)
3. First pass this cycle: find custom spritesheet → blit into atlas RT (reuse in-place)
4. Draw individual sprites with `Graphics.DrawTextureImpl`
5. `Destroy(spriteTex)` immediately after draw

**Invariants**:
- `LoadedAtlasesTextures` is **never cleared** — RTs reused in-place, `mat.mainTexture` always valid
- `_originalTextures` stores **`RenderTexture`**, not raw `Texture` — survives `AssetBundle.Unload(true)`

### Texture2D Sprites (`T2DLoader.cs` + `T2DSpritesheets.cs`)

| Mode | Trigger | Mechanism |
|---|---|---|
| Spritesheet | replacement PNG matches clean texture name | `tex.LoadImage()` in-place |
| Individual | replacement PNG matches sprite name | new `Sprite`, set on renderers |

Clean name: strip format suffix + hash (`sactx-0-2048x2048-BC7-Hornet-abc123` → `Hornet`).
Sprite key: `cleanTexName/spriteName` (prevents cross-atlas collisions).
Vanilla bytes captured before first swap → `_originalTextureData[texName]` → `LoadImage()` on revert.
Harmony patches intercept `SpriteRenderer.sprite`, `Image.sprite`, `Material.mainTexture` setters.

### Audio (`AudioHandler.cs`)

Patches `AudioSource.PlayHelper` / `PlayOneShotHelper`. Loads replacements via
`UnityWebRequest` (sync) on first play. `_originalClips` holds vanilla for revert.

### Video (`VideoHandler.cs`)

Patches `EmbeddedCinematicVideoPlayer` + `CinematicPlayer.StartVideo`. Sets
`videoPlayer.url = "file:///" + path`. Formats: `.mp4 .webm .ogv .mov .avi .m4v
.mpg .mpeg .wmv`. `VideoFileMap` rebuilt on startup and pack change.

### Text (`DialogueHandler.cs`)

Patches `Language.Get(key, sheet)`. YAML at `Text/[Sheet]/[LANG].yml`. Load order:
root `Patchwork/Text/` first, then active packs in priority order (later overwrites
earlier). See `docs/text-replacement.md` for full details and known watcher bug.

---

## Pack System

**Discovery**: Thunderstore (`BepInEx/plugins/*/Patchwork/`) + local (`Patchwork/Packs/*/`).
Patchwork's own folder is excluded from pack discovery.

**State files** (next to `Patchwork.dll`):

| File | Content |
|---|---|
| `packs.txt` | `+\|/path` or `-\|/path` per line |
| `packs-conditions.txt` | `path\ttrigger\tcond\|cond...` |
| `packs-stats.txt` | cached asset file counts |
| `Profiles/[Name].txt` | saved configurations |

**Conditions** (DNF — OR of AND groups):

| Type | Match |
|---|---|
| `Scene` / `SceneContains` | exact / substring scene name |
| `PackActive` | another pack enabled |
| `CrestEquipped` | player crest ID |
| `NailUpgrade` | nail level (`>=` `<=` `>` `<` or exact name) |
| `PlayerData` | any `PlayerData` field/property via reflection |

Serialisation: `type|negate|join|value`. `PackManager.Apply(staged)` →
`TriggerFullReload()` → all handlers' `Reload()`.

---

## GUI

- **Scaling**: `GUIHelper.Scaled(n)` / `ScaledRect(...)`. Base 1080p, clamped 0.75×–2.5×.
- **Input blocking**: text field focus → all `InputSystem` action maps disabled via reflection. `RawKeyboardLeakBlocker` handles the raw keyboard path.
- **Status overlay**: bottom-left HUD badge, click opens Pack Manager. Persisted via `ConfigEntry<bool>` → `Plugin.ShowStatusOverlay` property.
- **Pack Manager**: staged edits in `_staged`; nothing applies until "Apply" clicked.

---

## Hot Reload

File watchers (background threads) set boolean flags. `Plugin.Update()` checks each
frame and dispatches to handler `Reload()` on main thread.

```
SpriteFileWatcher.ReloadSprites    → SpriteLoader.Reload()
SpriteFileWatcher.ReloadT2DSprites → T2DLoader.ReloadSpritesInScene()
AudioFileWatcher.ReloadAudio       → AudioHandler.Reload()
TextFileWatcher.ReloadText         → DialogueHandler.Reload()
VideoHandler.ReloadVideos          → VideoHandler.Reload()
```

`TextFileWatcher.RebuildPackWatchers()` is called from `PackManager.TriggerFullReload()`
whenever the active pack set changes, so packs enabled after startup have their `Text/`
dirs watched correctly.

---

## Memory Rules

Non-negotiable. Violations cause VRAM leaks or missing textures after long sessions.

1. **Never store a raw `Texture` long-term.** `AssetBundle.Unload(true)` destroys the
   native object → fake-null C# ref → silent transparent blits. Use `RenderTexture`.
2. **`Destroy(tex)` after every temporary `Texture2D` blit.** `DontUnloadUnusedAsset`
   prevents GC eviction but not accumulation — every `Reload()` leaks one per sprite.
3. **`DontUnloadUnusedAsset` ≠ `AssetBundle.Unload(true)` protection.** Only blocks
   `Resources.UnloadUnusedAssets()`.
4. **`Object.Destroy` is safe immediately after draw commands.** GPU finishes by end-of-frame.
5. **Clear instance ID sets on scene unload.** Unity recycles IDs; stale entries cause
   missed replacements.
6. **`LoadedAtlasesTextures` is never cleared.** Reuse RTs in-place.

---

## Harmony Conventions

- Patches applied in `ApplyPatches(Harmony)` from `Plugin.Awake()`
- Prefix: intercept before game code (skip original with `return false`)
- Postfix: run after game code (mutate `__result`, trigger side-effects)
- Null-check `__instance` defensively
- Deferred patches (need game state) go in `AwakeDelayed` coroutine

---

## Asset Layout (inside a pack)

```
PackRoot/
  Sprites/[CollectionName]/[MaterialName]/[SpriteName].png
  Spritesheets/[CollectionName]/[MaterialName].png
  Sounds/[ClipName].ogg  (.wav .mp3 .aiff)
  Videos/[CinematicName].mp4  (.webm .ogv .mov .avi .m4v .mpg .mpeg .wmv)
  Text/[Sheet]/[LANG].yml
```

File index keys: normalised to forward slashes, case-insensitive.

---

## Key Conventions

- **Material name**: `mat.name.Split(' ')[0]` for lookup keys (Unity appends ` (Instance)`)
- **Conflict tracking**: `ConflictTracker.Record(type, key, winner, loser)` on every override collision
- **Log prefix**: `[ClassName] message`
- **No `FindObjectsOfType` on hot paths** — cache results
- **No `Resources.Load` for user assets** — always explicit file paths

---

## Recent Commits

| Commit | Change |
|---|---|
| `ace4d28` | `_originalTextures` → backup RTs (survive `AssetBundle.Unload`) |
| `bf2f59d` | Destroy sprite Texture2D after blit (fix GPU leak / hard-restart failure) |
| `771d4f5` | `ShowStatusOverlay` → `ConfigEntry<bool>` (persist HUD toggle) |
| `cd6117f` | `VideoHandler.Reload()` + eager video scan on startup and pack change |
| `c0d2407` | T2D vanilla sprite `DontUnloadUnusedAsset` (fix revert failure) |
| `6a6f28a` | tk2d atlas RT reuse in-place (fix all-objects transparency on toggle) |

---

## Open Design Areas

- **Conditional sprite system**: pre-bake N variant RTs per material; condition switch =
  pointer swap (`mat.mainTexture = variantRT`), zero GPU cost. Candidate map:
  `conditionKey → collection → material → spriteName → byte[]`, composited lazily on change.
  Multiple active conditions composited additively; priority resolves per-sprite conflicts.
- **Apply/Revert as pointer swaps**: `_originalTextures` (vanilla RT) and
  `LoadedAtlasesTextures` (custom RT) are both in memory — pack ON/OFF should be a
  pointer swap, not a blit cycle. Needs `Bake()` / `SetActive(bool)` split in `SpriteLoader`.
