# Patchwork Packed Bundle Format

## Motivation

Patchwork's current loading pipeline is optimised for **asset creators** — it watches files
for changes, rebuilds indexes on every reload, decodes PNG data on demand, and keeps
hot-reload infrastructure running throughout the session.  This is the right trade-off
for iteration, but it is the wrong trade-off for **end users**, who simply want their
chosen pack to load quickly at startup with a minimal memory and CPU footprint.

The packed bundle format addresses this by following the same split Unity itself uses:

| | Dev mode (current) | Packed mode (planned) |
|---|---|---|
| Target user | Asset creator | End user |
| File layout | Raw PNGs in folder hierarchy | Single `.pwpk` binary |
| Texture upload | PNG decode → `LoadImage` | Raw bytes → `LoadRawTextureData` |
| Index | Built at runtime by scanning directories | Pre-baked in file header |
| File watchers | Running (SpriteFileWatcher, TextFileWatcher) | Not started |
| Hot reload | Supported | Not supported |
| GPU memory | RGBA32 per-sprite texture | RGBA32 or DXT5 block-compressed |
| Load time | Many small file reads, repeated decode | One sequential read, no decode |

A pack distributed **without** a `.pwpk` file continues to work exactly as today (dev
mode).  A `.pwpk` beside the pack root silently upgrades loading to the fast path.

---

## File Format — `.pwpk`

All multi-byte integers are **little-endian**.

### Header

```
Offset  Size  Field
0       4     Magic: ASCII "PWPK"
4       4     Format version (currently 1)
8       4     Entry count  (N)
12      4     Reserved / flags (0 for version 1)
```

### Entry Table  (immediately after header, N entries)

Each entry is fixed-size in the table portion, followed by a variable-length key
string in a separate key block.  The key block immediately follows the last entry.

```
Field         Size   Description
key_offset    4      Byte offset from start of key block to this entry's key
key_length    2      Byte length of key (UTF-8, no null terminator)
asset_type    1      0 = individual sprite (T2D)
                     1 = spritesheet (full atlas replacement)
                     2 = audio clip
pixel_format  1      0 = RGBA32 (raw, uncompressed)
                     1 = DXT5 / BC3
                     2 = raw audio (format described by audio_meta)
width         2      Texture width in pixels  (0 for audio)
height        2      Texture height in pixels (0 for audio)
ppu           4f     Pixels-per-unit (float, sprites only)
pivot_x       4f     Normalised pivot X (float, sprites only)
pivot_y       4f     Normalised pivot Y (float, sprites only)
data_offset   8      Byte offset from start of file to asset data
data_length   4      Byte length of asset data
```

Total per entry: **40 bytes** (fixed) + variable key string in key block.

### Key Format

Keys follow the same conventions as the existing runtime index:

| Asset type | Key format |
|---|---|
| Individual sprite | `CollectionName/MaterialName/SpriteName` |
| Spritesheet | `CollectionName/MaterialName` |
| Audio | `filename-without-extension` (case-insensitive) |

### Data Block

Asset data is written sequentially after the key block, in the same order as entries.
No padding between entries (offsets are used for random access if needed, but the
expected access pattern is sequential top-to-bottom during startup load).

For `pixel_format = 0` (RGBA32): raw row-major RGBA bytes, top-to-bottom.
For `pixel_format = 1` (DXT5): standard BC3 block data, as expected by
`TextureFormat.DXT5` in Unity's `LoadRawTextureData`.
For audio: PCM or pre-encoded OGG/WAV bytes (format TBD in a later version).

---

## Build Step — Pack Manager "Build Pack" button

The packer runs **inside the game** after the creator has verified their pack in dev
mode.  This gives it access to Unity's full API surface and guarantees pixel-perfect
agreement between what the creator sees and what end users load.

### Packer algorithm

```
for each file in pack folder (sprites, spritesheets, audio):
    read PNG bytes
    create Texture2D, LoadImage (PNG decode)
    read back GetRawTextureData() → RGBA32 bytes
    [optional v2: compress to DXT5 via software encoder]
    write entry to index
    write raw bytes to data block
destroy all temporary Texture2D objects
write header + entry table + key block + data block → pack_root/pack.pwpk
```

The packer also writes a **manifest sidecar** (`pack.pwpk.json`) for human inspection:

```json
{
  "format_version": 1,
  "built_at": "2026-03-27T12:00:00Z",
  "patchwork_version": "1.4.0",
  "entry_count": 342,
  "entries": [
    { "key": "HeroCollection/HeroMaterial/knight_idle_01",
      "type": "sprite", "format": "RGBA32", "w": 64, "h": 64 }
  ]
}
```

---

## Load Path — `PackedBundleLoader`

```
PackedBundleLoader.TryOpen(packRoot)
  → if pack.pwpk exists: parse header + entry table → return loader
  → else: return null (caller falls back to dev mode)

loader.LoadAll()
  → sequential read of data block
  → for each sprite entry:
      tex = new Texture2D(w, h, format, false)
      tex.LoadRawTextureData(rawBytes)
      tex.Apply(updateMipmaps: false, makeNoLongerReadable: true)
      sprite = Sprite.Create(tex, ...)
      _loadedSprites[key] = sprite
  → for each audio entry:
      AudioClip via AudioClip.Create or WWW/UnityWebRequest (TBD)

loader.HasEntry(key) → O(1) dictionary lookup
loader.GetSprite(key) → already-created Sprite, no further work
```

### What is skipped entirely in packed mode

- `RebuildFileIndex` / directory scanning
- `FileCache` timestamp checks
- `SpriteFileWatcher` / `TextFileWatcher` startup
- PNG IHDR dimension reads in `T2DSpritesheets`
- `_preloadedBytes` deferred-promotion coroutine (`WarmSprites`)
- Hot-reload condition polling (`PollHotReloadConditions` for packed packs)
- `CheckForUninitializedSprites` sweep (all sprites loaded at scene entry)

---

## Integration Points in Existing Code

| File | Change |
|---|---|
| `handlers/SpriteLoader.cs` | `Reload()`: if `PackedBundleLoader.TryOpen` succeeds, skip `RebuildFileIndex`; `FindSprite`/`FindSpritesheet` delegate to loader |
| `handlers/T2DLoader.cs` | `PreloadAllTextures()`: if packed, skip byte-scan; `ApplyReplacementsInScene` still runs (sprites already in `_loadedSprites`) |
| `handlers/AudioHandler.cs` | `RebuildSoundIndex()`: packed path reads audio entries from loader |
| `Plugin.cs` | Skip `SpriteFileWatcher`/`TextFileWatcher` init for packed packs; skip hot-reload keybind handling |
| `util/PackedBundleLoader.cs` | **New** — format reader + in-memory index |
| `util/PackBuilder.cs` | **New** — invoked by Pack Manager "Build Pack" button |
| `gui/PackManagerWindow.cs` | Show "Build Pack" button for packs in dev mode; show "built on [date]" badge for packed packs |

---

## Version Roadmap

### v1 — RGBA32, sequential load (target)
- Custom `.pwpk` format, RGBA32 raw bytes
- In-game packer via Pack Manager
- Packed packs skip all file watching and index rebuilding
- Manifest sidecar for inspection

### v2 — DXT5/BC3 compression
- Software DXT5 encoder in packer (libsquish or Crunch via P/Invoke, or a pure C# port)
- ~4× GPU memory reduction, sub-millisecond upload per texture
- `pixel_format = 1` entries in existing format (no format version bump needed)

### v3 — Streaming / partial load
- Load only assets referenced in the current scene
- Scene manifest pre-computed at pack time
- Near-zero startup cost, assets stream in during scene load

---

## Non-Goals

- Encryption or DRM (out of scope for a modding tool)
- Asset deduplication across packs (each pack is self-contained)
- Cross-pack atlas merging (complex, deferred indefinitely)
- Replacing Unity's own AssetBundle system (`.pwpk` is intentionally simpler and
  buildable without the Unity Editor)
