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
