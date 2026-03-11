# The Patchwork Master Plan

> Oh behave! You thought the sprites were safe? That nobody would DARE lay a finger on the dialogue?
> Think again, baby. This document is the full blueprint to our diabolical scheme — and it is EXTREMELY shagadelic. Updated: 2026-03-11

---

## Phase 1: The Demands of Our Adoring Public

*Source: [patchwork.ashiepaws.dev/boards/feature-requests](https://patchwork.ashiepaws.dev/boards/feature-requests)*

### In-Game Dialogue Editor
*"First we took the sprites. Then we looked at the dialogue and said... yeah, we're taking that too. Yeah baby, YEAH."*
- [ ] An in-game editor window for seamless editing of dialogue box text
- [ ] Window shows text of any active dialogue box on screen and makes it editable
- [ ] "Save" button that automatically places edited text in the correct Patchwork folder location

### Pack Manager (Minecraft Resource Pack Style)
*"Swapping skins should be groovy, not some tedious folder-shuffling nightmare. One button, baby. One gorgeous, shagadelic button."*
- [ ] In-game UI to toggle Patchwork on/off without moving plugin files/folders
- [ ] Support switching between multiple installed skins/asset packs
- [ ] Pack load ordering / priority system

### Oversized Sprite Support
*"You DARE confine our artists to tiny little bounding boxes? That's not groovy at all! We shall BREAK FREE and look absolutely smashing doing it!"*
- [ ] Some sprites have very tight bounding boxes, limiting artist freedom
- [ ] Provide a way to display oversized sprites that exceed original bounding box dimensions

### UI Scaling
*"Smashing work so far. Really top-notch. But we're not resting on our laurels — oh no, baby."*
- [x] UI scaling options for Patchwork's in-game windows and UI elements
- [ ] Room for improvement on current implementation (higher-res display support)

### Conditional Sprites
*"Sprites that shapeshift based on what's happening in-game? That is DANGEROUSLY groovy. I'm not even sure the world is ready for this kind of mojo."*
- [ ] Allow asset pack creators to swap replacement assets based on in-game conditions
- [ ] Conditions: health, selected crest, equipped items, etc.
- [ ] Condition configuration format TBD (likely per-sprite JSON or folder convention)

---

## Phase 2: Missions We Have SPECTACULARLY Accomplished

*Oh yes baby, we have been BUSY. Allow me to present our trophy case of magnificent victories.*

### T2D Sprite Replacement System
- [x] Individual T2D sprite replacement (`Sprites/T2D/{cleanAtlasName}/{spriteName}.png`)
- [x] T2D spritesheet replacement (`Spritesheets/T2D/{cleanAtlasName}.png`)
- [x] `CreateSpriteFromSpritesheet()` with pivot normalization and rect bounds checking
- [x] `CleanTextureName()` handling raw atlas names (e.g. `sactx-0-4096x4096-BC7-Hornet-5bdf1644` -> `Hornet`)
- [x] Supports both `-BC7-` and `DXT5|BC3-` compression format atlas names
- [x] Backwards-compatible raw atlas name loading via `SanitizeForFilesystem()` fallback

### Eager Loading System
*"Every single sprite, locked and loaded before the scene even knows what's happening. The game walks in and we're already there, looking FANTASTIC. No pop-in. No mercy. Just pure, unadulterated mojo."*
- [x] Two-phase eager loading: preload all textures at plugin startup, apply on scene load
- [x] All replacements ready on first frame after scene load (no pop-in)
- [x] Static caches persist across scene traversal, death, and fast travel
- [x] `ApplyT2DReplacementsInScene()` fires on every `sceneLoaded` event

### Performance: Reentrant Guard Optimization
*"We used to inspect the ENTIRE stack trace like some kind of square. Honestly, who does that? Amateurs, that's who. We looked at ourselves in the mirror and said 'not groovy' and fixed it immediately."*
- [x] Replaced `StackTrace()` inspection with simple `_handling` / `_enforcing` boolean flags
- [x] Zero allocation, O(1) guard checks
- [x] `try-finally` block ensures flags always reset (no stuck guard risk)

### File Watcher & Hot Reload
*"You drop a PNG in the folder and BAM — instant results. No restart. No waiting. The game just bends to your will in real time. That's the kind of power that makes you want to do a little victory dance, baby."*
- [x] File watcher detects changes to sprite PNGs on disk
- [x] Hot reload applies changes without restarting the game
- [x] Cache invalidation on file change

### tk2d Sprite Replacement (Pre-existing)
*"Where this whole beautiful, ridiculous operation started. The very first caper. The heist that launched a thousand sprites."*
- [x] Individual tk2d sprite replacement (`Sprites/{collectionName}/{materialName}/{spriteName}.png`)
- [x] tk2d spritesheet replacement (`Spritesheets/{collectionName}/{materialName}.png`)
- [x] `InitPostfix()` hooks collection initialization for immediate replacement

---

## The Blueprint

### Directory Structure
*"Know the layout of the fortress you're infiltrating, baby. Memorize it. Love it."*
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
*"These absolute UNITS survive death, fast travel, scene transitions, you name it. Completely indestructible. You could throw them into a volcano and they'd climb back out looking fabulous."*
- `LoadedT2DSprites` — converted Sprite objects ready for injection
- `PreloadedT2DTextures` — raw Texture2D loaded from PNG files
- `LoadedAtlases` / `LoadedAtlasesTextures` — tk2d atlas cache
- `KnownT2DSpriteRenderers` / `KnownT2DImages` — tracked components for enforcement

### Known Weak Spots (Shhh — Keep It Between Us, Baby)
*"Every supervillain has an Achilles' heel. Ours are... manageable. But let's not broadcast them, yeah?"*
- `KnownT2DSpriteRenderers` HashSet can accumulate stale references across scenes (cleaned during `EnforceT2DReplacements()` LateUpdate)
- `CreateSpriteFromSpritesheet()` pivot normalization (`original.pivot / rect.size`) and rect bounds — first thing to verify with a debugger

---

## The Secret Lair
*"A true villain — a PROPERLY groovy villain — can scheme from absolutely anywhere. A coffee shop. A moving train. A phone on the bus while pretending to read the news. Shagadelic."*
- GitHub Codespaces recommended for mobile compilation
- .NET 6.0 SDK required
- Project compiles with one trivial null reference warning (cosmetic, not a runtime issue)
