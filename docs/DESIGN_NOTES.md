# Patchwork Design Notes

Backlog of discussed features and design decisions, preserved for future implementation.

---

## Pack Encryption / Asset Protection

**Problem:** Bad actors extracting pack assets and re-selling them as paid bundles.

**Core constraint:** Client-side encryption cannot fully prevent extraction — the mod must decrypt in memory to apply assets, and memory is always readable by a determined person. The goal is to raise the bar against casual redistribution, not eliminate the possibility.

### Evaluated options

| Option | What it stops | What it doesn't stop |
|--------|--------------|----------------------|
| A — Key in `pack.json` (AES, `.enc` files) | File-browser copying, automated scrapers | Anyone who opens `pack.json` and writes a decryption script |
| B — Password-protected packs | All of the above + scripted extraction (password not in pack folder) | Someone with the password and a memory dumper |
| C — Embedded attribution (LSB watermarking / audio metadata) | Nothing — detection only | N/A |

### Recommended approach: B + C

**Password-protected packs:**
- Assets stored encrypted (AES-128), key derived from user-supplied password
- Password is NOT included in the pack folder — distributed by author separately (Patreon, Thunderstore DM, etc.)
- User enters password once in the mod UI; stored locally (hashed for verification against a hash in `pack.json`)
- Pack is inert without the correct password
- Closes the primary threat: a lazy bad actor who just copies the folder

**Embedded attribution:**
- Pack GUID + author name baked invisibly into asset data
  - Images: LSB (least-significant-bit) steganography
  - Audio: ID3/metadata tags
- Does not prevent extraction but makes redistribution traceable
- Provides community and legal evidence if redistribution occurs

### Open questions before implementation
1. Should the password be entered once per session or stored persistently (with a local hash)?
2. Should pack authors opt in via a `"encrypted": true` flag in `pack.json`, or is encryption a separate pack-packaging tool?
3. Watermarking scope: images only, or audio too?

---

## Audio Randomization (non-issue)

The game itself requests distinct clip names (`yelp1`, `yelp3`, `yelp5`, etc.) as separate AudioClip objects. The mod replaces each named clip one-to-one. No mod-side randomization is needed or appropriate — the game's own audio system handles selection. Use the AudioLog window to verify which names are requested at runtime.

---

## Conditional Packs — Future Condition Types

Current V1 condition types: `scene` (exact), `scene~` (contains), `pack` (another pack active).

Discussed future extensions:
- Game flags / save data (requires reflection into Silksong internals)
- Player state (health, position) — same caveat
- Time-based conditions (game clock, real-time clock)
- Random / weighted conditions

The `HotReload` trigger mode in `ReloadTrigger` is already wired to poll every ~2 s in `Plugin.Update`, ready for non-scene condition types without further infrastructure changes.

---

## Material Color Tints — Creator Blind Spot

**Observation:** Team Cherry applies per-material color corrections on top of raw sprite textures.
A sprite that is white/grey in the source PNG renders with warm body tint, saturated cloak colour,
and green ambient influence in-game. The raw atlas is unchanged; the transformation lives in the
material's shader properties (`_Color`, brightness/saturation uniforms, possibly a custom TC shader).

**Impact on pack authors:** Patchwork blits the replacement PNG into the atlas RT and sets
`mat.mainTexture` — the same material colour properties then apply on top of the replacement.
A creator working from the dumped PNG sees the untinted original, produces artwork to match it,
and the in-game result looks wrong because the tint shifts their colours in ways they didn't account for.

**What needs investigation:**
1. Which shader properties carry the per-sprite colour correction — `_Color`, a brightness float,
   a custom Team Cherry uniform? Dump `mat.shader.name` and iterate `mat.GetTexturePropertyNames()`
   / `mat.GetFloat` / `mat.GetColor` for a known tinted collection to enumerate them.
2. Whether the tint is on the `tk2dSpriteCollectionData` material, on a per-`SpriteRenderer`
   `MaterialPropertyBlock`, or applied via Unity's lighting/post-processing stack.

**Planned tooling improvements:**
- **Material property readout** in AnimationController: display `mat.color` and any non-default
  colour/float shader uniforms for the current frame's material so creators know what correction is active.
- **Tinted preview**: when rendering the frame preview in AnimationController, blit using the full
  material (not just `mat.mainTexture`) so the tool shows the same result as the game.
- **Dump sidecar**: when dumping a sprite, write a `_material.txt` (or JSON) beside it listing the
  active shader properties, so creators can pre-compensate in their image editor.

**Note:** This is distinct from Patchwork's own atlas RT compositing. The RT blit is linear/unmodified;
the colour shift happens after `mat.mainTexture` is read by the GPU during the render pass.
