# The Patchwork Master Plan

> You thought the sprites were safe. That nobody would dare touch the dialogue.
> How delightfully naive. This document is the full blueprint. Updated: 2026-03-13

---

## Phase 1: The Demands of Our Adoring Public

*Source: [patchwork.ashiepaws.dev/boards/feature-requests](https://patchwork.ashiepaws.dev/boards/feature-requests)*

### In-Game Dialogue Editor
*"First we took the sprites. Then we turned our gaze upon the dialogue. Naturally."*
- [ ] An in-game editor window for seamless editing of dialogue box text
- [ ] Window shows text of any active dialogue box on screen and makes it editable
- [ ] "Save" button that automatically places edited text in the correct Patchwork folder location

### Pack Manager (Minecraft Resource Pack Style)
*"Swapping skins should be elegant, not tedious folder-shuffling. One button. One decisive click."*
- [ ] In-game UI to toggle Patchwork on/off without moving plugin files/folders
- [ ] Support switching between multiple installed skins/asset packs
- [ ] Pack load ordering / priority system

### Oversized Sprite Support
*"You dare confine our artists to tiny bounding boxes? We shall break free."*
- [ ] Some sprites have very tight bounding boxes, limiting artist freedom
- [ ] Provide a way to display oversized sprites that exceed original bounding box dimensions

### UI Scaling
*"Solid work so far. But we are not yet satisfied."*
- [x] UI scaling options for Patchwork's in-game windows and UI elements
- [ ] Room for improvement on current implementation (higher-res display support)

### Conditional Sprites
*"Sprites that shift form based on in-game state. The artists will have such power at their fingertips."*
- [ ] Allow asset pack creators to swap replacement assets based on in-game conditions
- [ ] Conditions: health, selected crest, equipped items, etc.
- [ ] Condition configuration format TBD (likely per-sprite JSON or folder convention)

### TintRenderer Investigation
*"They tint sprites at runtime. We must understand this sorcery before we can subvert it."*
- [ ] Investigate how TintRenderer works and interacts with sprite replacement
- [ ] Determine if tint colors are applied post-replacement or need special handling
- [ ] Check if TintRenderer conflicts with T2D in-place texture swaps

### UI Vanishing Bug (Fixed — Sprite Name Collision)
*"The enforcement loop couldn't tell a UI sprite from a T2D sprite. Every frame, it replaced the wrong ones."*
- [x] **Root cause**: NOT shared atlas contamination — the Hornet atlas contains only Hornet sprites (134 of them, confirmed via `LogAtlasContents`)
- [x] **Actual root cause**: `EnforceT2DReplacements` (runs every `LateUpdate`) and `HandleLoad` looked up sprites by `sprite.name` in `LoadedT2DSprites`/`PreloadedT2DTextures` without checking if the sprite was actually T2D-backed
  - If a UI `Image` sprite shared a name with any T2D replacement file, the enforcement loop would replace it every frame
  - The replacement Sprite has wrong rect/dimensions → UI element renders as blank
- [x] **Fix**: Added `ConfirmedT2DSpriteNames` HashSet — only sprites confirmed to be on T2D atlas textures (via `IsT2DTexture`) are eligible for replacement/enforcement
  - Populated during `PreloadAllT2DTextures`, `ApplyT2DReplacementsInScene`, and `HandleLoad`
  - Gates `EnforceT2DReplacements`, `CheckForUninitializedSprites`, and `HandleLoad` cached lookup
- [x] **Diagnostic**: `LogAtlasContents()` logs ALL sprites sharing a replaced atlas for visibility

### Spritesheet Multi-Resolution Support (Fixed)
*"The Hornet atlas dares to appear at multiple resolutions. We now speak all its languages."*
- [x] **Problem**: Two runtime atlases (`sactx-1-2048x2048-BC7-Hornet-*` and `sactx-0-4096x4096-BC7-Hornet-*`) both clean to "Hornet"
  - `SpritesheetOverrides` was a flat dictionary — only one PNG per clean name, first wins
  - The 4096x4096 atlas was always skipped (size mismatch)
  - The 333x467 and 650x429 "sizes" were individual sprite rects on the 2048x2048 atlas, not separate atlases
- [x] **Fix**: `SpritesheetOverrides` now stores `List<(byte[], int, int)>` per clean name — multiple resolution variants
  - `TrySwapTexture` finds the variant matching runtime texture dimensions
  - Mismatch warning now shows all available replacement sizes
  - Mismatched atlases now also get `LogAtlasContents()` output for diagnostics
- [x] **File layout**: Two ways to provide variants:
  - Flat: `Spritesheets/T2D/Hornet.png` (single size, as before)
  - Raw names: `sactx-1-2048x2048-BC7-Hornet-5bdf1644.png` and `sactx-0-4096x4096-BC7-Hornet-5bdf1644.png` — both clean to "Hornet", stored as separate dimension variants
  - Subdirectory: `Spritesheets/T2D/Hornet/*.png` (multiple PNGs at different sizes)
  - Both approaches can coexist; duplicates at the same dimensions are skipped
- [x] **Dump collision fix**: `DumpT2DAtlasTexture` now writes `_atlas_{W}x{H}.png` instead of `_atlas.png` — both Hornet atlases dump without overwriting each other
- [x] **Convert collision fix**: `ConvertT2DSpritesheet` now outputs to `Converted/T2D/{name}/{W}x{H}/` subdirectories
- [x] **LogAtlasContents** now includes texture dimensions and raw name for easy cross-referencing

### UI Vanishing Bug (Under Investigation)
*"The silk spool and crests have gone missing. The culprit remains at large."*
- [x] **Not the Hornet atlas**: Log confirms all 134 sprites on the 2048x2048 Hornet atlas are Hornet-related (no UI sprites)
- [x] **Not sprite name collision**: `ConfirmedT2DSpriteNames` whitelist added but didn't resolve the issue
- [x] **Diagnostic added**: `SetImageSpritePostfix` now logs a warning if `TrySwapTexture` overwrites a texture backing a UI `Image` — this will identify if in-place atlas swap is hitting UI textures
- [x] **Diagnostic added**: Mismatched atlases now also log their sprite contents — will reveal if 4096x4096 atlas contains UI sprites
- [ ] **Next step**: Check log output for `[T2D-UI]` warnings and mismatched atlas contents to identify the actual cause
- [ ] If UI textures are NOT being swapped, the issue may be external to Patchwork (scene setup, Canvas hierarchy, etc.)

### Non-Atlas Texture Dump Gap (Fixed)
*"Particles_ash hid in the cracks between atlas and standalone. No more."*
- [x] **Problem**: Non-T2D textures with Sprite objects (e.g. `Particles_ash` at internal path `Assets/Sprites/Hero/Knight/death_v02`) were missed by both dump paths
  - `DumpAllT2DSprites` only called `HandleDump` for `IsT2DTexture()` textures
  - `DumpStandaloneTextures` skipped anything with Sprite references
  - The `else` branch in `HandleDump` was effectively dead code
- [x] **Fix**: `DumpAllT2DSprites` now also dumps non-T2D sprite-backed textures (one per texture instance ID)
- [x] **Sanitization**: `SanitizeForFilesystem()` now strips `/`, `\`, `:` in addition to `|` for safe file paths
- [x] **Logging**: Non-atlas texture dumps now log raw name → sanitized name mapping for modder discoverability
- [ ] **Open question**: Should Patchwork support organizing replacements by internal asset path, or is flat `Sprites/T2D/{textureName}.png` sufficient?

---

## Phase 2: Accomplished

*A trophy case of victories, each hard-won.*

### T2D Sprite Replacement System
- [x] Individual T2D sprite replacement (`Sprites/T2D/{cleanAtlasName}/{spriteName}.png`)
- [x] T2D spritesheet replacement (`Spritesheets/T2D/{cleanAtlasName}.png`)
- [x] `CreateSpriteFromSpritesheet()` with pivot normalization and rect bounds checking
- [x] `CleanTextureName()` handling raw atlas names (e.g. `sactx-0-4096x4096-BC7-Hornet-5bdf1644` -> `Hornet`)
- [x] Supports both `-BC7-` and `DXT5|BC3-` compression format atlas names
- [x] Backwards-compatible raw atlas name loading via `SanitizeForFilesystem()` fallback

### Standalone Texture Replacement (Particles, etc.)
*"They thought standalone textures were beyond our reach. They were wrong."*
- [x] Particle textures are standalone `Texture2D` objects — plain names like `rock_particles`, `soul_orb`
- [x] Not T2D atlases: no `-BC7-` prefix, no `Sprite` objects, `IsT2DTexture` returns false
- [x] Already handled by Pass 1 `Resources.FindObjectsOfTypeAll<Texture2D>()` + `TrySwapTexture`
- [x] Replacement PNGs placed in `Spritesheets/T2D/{textureName}.png` (same as atlas spritesheets)
- [x] Dump support via `DumpStandaloneTextures()` — outputs to `Dumps/T2D/_standalone/`
- [x] No dedicated particle sweep needed — the global `Texture2D` sweep is sufficient
- [x] Dump broadened: sweeps all `Resources.FindObjectsOfTypeAll<Texture2D>()` instead of only `ParticleSystemRenderer` materials

### Eager Loading System
*"Every sprite, locked and loaded before the scene even knows what happened."*
- [x] Two-phase eager loading: preload all textures at plugin startup, apply on scene load
- [x] All replacements ready on first frame after scene load (no pop-in)
- [x] Static caches persist across scene traversal, death, and fast travel
- [x] `ApplyT2DReplacementsInScene()` fires on every `sceneLoaded` event

### Performance: Reentrant Guard Optimization
*"We once inspected the entire stack trace. Embarrassing. That has been corrected."*
- [x] Replaced `StackTrace()` inspection with simple `_handling` / `_enforcing` boolean flags
- [x] Zero allocation, O(1) guard checks
- [x] `try-finally` block ensures flags always reset (no stuck guard risk)

### File Watcher & Hot Reload
*"Drop a PNG in the folder. Watch the game bend to your will in real time."*
- [x] File watcher detects changes to sprite PNGs on disk
- [x] Hot reload applies changes without restarting the game
- [x] Cache invalidation on file change

### tk2d Sprite Replacement (Pre-existing)
*"Where this whole operation began. The first heist."*
- [x] Individual tk2d sprite replacement (`Sprites/{collectionName}/{materialName}/{spriteName}.png`)
- [x] tk2d spritesheet replacement (`Spritesheets/{collectionName}/{materialName}.png`)
- [x] `InitPostfix()` hooks collection initialization for immediate replacement

---

## The Blueprint

### Directory Structure
*"Know the layout."*
```
Patchwork/
├── Sprites/
│   ├── {CollectionName}/          # tk2d sprites
│   │   └── {MaterialName}/
│   │       └── sprite_name.png
│   └── T2D/                       # Texture2D sprites
│       └── {CleanAtlasName}/
│           └── sprite_name.png
├── Spritesheets/
│   ├── {CollectionName}/          # tk2d spritesheets
│   │   └── {MaterialName}.png
│   └── T2D/                       # T2D spritesheets + standalone textures
│       └── {CleanAtlasName}.png
└── Dumps/
    └── T2D/
        ├── {CleanAtlasName}/      # Dumped atlas sprites
        │   ├── _atlas.png
        │   └── sprite_name.png
        └── _standalone/           # Dumped standalone textures (particles, etc.)
            └── texture_name.png
```

### Key Caches (Static, Scene-Persistent)
*"These survive death, fast travel, scene transitions. Indestructible."*
- `LoadedT2DSprites` — converted Sprite objects ready for injection
- `PreloadedT2DTextures` — raw Texture2D loaded from PNG files
- `LoadedAtlases` / `LoadedAtlasesTextures` — tk2d atlas cache
- `KnownT2DSpriteRenderers` / `KnownT2DImages` — tracked components for enforcement

### Known Weak Spots
*"Every plan has its vulnerabilities. These are ours."*
- `KnownT2DSpriteRenderers` HashSet can accumulate stale references across scenes (cleaned during `EnforceT2DReplacements()` LateUpdate)
- `CreateSpriteFromSpritesheet()` pivot normalization (`original.pivot / rect.size`) and rect bounds — first thing to verify with a debugger
- Spritesheet size mismatch: same atlas name can appear at multiple runtime resolutions (see Phase 1)

---

## The Secret Lair
*"One can scheme from anywhere. A coffee shop. A moving train. A phone on the bus."*
- GitHub Codespaces recommended for mobile compilation
- .NET 6.0 SDK required
- Project compiles with one trivial null reference warning (cosmetic, not a runtime issue)
