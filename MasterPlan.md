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

### Shared Atlas Contamination (UI Vanishing Bug)
*"Unity packs sprites from different systems into one atlas. Replace the atlas, lose the UI. Diabolical."*
- [ ] **Root cause**: `TrySwapTexture` does `LoadImage()` which overwrites the entire atlas pixel data
  - Unity can pack character sprites AND UI sprites (silk spool, bar, crests) into the same runtime atlas
  - When a modder provides e.g. `Hornet.png` with only Hornet frames, UI sprite regions become transparent → they vanish
  - The 4096x4096 "Hornet" atlas may contain the silk spool, equipment bar, and crest icons
- [x] **Diagnostic**: `LogAtlasContents()` now logs ALL sprites sharing a replaced atlas so modders can see collateral damage
- [ ] **Fix options** (prioritized):
  1. Composite replacement: only overwrite sprite rect regions that the modder intends to change, preserve the rest
  2. Allow modders to include all atlas sprites in their replacement PNG (requires atlas content documentation)
  3. Per-sprite opt-out: let modders mark specific sprites as "do not replace" in a config
- [ ] Use dump logs to catalogue which atlases are shared between game systems

### Spritesheet Size Mismatch Handling
*"The Hornet atlas dares to appear at multiple resolutions. We must accommodate this... insolence."*
- [ ] Runtime textures for the same atlas name can appear at different sizes (e.g. Hornet: 4096x4096, 333x467, 650x429)
- [ ] Currently skipped with a warning when replacement PNG dimensions don't match
- [ ] Investigate scaling replacement to match, or supporting multiple resolution variants
- [ ] **Tracking**: Mismatch warning now logs full raw texture name and instance ID for cross-referencing
- [ ] Catalogue all observed Hornet atlas resolutions from logs to determine if variants are scene-specific or LOD-based
- [ ] Decide approach: multi-resolution replacement PNGs, runtime scaling, or both
- [ ] **Note**: Smaller "Hornet" atlas instances (333x467, 650x429) may actually be UI atlases containing Hornet icons — investigate whether these are distinct from the main character atlas

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
