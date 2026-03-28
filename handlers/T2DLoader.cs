using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using Patchwork.Util;

namespace Patchwork.Handlers;

/// <summary>
/// Loads, applies, enforces, and hot-reloads T2D sprite replacements.
/// Mirrors the structure of SpriteLoader.
/// Spritesheet-specific methods live in T2DSpritesheets.cs (partial).
/// </summary>
public static partial class T2DLoader
{
    public static string AtlasLoadPath => Path.Combine(SpriteLoader.AtlasLoadPath, "T2D");

    // --- In-place spritesheet replacement (inspired by Customizer T2D) ---
    // Keyed by raw filename (without extension). At runtime, TrySwapTexture looks up
    // by raw texture name first, then falls back to clean name. This naturally handles
    // multiple atlases sharing the same clean name (e.g. sactx-1-2048x2048-BC7-Hornet-*
    // and sactx-0-4096x4096-BC7-Hornet-* are simply two different keys).
    // (Declaration in T2DSpritesheets.cs — owned by the spritesheet partial.)

    // --- Individual sprite replacement (Patchwork's value-add) ---
    // Skin authors can replace individual frames without repacking an atlas.
    // All replacement sprites are created upfront in PreloadAllTextures and stored here.
    // Key = T2DUtil.SpriteKey(cleanTexName, spriteName) for T2D atlas sprites,
    //       spriteName alone for flat/standalone sprites.
    private static readonly Dictionary<string, Sprite>  _loadedSprites    = new();
    // Reverse index: spriteName → loadedSprites key. Built at preload time.
    // Enables O(1) name-based lookup when the renderer holds one of our own replacement
    // sprites (whose texture has no T2D-recognizable name after the first apply).
    private static readonly Dictionary<string, string>  _spriteNameToKey  = new();
    // Maps asset key → source pack path (null = base Patchwork folder). Used for conflict reporting.
    private static readonly Dictionary<string, string>  _t2dProviders     = new();

    // T2D keys that have been logged as missing — suppresses repeat logs per session.
    private static readonly HashSet<string> _t2dMissLogged = new();

    private static readonly Dictionary<int, string> _trackedSpriteNames = new();
    private static readonly HashSet<SpriteRenderer> _knownRenderers = new();
    private static readonly HashSet<Image> _knownImages = new();

    // Cached null-check predicates to avoid per-frame closure allocations in RemoveWhere
    private static readonly System.Predicate<SpriteRenderer> _srNull = sr => sr == null;
    private static readonly System.Predicate<Image> _imgNull = img => img == null;

    private static bool _enforcing;
    private static bool _handling;

    /// <summary>True when any T2D replacement data exists. Gates all expensive per-frame sweeps.</summary>
    public static bool HasT2DReplacements =>
        SpritesheetOverrides.Count > 0 || _loadedSprites.Count > 0;

    /// <summary>True while a harmony setter or enforcement sweep is in progress.</summary>
    public static bool IsHandlingOrEnforcing => _handling || _enforcing;

    // Read-only stats for GUI
    public static int SpritesheetOverrideCount => SpritesheetOverrides.Count;
    public static int LoadedT2DSpriteCount => _loadedSprites.Count;
    public static int TrackedRendererCount => _knownRenderers.Count + _knownImages.Count;
    public static IEnumerable<string> LoadedT2DSpriteNames => _loadedSprites.Keys;
    public static IEnumerable<string> SpritesheetOverrideNames => SpritesheetOverrides.Keys;

    // ================================================================
    //  Entry points called from T2DHandler harmony patches
    // ================================================================

    public static void OnSpriteSet(SpriteRenderer sr, Sprite value)
    {
        _trackedSpriteNames[sr.GetInstanceID()] = value.name;
        if (value.texture != null && !HasIndividualReplacement(value))
            TrySwapTexture(value.texture);
        HandleLoad(sr, value);
    }

    public static void OnSpriteSet(Image img, Sprite value)
    {
        _trackedSpriteNames[img.GetInstanceID()] = value.name;
        if (value.texture != null && !HasIndividualReplacement(value))
            TrySwapTexture(value.texture);
        HandleLoad(img, value);
    }

    /// <summary>
    /// Returns true when an individual sprite replacement exists for this sprite in _loadedSprites.
    /// Used to skip the in-place atlas swap for sprites that have their own PNG.
    /// </summary>
    private static bool HasIndividualReplacement(Sprite sprite)
    {
        if (sprite.texture == null || !T2DUtil.IsT2DTexture(sprite.texture.name))
            return false;
        string key = T2DUtil.SpriteKey(T2DUtil.CleanTextureName(sprite.texture.name), sprite.name);
        return _loadedSprites.ContainsKey(key);
    }

    // ================================================================
    //  Scene load and preloading
    // ================================================================

    public static void ApplyReplacementsInScene()
    {
        // Pass 1: In-place texture replacement for spritesheets.
        if (SpritesheetOverrides.Count > 0)
        {
            foreach (var tex in Resources.FindObjectsOfTypeAll<Texture2D>())
            {
                if (tex != null)
                    TrySwapTexture(tex);
            }
        }

        // Pass 2: Apply individual sprite replacements to all renderers/images (including inactive).
        // All replacement Sprite objects are already in _loadedSprites — no staging or promotion needed.
        if (!HasT2DReplacements)
            return;

        int srCount = 0, imgCount = 0;
        foreach (var sr in Resources.FindObjectsOfTypeAll<SpriteRenderer>())
        {
            if (sr == null || sr.sprite == null) continue;
            srCount++;
            HandleLoad(sr, sr.sprite);
        }
        foreach (var img in Resources.FindObjectsOfTypeAll<Image>())
        {
            if (img == null || img.sprite == null) continue;
            imgCount++;
            HandleLoad(img, img.sprite);
        }
    }

    public static void PreloadAllTextures()
    {
        BuildSpritesheetOverrides();

        void ScanDirectory(string t2dRoot, string sourcePack)
        {
            Plugin.Logger.LogInfo($"[T2D-Trace] ScanDirectory: '{t2dRoot}' exists={Directory.Exists(t2dRoot)}");
            if (!Directory.Exists(t2dRoot))
                return;

            int added = 0;

            Sprite CreateAndStore(string key, string spriteName, string file)
            {
                if (_loadedSprites.ContainsKey(key))
                {
                    ConflictTracker.Record("t2d-sprite", key,
                        _t2dProviders.GetValueOrDefault(key), sourcePack);
                    return null;
                }
                byte[] bytes = TexUtil.ReadBytesFromPNG(file);
                if (bytes == null) return null;
                Texture2D tex = TexUtil.CreateTextureFromBytes(bytes);
                if (tex == null) return null;
                // Mark texture so TrySwapTexture never confuses it with a full-atlas spritesheet
                // (sprite-sized texture shares the atlas name but would fail the size check).
                SkippedTextureIds.Add(tex.GetInstanceID());
                Sprite sprite = Sprite.Create(tex,
                    new Rect(0, 0, tex.width, tex.height),
                    new Vector2(0.5f, 0.5f), 100f);
                sprite.name = spriteName;
                sprite.hideFlags = HideFlags.DontUnloadUnusedAsset;
                _loadedSprites[key] = sprite;
                _t2dProviders[key] = sourcePack;
                // Reverse index so HandleLoad can find atlas-qualified keys from sprite name alone
                // (needed when a renderer holds our own replacement sprite whose texture name is
                // no longer a T2D atlas identifier — IsT2DTexture returns false on second pass).
                if (!_spriteNameToKey.ContainsKey(spriteName))
                    _spriteNameToKey[spriteName] = key;
                return sprite;
            }

            // Structured path: T2D/[AtlasName]/[SpriteName].png
            // Special case: T2D/_standalone/[SpriteName].png — for sprites on textures that
            // don't pass IsT2DTexture. Stored under a flat key so HandleLoad finds them.
            foreach (var atlasDir in Directory.GetDirectories(t2dRoot))
            {
                string atlasName = Path.GetFileName(atlasDir);
                bool isStandalone = atlasName.Equals("_standalone", System.StringComparison.OrdinalIgnoreCase);
                foreach (var file in Directory.GetFiles(atlasDir, "*.png"))
                {
                    string spriteName = Path.GetFileNameWithoutExtension(file);
                    string key = isStandalone ? spriteName : T2DUtil.SpriteKey(atlasName, spriteName);
                    if (CreateAndStore(key, spriteName, file) != null)
                        added++;
                }
            }

            // Flat path: T2D/[SpriteName].png — fallback when no atlas-qualified match exists.
            foreach (var file in Directory.GetFiles(t2dRoot, "*.png"))
            {
                string spriteName = Path.GetFileNameWithoutExtension(file);
                if (CreateAndStore(spriteName, spriteName, file) != null)
                    added++;
            }

            Plugin.Logger.LogInfo($"[T2D-Trace] ScanDirectory: added {added} key(s) from '{t2dRoot}'");
        }

        var packPaths = Plugin.PluginPackPaths.ToList();
        Plugin.Logger.LogInfo($"[T2D-Trace] PreloadAllTextures: {packPaths.Count} active pack path(s)");
        foreach (var pp in packPaths)
            Plugin.Logger.LogInfo($"[T2D-Trace]   pack path: '{pp}'");

        ScanDirectory(Path.Combine(SpriteLoader.LoadPath, "T2D"), null);
        foreach (var packPath in packPaths)
            ScanDirectory(Path.Combine(packPath, "Sprites", "T2D"), packPath);

        Plugin.Logger.LogInfo($"[T2D-Trace] PreloadAllTextures done: {_loadedSprites.Count} sprite(s) ready");
    }

    // ================================================================
    //  Individual sprite loading (HandleLoad)
    // ================================================================

    internal static void HandleLoad(object spriteContainer, Sprite sprite)
    {
        if (_handling || !HasT2DReplacements)
            return;
        if (sprite?.texture == null)
            return;

        _handling = true;
        try
        {
            Sprite replacement = null;

            if (T2DUtil.IsT2DTexture(sprite.texture.name))
            {
                // Primary path: atlas-qualified key lookup.
                string cleanTexName = T2DUtil.CleanTextureName(sprite.texture.name);
                string key = T2DUtil.SpriteKey(cleanTexName, sprite.name);

                if (_loadedSprites.TryGetValue(key, out var cached) && cached != null && cached.texture != null)
                {
                    // Lazily match the replacement sprite's PPU to the game's native PPU on first
                    // encounter. Preload uses 100f as a placeholder; the real value comes from the
                    // first intercepted sprite setter where we have the live atlas sprite in hand.
                    // We use sprite.pixelsPerUnit directly — pack authors control visual size through
                    // PNG dimensions, not by matching the original atlas sub-rect world size.
                    if (System.Math.Abs(cached.pixelsPerUnit - sprite.pixelsPerUnit) > 0.5f)
                    {
                        var corrected = Sprite.Create(cached.texture,
                            new Rect(0, 0, cached.texture.width, cached.texture.height),
                            new Vector2(0.5f, 0.5f), sprite.pixelsPerUnit);
                        corrected.name = cached.name;
                        corrected.hideFlags = HideFlags.DontUnloadUnusedAsset;
                        Object.Destroy(cached);
                        _loadedSprites[key] = corrected;
                        cached = corrected;
                    }
                    replacement = cached;
                }
                else if (_t2dMissLogged.Add(key))
                    Plugin.Logger.LogInfo(
                        $"[T2D-Miss] sprite='{sprite.name}' tex='{cleanTexName}' " +
                        $"key='{key}' loaded={_loadedSprites.Count}");
                // Spritesheet replacement is handled by TrySwapTexture in-place.
            }

            // Name-index fallback: handles two cases:
            //   1. Non-T2D texture (flat layout or _standalone sprites)
            //   2. Our own replacement sprites on the renderer — their texture has no T2D-style
            //      name so IsT2DTexture returns false; the T2D branch above was skipped entirely.
            //      _spriteNameToKey maps sprite.name → atlas-qualified key so we find the right
            //      replacement even on subsequent pack-switches or vanilla-revert attempts.
            if (replacement == null && _spriteNameToKey.TryGetValue(sprite.name, out var fallbackKey)
                && _loadedSprites.TryGetValue(fallbackKey, out var fallback) && fallback != null && fallback.texture != null)
            {
                replacement = fallback;
            }

            if (replacement != null)
            {
                SetSprite(spriteContainer, replacement);
                TrackContainer(spriteContainer);
            }
        }
        finally
        {
            _handling = false;
        }
    }

    private static void SetSprite(object container, Sprite sprite)
    {
        if (container is SpriteRenderer sr)
            sr.sprite = sprite;
        else if (container is Image img)
            img.sprite = sprite;
    }

    private static void TrackContainer(object container)
    {
        if (container is SpriteRenderer sr)
            _knownRenderers.Add(sr);
        else if (container is Image img)
            _knownImages.Add(img);
    }

    // ================================================================
    //  Periodic enforcement
    // ================================================================

    public static void CheckForUninitializedSprites()
    {
        if (!HasT2DReplacements)
            return;

        int triggered = 0;
        foreach (var sr in Object.FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None))
        {
            if (sr == null || sr.sprite == null) continue;
            CheckSprite(sr.GetInstanceID(), sr.sprite, out bool changed, out _);
            if (changed)
            {
                sr.sprite = sr.sprite;
                triggered++;
            }
        }

        foreach (var img in Object.FindObjectsByType<Image>(FindObjectsSortMode.None))
        {
            if (img == null || img.sprite == null) continue;
            CheckSprite(img.GetInstanceID(), img.sprite, out bool changed, out _);
            if (changed)
            {
                img.sprite = img.sprite;
                triggered++;
            }
        }

        if (triggered > 0)
            Plugin.Logger.LogInfo($"[T2D] CheckForUninitializedSprites: caught {triggered} uninitialized renderer(s)");
    }

    public static void EnforceT2DReplacements()
    {
        if (!HasT2DReplacements)
            return;

        _enforcing = true;
        try
        {
            foreach (var sr in _knownRenderers)
            {
                if (sr == null || sr.sprite == null) continue;
                if (TryGetReplacement(sr.sprite, out var replacement) && sr.sprite != replacement)
                    sr.sprite = replacement;
            }

            foreach (var img in _knownImages)
            {
                if (img == null || img.sprite == null) continue;
                if (TryGetReplacement(img.sprite, out var replacement) && img.sprite != replacement)
                    img.sprite = replacement;
            }

            // Clean up destroyed references
            _knownRenderers.RemoveWhere(_srNull);
            _knownImages.RemoveWhere(_imgNull);
        }
        finally
        {
            _enforcing = false;
        }
    }

    /// <summary>Checks if a sprite has a replacement loaded but is not yet showing it.</summary>
    private static void CheckSprite(int instanceId, Sprite sprite, out bool nameChanged, out bool replacementMissing)
    {
        string key = KeyForSprite(sprite);
        if (!_loadedSprites.ContainsKey(key))
        {
            nameChanged = false;
            replacementMissing = false;
            return;
        }

        nameChanged = !_trackedSpriteNames.TryGetValue(instanceId, out string last) || last != sprite.name;
        replacementMissing = !nameChanged && _loadedSprites.TryGetValue(key, out var cached) && sprite != cached;

        if (nameChanged || replacementMissing)
            _trackedSpriteNames[instanceId] = sprite.name;
    }

    private static bool TryGetReplacement(Sprite sprite, out Sprite replacement)
        => _loadedSprites.TryGetValue(KeyForSprite(sprite), out replacement) && replacement != null;

    private static string KeyForSprite(Sprite sprite)
        => (sprite.texture != null && T2DUtil.IsT2DTexture(sprite.texture.name))
            ? T2DUtil.SpriteKey(T2DUtil.CleanTextureName(sprite.texture.name), sprite.name)
            : sprite.name;

    // ================================================================
    //  Scene-transition cleanup
    // ================================================================

    /// <summary>
    /// Clears per-scene transient state after a scene unloads.
    /// - Texture instance ID sets are flushed: IDs are recycled by Unity across scenes,
    ///   so a stale "skipped" entry could silently suppress a valid replacement next scene.
    /// - Dead instance IDs in _trackedSpriteNames are pruned to prevent unbounded growth.
    /// Call on SceneManager.sceneUnloaded alongside PruneStaleOriginals.
    /// </summary>
    public static void PruneSceneState()
    {
        // Clear texture ID sets — scene textures are about to be destroyed and their
        // IDs may be reused. Force fresh evaluation next scene.
        ReplacedTextureIds.Clear();
        SkippedTextureIds.Clear();

        // Re-protect replacement sprite textures that are still alive in _loadedSprites.
        // These are sprite-sized Texture2D objects named after their parent atlas; without
        // this they'd be matched by TrySwapTexture on the next ApplyReplacementsInScene
        // Pass 1 and produce spurious size-mismatch warnings.
        foreach (var sprite in _loadedSprites.Values)
            if (sprite?.texture != null)
                SkippedTextureIds.Add(sprite.texture.GetInstanceID());

        // Remove dead SpriteRenderer/Image instance IDs from the name-tracking dict.
        // These accumulate as enemies/objects are destroyed during gameplay.
        if (_trackedSpriteNames.Count > 0)
        {
            var deadKeys = new List<int>();
            foreach (var id in _trackedSpriteNames.Keys)
            {
                bool isLive = false;
                foreach (var sr in _knownRenderers)
                    if (sr != null && sr.GetInstanceID() == id) { isLive = true; break; }
                if (!isLive)
                    foreach (var img in _knownImages)
                        if (img != null && img.GetInstanceID() == id) { isLive = true; break; }
                if (!isLive) deadKeys.Add(id);
            }
            foreach (var k in deadKeys) _trackedSpriteNames.Remove(k);
            if (deadKeys.Count > 0)
                Plugin.Logger.LogInfo($"[T2D] Pruned {deadKeys.Count} stale tracked name(s) after scene unload");
        }
    }

    // ================================================================
    //  Hot reload and cache invalidation
    // ================================================================

    public static void ReloadSpritesInScene()
    {
        Plugin.Logger.LogInfo($"[T2D-Reload] Starting hot reload. " +
            $"Pre-reload state: {_loadedSprites.Count} loaded sprites, " +
            $"{SpritesheetOverrides.Count} spritesheet overrides");

        // Save old sprites for deferred cleanup — do NOT destroy yet,
        // because renderers still reference these sprites. Destroying now would leave
        // renderers with null sprites, causing them to be skipped during re-apply.
        var oldSprites = new List<Sprite>(_loadedSprites.Values);

        // Clear all caches
        _loadedSprites.Clear();
        _spriteNameToKey.Clear();
        _t2dProviders.Clear();
        ReplacedTextureIds.Clear();
        SkippedTextureIds.Clear();
        T2DUtil.ClearCleanNameCache();

        // Clearing SkippedTextureIds removed protection from old sprite-sized textures that are
        // still alive (referenced by oldSprites). Their names match atlas names in SpritesheetOverrides
        // so TrySwapTexture would find them, fail the size check, and log spurious warnings.
        // Re-add them now so they're skipped during the spritesheet swap below.
        foreach (var sprite in oldSprites)
            if (sprite?.texture != null)
                SkippedTextureIds.Add(sprite.texture.GetInstanceID());

        // Clear tracking state so stale renderer/sprite associations from the old pack
        // cannot interfere with the new one.  Renderers will be re-tracked as HandleLoad
        // processes them in the sweep below.
        _knownRenderers.Clear();
        _knownImages.Clear();
        _trackedSpriteNames.Clear();
        _t2dMissLogged.Clear();

        // Rebuild everything from disk — PreloadAllTextures creates all replacement sprites upfront.
        PreloadAllTextures();
        Plugin.Logger.LogInfo($"[T2D-Reload] After PreloadAllTextures: " +
            $"{SpritesheetOverrides.Count} spritesheet overrides, " +
            $"{_loadedSprites.Count} loaded sprites");

        // Re-apply in-place texture swaps; also restore vanilla pixels for any texture whose
        // pack was just disabled (HasStoredOriginals is true while any originals are pending).
        if (SpritesheetOverrides.Count > 0 || HasStoredOriginals)
        {
            int swapCount = 0, restoreCount = 0;
            foreach (var tex in Resources.FindObjectsOfTypeAll<Texture2D>())
            {
                if (tex == null) continue;
                // Sprite-sized textures (from oldSprites) share their atlas name as the key in
                // _originalTextureData. If we let TryRestoreTexture run on them it will consume
                // the full-atlas PNG entry, leaving the real atlas un-restorable. Guard with the
                // preSkipped flag: anything we intentionally skipped is not a candidate for restore.
                bool preSkipped = SkippedTextureIds.Contains(tex.GetInstanceID());
                if (TrySwapTexture(tex))
                    swapCount++;
                else if (!preSkipped && TryRestoreTexture(tex))
                    restoreCount++;
            }
            Plugin.Logger.LogInfo($"[T2D-Reload] Swapped {swapCount} textures via spritesheets, restored {restoreCount} to vanilla");

            // If no spritesheet overrides remain active, TryRestoreTexture has written vanilla
            // pixels back into all previously-replaced atlases. The stored PNG bytes are no longer
            // needed and would otherwise persist in memory until the next scene unload (PruneStaleOriginals
            // skips live textures). Clear them now so the memory is reclaimed immediately.
            if (SpritesheetOverrides.Count == 0)
            {
                int freed = _originalTextureData.Count;
                _originalTextureData.Clear();
                if (freed > 0)
                    Plugin.Logger.LogInfo($"[T2D-Reload] Freed {freed} stored original(s) — no active spritesheet overrides");
            }
        }

        // Re-apply individual sprite replacements to all renderers
        int srCount = 0, srLoaded = 0;
        foreach (var sr in Resources.FindObjectsOfTypeAll<SpriteRenderer>())
        {
            if (sr == null || sr.sprite == null || sr.sprite.texture == null) continue;
            srCount++;
            int prev = _loadedSprites.Count;
            HandleLoad(sr, sr.sprite);
            if (_loadedSprites.Count > prev) srLoaded++;
        }
        int imgCount = 0, imgLoaded = 0;
        foreach (var img in Resources.FindObjectsOfTypeAll<Image>())
        {
            if (img == null || img.sprite == null || img.sprite.texture == null) continue;
            imgCount++;
            int prev = _loadedSprites.Count;
            HandleLoad(img, img.sprite);
            if (_loadedSprites.Count > prev) imgLoaded++;
        }
        Plugin.Logger.LogInfo($"[T2D-Reload] HandleLoad pass: " +
            $"{srCount} SpriteRenderers ({srLoaded} new loads), " +
            $"{imgCount} Images ({imgLoaded} new loads)");

        // Any renderer that still holds an old replacement sprite after the HandleLoad sweep
        // above was not updated (no new replacement exists for it). Revert it to the vanilla
        // sprite before we destroy old sprites — otherwise the destroyed textures leave those
        // renderers with null references → black silhouettes.
        // We do NOT gate on !HasT2DReplacements: base-path T2D files always present can keep
        // HasT2DReplacements true even when the user's pack is fully disabled, which would
        // wrongly skip the entire revert pass. Instead, check per-renderer: if the renderer
        // still holds an old sprite, HandleLoad didn't replace it → needs vanilla revert.
        if (oldSprites.Count > 0)
        {
            var oldSpriteSet = new HashSet<Sprite>(oldSprites);

            // Index all live sprites that are NOT our replacements, keyed by name.
            var vanillaByName = new Dictionary<string, Sprite>(System.StringComparer.OrdinalIgnoreCase);
            int totalLiveSprites = 0;
            foreach (var s in Resources.FindObjectsOfTypeAll<Sprite>())
            {
                totalLiveSprites++;
                if (s != null && !oldSpriteSet.Contains(s) && !vanillaByName.ContainsKey(s.name))
                    vanillaByName[s.name] = s;
            }
            int revertCount = 0, missCount = 0;
            // Guard against the Harmony postfixes (OnSpriteSet → TrySwapTexture + HandleLoad)
            // re-entering during sprite assignment. We handle the lookup explicitly here.
            _enforcing = true;
            try
            {
                foreach (var sr in Resources.FindObjectsOfTypeAll<SpriteRenderer>())
                {
                    if (sr == null || sr.sprite == null) continue;
                    if (!oldSpriteSet.Contains(sr.sprite)) continue; // HandleLoad already updated this one
                    // Prefer a freshly-loaded individual replacement; fall back to vanilla.
                    Sprite target = _loadedSprites.TryGetValue(KeyForSprite(sr.sprite), out var cached) ? cached
                        : vanillaByName.TryGetValue(sr.sprite.name, out var v) ? v : null;
                    if (target != null) { sr.sprite = target; revertCount++; }
                    else { Plugin.Logger.LogWarning($"[T2D-Revert] SpriteRenderer '{sr.name}': old sprite '{sr.sprite.name}' has no replacement or vanilla match"); missCount++; }
                }
                foreach (var img in Resources.FindObjectsOfTypeAll<Image>())
                {
                    if (img == null || img.sprite == null) continue;
                    if (!oldSpriteSet.Contains(img.sprite)) continue; // HandleLoad already updated this one
                    Sprite target = _loadedSprites.TryGetValue(KeyForSprite(img.sprite), out var cached) ? cached
                        : vanillaByName.TryGetValue(img.sprite.name, out var v) ? v : null;
                    if (target != null) { img.sprite = target; revertCount++; }
                    else { Plugin.Logger.LogWarning($"[T2D-Revert] Image '{img.name}': old sprite '{img.sprite.name}' has no replacement or vanilla match"); missCount++; }
                }
            }
            finally { _enforcing = false; }
            if (missCount > 0)
                Plugin.Logger.LogWarning($"[T2D-Reload] Revert sweep: {revertCount} reverted, {missCount} missed");
        }

        // NOW destroy old sprites — renderers have been updated with new replacements
        foreach (var sprite in oldSprites)
        {
            if (sprite != null && sprite.texture != null)
                Object.Destroy(sprite.texture);
            if (sprite != null)
                Object.Destroy(sprite);
        }

        Plugin.Logger.LogInfo($"[T2D-Reload] Reload complete. " +
            $"Post-reload state: {_loadedSprites.Count} loaded sprites. " +
            $"Destroyed {oldSprites.Count} old sprites");
    }


    public static void InvalidateSpritesheet(string texName)
    {
        // NOTE: May be called from the file watcher's background thread.
        // Do NOT call Object.Destroy here — only clear caches.
        string cleanName = T2DUtil.CleanTextureName(texName);
        Plugin.Logger.LogInfo($"[T2D-Invalidate] InvalidateSpritesheet('{texName}') → clean='{cleanName}'. " +
            $"Before: {SpritesheetOverrides.Count} overrides, {_loadedSprites.Count} loaded sprites");

        SpritesheetOverrides.Remove(texName);
        ReplacedTextureIds.Clear();
        SkippedTextureIds.Clear();

        // Remove all individual sprites whose key starts with "cleanName/" (atlas-qualified keys).
        int removedSprites = 0;
        string prefix = cleanName + "/";
        foreach (var key in _loadedSprites.Keys.Where(k => k.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase)).ToList())
        {
            _loadedSprites.Remove(key);
            removedSprites++;
        }
        Plugin.Logger.LogInfo($"[T2D-Invalidate] InvalidateSpritesheet done. Removed {removedSprites} loaded sprites. " +
            $"After: {SpritesheetOverrides.Count} overrides, {_loadedSprites.Count} loaded sprites");
    }

    public static void InvalidateCache(string spriteName, string atlasName = null)
    {
        // NOTE: May be called from the file watcher's background thread.
        // Do NOT call Object.Destroy here — only clear caches.
        Plugin.Logger.LogInfo($"[T2D-Invalidate] InvalidateCache('{spriteName}', atlas='{atlasName}')");

        if (atlasName != null)
        {
            _loadedSprites.Remove(T2DUtil.SpriteKey(atlasName, spriteName));
        }
        else
        {
            foreach (var key in _loadedSprites.Keys.Where(k => k == spriteName || k.EndsWith($"/{spriteName}")).ToList())
                _loadedSprites.Remove(key);
        }
        _spriteNameToKey.Remove(spriteName);
    }
}
