# Patchwork Session Journal

> Recovery file for Claude Code context. Covers the full arc of work on branch
> `claude/patchwork-improvements-my7Ut` (30 commits since master).

---

## Session Timeline & Key Decisions

### 1. T2D Spritesheet/Atlas System (Foundation)
- Built T2D spritesheet replacement to match the existing tk2d system
- `CleanTextureName()` strips raw Unity atlas names (e.g. `sactx-0-4096x4096-BC7-Hornet-5bdf1644` → `Hornet`)
- Supports both `-BC7-` and `DXT5|BC3-` compression format names
- Added raw atlas name fallback so modders can use either clean or raw filenames

### 2. In-Place Texture Replacement (Major Architecture Shift)
- Rewrote from "create new Sprite" to "overwrite the Texture2D pixel data in place"
- This means ALL renderers using that texture get the swap for free — no per-component tracking needed
- Hot-reload required special handling: defer `UnityEngine.Object.Destroy()` until after re-applying swaps

### 3. Spritesheet Cache Collision Bug
- Same atlas (e.g. Hornet) appears at multiple resolutions (2048x2048 and 4096x4096)
- `SpritesheetOverrides` was a flat dict keyed by clean name — only one resolution survived
- Fix: store `List<(byte[], int, int)>` per clean name, match by dimensions at swap time
- Dump/convert paths updated to include `{W}x{H}` to avoid file overwrites

### 4. Particle & Standalone Texture Support
- Particles use plain `Texture2D` objects (not atlases) — names like `rock_particles`, `soul_orb`
- Already caught by the global `Texture2D` sweep in scene apply
- Added `DumpStandaloneTextures()` for modder discoverability
- Initially added a dedicated `ParticleSystemRenderer` sweep, then removed it as redundant

### 5. UI Vanishing Bug (Multi-Step Investigation)
- **Symptom**: Silk spool and crests disappeared when Patchwork T2D was active
- **Red herring**: Initially suspected shared atlas contamination (Hornet atlas containing UI sprites)
- **Diagnostic**: Added `LogAtlasContents()` — confirmed Hornet atlas has only Hornet sprites (134 of them)
- **Root cause**: `EnforceT2DReplacements` ran every `LateUpdate` and matched sprites by name alone. If a UI `Image` sprite shared a name with any T2D replacement file, it got replaced with wrong-sized sprite data → blank
- **Fix**: `ConfirmedT2DSpriteNames` HashSet — only sprites confirmed on T2D atlas textures are eligible for enforcement
- Added `[T2D-UI]` diagnostic warnings for any texture swap hitting UI `Image` components

### 6. Performance Bug (Enemy AI Breaking)
- **Symptom**: Enemy AI completely broken — enemies frozen/non-responsive
- **Root cause**: Per-frame reflection (`GetComponent` via string) and filesystem scans in the enforcement loop
- **Fix**: Eliminated all per-frame reflection and filesystem access; gated T2D per-frame work behind `HasT2DReplacements` early-out check
- This was a critical fix — the per-frame cost was starving the game's update loop

### 7. Conflict Detection (CustomizerT2D)
- Another mod (`CustomizerT2D` / variants like `Customizer2D`, `CustomizerTD2`) can conflict with Patchwork's T2D system
- Added startup conflict detection that warns users if both are loaded
- Broadened detection to catch all naming variants

### 8. AudioHandler NRE Fix
- `AudioHandler` was spamming NullReferenceException for unsupported audio formats
- Simple null check fix

### 9. Vanilla Frame Flash Fix
- First-trigger sprite replacements showed one frame of vanilla sprite before the replacement applied
- Fixed timing to eliminate the flash

---

## Architecture Notes

### Key Files
- Main plugin file handles all T2D logic (preloading, scene apply, enforcement, hot-reload)
- MasterPlan.md has the full feature roadmap and technical details of each fix
- Static caches persist across scenes (death, fast travel, etc.)

### How T2D Replacement Works (Current)
1. **Startup**: `PreloadAllT2DTextures()` loads all PNGs from `Sprites/T2D/` and `Spritesheets/T2D/`
2. **Scene load**: `ApplyT2DReplacementsInScene()` iterates all `Texture2D` objects, calls `TrySwapTexture()` for matches
3. **Per-frame**: `EnforceT2DReplacements()` in `LateUpdate` catches late-spawned renderers (gated behind `HasT2DReplacements`)
4. **Hot-reload**: File watcher detects PNG changes → rebuild caches → re-apply in-place swaps

### Directory Layout
```
Patchwork/
├── Sprites/T2D/{atlas}/sprite.png     # Individual sprite replacements
├── Spritesheets/T2D/{atlas}.png        # Full spritesheet replacements
├── Dumps/T2D/{atlas}/                  # Dumped sprites for modders
│   ├── _atlas_{W}x{H}.png
│   └── sprite_name.png
└── Dumps/T2D/_standalone/              # Standalone texture dumps
```

---

## Open Items (from MasterPlan.md)
- In-game dialogue editor
- Pack manager (Minecraft resource-pack style)
- Oversized sprite support
- Conditional sprites (health, crest, equipment-based swaps)
- TintRenderer investigation
- UI vanishing: `[T2D-UI]` log check still pending from user
- Whether to support asset-path-based replacement organization

---

## Tone & Style
- MasterPlan.md is written in a playful "goofy villain monologue" style per user's request
- The user (FabulatorK) is the Patchwork mod author — knows the codebase well
- This is a BepInEx/Unity mod for Hollow Knight: Silksong
