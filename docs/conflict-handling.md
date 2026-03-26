# Patchwork Conflict Handling

## Priority Order

Assets are loaded in a fixed priority order. The first pack to claim an asset key wins; all later packs that supply the same key are silently skipped and recorded as losers.

```
Base Patchwork folder  (highest — always wins)
Pack #0  (top of list in Pack Manager)
Pack #1
...
Pack #N  (lowest priority)
```

## Covered Asset Types

| Type | Key format | Guard |
|------|-----------|-------|
| T2D individual sprite | `atlasCleanName/spriteName` or bare `spriteName` | `_preloadedBytes.ContainsKey` |
| T2D spritesheet | raw PNG filename (no extension) | `SpritesheetOverrides.ContainsKey` |
| tk2d sprite / sheet | `collection/spriteName` | first match in scan loop |
| Audio | filename without extension | `_soundIndex.ContainsKey` |

## ConflictTracker

Every skipped asset is recorded as an `Entry`:

```
Type       — "sprite" | "sheet" | "t2d-sprite" | "t2d-sheet"
Key        — asset identifier
WinnerPack — path of the pack whose copy was used (null = base folder)
LoserPack  — path of the pack that was skipped (null = base folder)
```

The tracker is cleared at the start of every `Apply()` or `Rescan()` cycle and rebuilt during load. Two query helpers exist for the GUI:

- `ShadowedCount(packPath)` — how many of this pack's assets were overridden by a higher-priority pack
- `OverridingCount(packPath)` — how many lower-priority assets this pack shadows

## Notes

- Audio conflicts are **not** recorded in `ConflictTracker` — `RebuildSoundIndex` silently takes first-found only.
- The base folder's precedence over all packs is intentional and cannot be changed by reordering packs.
- Conflict data is only valid until the next `Apply()` / `Rescan()` / pack toggle.
