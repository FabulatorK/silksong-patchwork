# The Patchwork Master Plan

> You thought the sprites were safe? You thought the dialogue was untouchable? Foolish.
> This document details every phase of the operation. Updated: 2026-03-11

---

## Phase 1: The Demands of Our Loyal Subjects

*Source: [patchwork.ashiepaws.dev/boards/feature-requests](https://patchwork.ashiepaws.dev/boards/feature-requests)*

### In-Game Dialogue Editor
*"Why stop at sprites when we can rewrite reality itself?"*
- [ ] An in-game editor window for seamless editing of dialogue box text
- [ ] Window shows text of any active dialogue box on screen and makes it editable
- [ ] "Save" button that automatically places edited text in the correct Patchwork folder location

### Pack Manager (Minecraft Resource Pack Style)
*"An arsenal of skins, all at your fingertips. No more rummaging through folders like some peasant."*
- [ ] In-game UI to toggle Patchwork on/off without moving plugin files/folders
- [ ] Support switching between multiple installed skins/asset packs
- [ ] Pack load ordering / priority system

### Oversized Sprite Support
*"You dare confine our artists to tiny bounding boxes? We shall break free."*
- [ ] Some sprites have very tight bounding boxes, limiting artist freedom
- [ ] Provide a way to display oversized sprites that exceed original bounding box dimensions

### UI Scaling
*"Already partially conquered. But the work is never truly done."*
- [x] UI scaling options for Patchwork's in-game windows and UI elements
- [ ] Room for improvement on current implementation (higher-res display support)

### Conditional Sprites
*"The sprites shall shift and change with the tides of battle. Magnificent."*
- [ ] Allow asset pack creators to swap replacement assets based on in-game conditions
- [ ] Conditions: health, selected crest, equipped items, etc.
- [ ] Condition configuration format TBD (likely per-sprite JSON or folder convention)

---

## Phase 2: What We Have Already Seized

*Every checked box is a small victory. Savour them.*

### T2D Sprite Replacement System
- [x] Individual T2D sprite replacement (`Sprites/T2D/{cleanAtlasName}/{spriteName}.png`)
- [x] T2D spritesheet replacement (`Spritesheets/T2D/{cleanAtlasName}.png`)
- [x] `CreateSpriteFromSpritesheet()` with pivot normalization and rect bounds checking
- [x] `CleanTextureName()` handling raw atlas names (e.g. `sactx-0-4096x4096-BC7-Hornet-5bdf1644` -> `Hornet`)
- [x] Supports both `-BC7-` and `DXT5|BC3-` compression format atlas names

### Eager Loading System
*"Every sprite, replaced before the scene even knows what hit it. No pop-in. No mercy."*
- [x] Two-phase eager loading: preload all textures at plugin startup, apply on scene load
- [x] All replacements ready on first frame after scene load (no pop-in)
- [x] Static caches persist across scene traversal, death, and fast travel
- [x] `ApplyT2DReplacementsInScene()` fires on every `sceneLoaded` event

### Performance: Reentrant Guard Optimization
*"We used to inspect the entire stack trace like amateurs. Never again."*
- [x] Replaced `StackTrace()` inspection with simple `_handling` / `_enforcing` boolean flags
- [x] Zero allocation, O(1) guard checks
- [x] `try-finally` block ensures flags always reset (no stuck guard risk)

### File Watcher & Hot Reload
*"Change a PNG on disk and watch the game bend to your will in real time."*
- [x] File watcher detects changes to sprite PNGs on disk
- [x] Hot reload applies changes without restarting the game
- [x] Cache invalidation on file change

### tk2d Sprite Replacement (Pre-existing)
*"The original conquest. Where it all began."*
- [x] Individual tk2d sprite replacement (`Sprites/{collectionName}/{materialName}/{spriteName}.png`)
- [x] tk2d spritesheet replacement (`Spritesheets/{collectionName}/{materialName}.png`)
- [x] `InitPostfix()` hooks collection initialization for immediate replacement

---

## The Blueprint

### Directory Structure
*"Know the layout of the fortress you are infiltrating."*
```
Patchwork/
├── Sprites/
│   ├── {CollectionName}/          # tk2d sprites
│   │   └── {MaterialName}/
│   │       └── sprite_name.png
│   └── T2D/                       # Texture2D sprites
│       └── {CleanAtlasName}/
│           └── sprite_name.png
└── Spritesheets/
    ├── {CollectionName}/          # tk2d spritesheets
    │   └── {MaterialName}.png
    └── T2D/                       # T2D spritesheets
        └── {CleanAtlasName}.png
```

### Key Caches (Static, Scene-Persistent)
*"These survive death, fast travel, and scene transitions. They are eternal."*
- `LoadedT2DSprites` — converted Sprite objects ready for injection
- `PreloadedT2DTextures` — raw Texture2D loaded from PNG files
- `LoadedAtlases` / `LoadedAtlasesTextures` — tk2d atlas cache
- `KnownT2DSpriteRenderers` / `KnownT2DImages` — tracked components for enforcement

### Known Weaknesses (Shh)
- `KnownT2DSpriteRenderers` HashSet can accumulate stale references across scenes (cleaned during `EnforceT2DReplacements()` LateUpdate)
- `CreateSpriteFromSpritesheet()` pivot normalization (`original.pivot / rect.size`) and rect bounds — first thing to verify with a debugger

---

## Secret Lair Setup
*"One can scheme from anywhere — even a phone."*
- GitHub Codespaces recommended for mobile compilation
- .NET 6.0 SDK required
- Project compiles with one trivial null reference warning (cosmetic, not a runtime issue)
