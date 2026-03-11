# Patchwork Master Plan

> Living document tracking feature requests, completed work, and roadmap.
> Last updated: 2026-03-11

---

## Community Feature Requests

*Source: [patchwork.ashiepaws.dev/boards/feature-requests](https://patchwork.ashiepaws.dev/boards/feature-requests)*

### In-Game Dialogue Editor
- [ ] An in-game editor window for seamless editing of dialogue box text
- [ ] Window shows text of any active dialogue box on screen and makes it editable
- [ ] "Save" button that automatically places edited text in the correct Patchwork folder location

### Pack Manager (Minecraft Resource Pack Style)
- [ ] In-game UI to toggle Patchwork on/off without moving plugin files/folders
- [ ] Support switching between multiple installed skins/asset packs
- [ ] Pack load ordering / priority system

### Oversized Sprite Support
- [ ] Some sprites have very tight bounding boxes, limiting artist freedom
- [ ] Provide a way to display oversized sprites that exceed original bounding box dimensions

### UI Scaling
- [x] UI scaling options for Patchwork's in-game windows and UI elements
- [ ] Room for improvement on current implementation (higher-res display support)

### Conditional Sprites
- [ ] Allow asset pack creators to swap replacement assets based on in-game conditions
- [ ] Conditions: health, selected crest, equipped items, etc.
- [ ] Condition configuration format TBD (likely per-sprite JSON or folder convention)

---

## Completed Work (This Session)

### T2D Sprite Replacement System
- [x] Individual T2D sprite replacement (`Sprites/T2D/{cleanAtlasName}/{spriteName}.png`)
- [x] T2D spritesheet replacement (`Spritesheets/T2D/{cleanAtlasName}.png`)
- [x] `CreateSpriteFromSpritesheet()` with pivot normalization and rect bounds checking
- [x] `CleanTextureName()` handling raw atlas names (e.g. `sactx-0-4096x4096-BC7-Hornet-5bdf1644` -> `Hornet`)
- [x] Supports both `-BC7-` and `DXT5|BC3-` compression format atlas names

### Eager Loading System
- [x] Two-phase eager loading: preload all textures at plugin startup, apply on scene load
- [x] All replacements ready on first frame after scene load (no pop-in)
- [x] Static caches persist across scene traversal, death, and fast travel
- [x] `ApplyT2DReplacementsInScene()` fires on every `sceneLoaded` event

### Performance: Reentrant Guard Optimization
- [x] Replaced `StackTrace()` inspection with simple `_handling` / `_enforcing` boolean flags
- [x] Zero allocation, O(1) guard checks
- [x] `try-finally` block ensures flags always reset (no stuck guard risk)

### File Watcher & Hot Reload
- [x] File watcher detects changes to sprite PNGs on disk
- [x] Hot reload applies changes without restarting the game
- [x] Cache invalidation on file change

### tk2d Sprite Replacement (Pre-existing)
- [x] Individual tk2d sprite replacement (`Sprites/{collectionName}/{materialName}/{spriteName}.png`)
- [x] tk2d spritesheet replacement (`Spritesheets/{collectionName}/{materialName}.png`)
- [x] `InitPostfix()` hooks collection initialization for immediate replacement

---

## Architecture Notes

### Directory Structure
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
- `LoadedT2DSprites` — converted Sprite objects ready for injection
- `PreloadedT2DTextures` — raw Texture2D loaded from PNG files
- `LoadedAtlases` / `LoadedAtlasesTextures` — tk2d atlas cache
- `KnownT2DSpriteRenderers` / `KnownT2DImages` — tracked components for enforcement

### Known Watch Points
- `KnownT2DSpriteRenderers` HashSet can accumulate stale references across scenes (cleaned during `EnforceT2DReplacements()` LateUpdate)
- `CreateSpriteFromSpritesheet()` pivot normalization (`original.pivot / rect.size`) and rect bounds — first thing to verify with a debugger

---

## Development Environment
- GitHub Codespaces recommended for mobile compilation
- .NET 6.0 SDK required
- Project compiles with one trivial null reference warning (cosmetic, not a runtime issue)
