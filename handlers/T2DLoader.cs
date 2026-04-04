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

    /// <summary>
    /// Set by GraphicsPillar when the T2D or T2D-Log sub-tab is visible.
    /// Gates all trigger-logging work; a single bool check in the hot setter path.
    /// Reset to false at the top of Plugin.OnGUI so it goes dark when DevHub closes.
    /// </summary>
    public static bool IsT2DLogActive;

    /// <summary>
    /// Fired (main thread, only when <see cref="IsT2DLogActive"/>) on every sprite or
    /// T2D-texture setter invocation.  Args: (cleanTexName, spriteName-or-empty).
    /// </summary>
    public static event System.Action<string, string> OnT2DTrigger;

    /// <summary>Raises OnT2DTrigger. Call only when IsT2DLogActive is true.</summary>
    public static void RaiseT2DTrigger(string cleanTexName, string spriteName)
        => OnT2DTrigger?.Invoke(cleanTexName, spriteName);

    // Read-only stats for GUI
    public static int SpritesheetOverrideCount => SpritesheetOverrides.Count;
    public static int LoadedT2DSpriteCount => _loadedSprites.Count;
    public static int TrackedRendererCount => _knownRenderers.Count + _knownImages.Count;
    public static IEnumerable<string> LoadedT2DSpriteNames => _loadedSprites.Keys;
    public static IEnumerable<string> SpritesheetOverrideNames => SpritesheetOverrides.Keys;

    /// <summary>
    /// Unconditionally adds a renderer to the tracking set so the T2D browser
    /// can enumerate it even when no replacement pack is loaded.
    /// </summary>
    public static void TrackRenderer(SpriteRenderer sr) { if (sr != null) _knownRenderers.Add(sr); }
    public static void TrackImage(UnityEngine.UI.Image img) { if (img != null) _knownImages.Add(img); }

    // Cached Sprite[] from the scene seed sweep — used for sprite-list enrichment and
    // FindSceneSprite fallback. Avoids repeated FindObjectsOfTypeAll<Sprite> calls.
    private static Sprite[] _seededSprites = System.Array.Empty<Sprite>();

    // FindSceneSprite result cache — keyed by "cleanTexName/spriteName".
    // Cleared when the scene seed resets (scene unload).
    private static readonly Dictionary<string, Sprite> _spriteHighlightCache =
        new(System.StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Seeds _knownRenderers/_knownImages and caches all Sprite objects in the scene.
    /// Runs once per scene on first browser open; catches sprites assigned before
    /// Harmony patches fired.
    /// </summary>
    private static bool _sceneSeedDone;
    public static void SeedFromScene()
    {
        if (_sceneSeedDone) return;
        _sceneSeedDone = true;
        foreach (var sr in Resources.FindObjectsOfTypeAll<SpriteRenderer>())
            if (sr != null && sr.sprite?.texture != null) _knownRenderers.Add(sr);
        foreach (var img in Resources.FindObjectsOfTypeAll<UnityEngine.UI.Image>())
            if (img != null && img.sprite?.texture != null) _knownImages.Add(img);
        // Cache all Sprite assets in one sweep — reused for enrichment and UV highlight lookup.
        _seededSprites = Resources.FindObjectsOfTypeAll<Sprite>();
    }

    public static void ResetSceneSeed()
    {
        _sceneSeedDone = false;
        _seededSprites = System.Array.Empty<Sprite>();
        _spriteHighlightCache.Clear();
        // Prune destroyed renderers/images so the sets don't grow unboundedly across scenes.
        _knownRenderers.RemoveWhere(_srNull);
        _knownImages.RemoveWhere(_imgNull);
    }

    // ================================================================
    //  Scene texture snapshot for the T2D browser GUI
    // ================================================================

    /// <summary>
    /// Snapshot of every distinct texture visible via tracked renderers and images,
    /// ready for the T2DTextureController browser.  Called at most once per 2 s
    /// (callers enforce the cooldown).
    /// </summary>
    public static List<T2DSceneEntry> GetSceneTextureEntries()
    {
        var spritesByTexId = new Dictionary<int, List<string>>();
        var texById        = new Dictionary<int, Texture>();

        void Accumulate(Sprite sprite)
        {
            if (sprite?.texture == null) return;
            var tex = sprite.texture;
            int id  = tex.GetInstanceID();
            if (!texById.ContainsKey(id)) { texById[id] = tex; spritesByTexId[id] = new List<string>(); }
            var names = spritesByTexId[id];
            if (!names.Contains(sprite.name)) names.Add(sprite.name);
        }

        SeedFromScene();
        foreach (var sr in _knownRenderers) { if (sr != null) Accumulate(sr.sprite); }
        foreach (var img in _knownImages)   { if (img != null) Accumulate(img.sprite); }

        // Deduplicate by clean name: the game can hold multiple Texture2D instances for the
        // same logical atlas (e.g. Inventory loaded once per renderer). Merge them so the
        // browser shows one entry per atlas, not one per renderer instance.
        var byCleanName = new Dictionary<string, (Texture tex, string rawName, List<string> sprites, bool isT2D)>(
            System.StringComparer.OrdinalIgnoreCase);

        foreach (var kvp in texById)
        {
            var    tex    = kvp.Value;
            bool   isT2D  = T2DUtil.IsT2DTexture(tex.name);
            string clean  = isT2D ? T2DUtil.CleanTextureName(tex.name) : tex.name;
            var    names  = spritesByTexId[kvp.Key];

            if (byCleanName.TryGetValue(clean, out var existing))
            {
                // Always merge sprite names into the existing list (it's a reference, mutations persist).
                foreach (var s in names)
                    if (!existing.sprites.Contains(s)) existing.sprites.Add(s);
                // Prefer the LARGEST texture — multiple Texture2D instances can share a clean name
                // (e.g. Inventory: one per-renderer copy and one full atlas). The full atlas has the
                // most pixels; keeping it ensures the browser preview shows the atlas, not a crop.
                if (tex.width * tex.height > existing.tex.width * existing.tex.height)
                    byCleanName[clean] = (tex, tex.name, existing.sprites, existing.isT2D);
            }
            else
            {
                byCleanName[clean] = (tex, tex.name, names, isT2D);
            }
        }

        // ── Enrichment pass 1: Unity Sprite assets (clean-name keyed) ────────
        // Done AFTER byCleanName is built so we match by atlas identity, not by
        // texture instance ID. Sprites that reference a different Texture2D instance
        // of the same logical atlas (common when the game loads one copy per renderer)
        // are correctly attributed here instead of being silently skipped.
        foreach (var sprite in _seededSprites)
        {
            if (sprite?.texture == null) continue;
            bool   sIsT2D = T2DUtil.IsT2DTexture(sprite.texture.name);
            string sClean = sIsT2D ? T2DUtil.CleanTextureName(sprite.texture.name) : sprite.texture.name;
            if (!byCleanName.TryGetValue(sClean, out var entry)) continue;
            if (!entry.sprites.Contains(sprite.name)) entry.sprites.Add(sprite.name);
            // Opportunistically upgrade to the larger texture instance.
            if (sprite.texture.width * sprite.texture.height > entry.tex.width * entry.tex.height)
                byCleanName[sClean] = (sprite.texture, sprite.texture.name, entry.sprites, entry.isT2D);
        }

        // ── Enrichment pass 2: tk2d SpriteCollectionData ─────────────────────
        // tk2d stores sprite names in SpriteDefinition arrays, not as Unity Sprite
        // objects — FindObjectsOfTypeAll<Sprite> is completely blind to them. If a
        // tk2d atlas texture appears in the browser (e.g. via a material setter or a
        // Unity SpriteRenderer that shares the same texture), this pass fills in all
        // the frame names from the collection definition.
        foreach (var coll in Resources.FindObjectsOfTypeAll<tk2dSpriteCollectionData>())
        {
            if (coll?.spriteDefinitions == null) continue;
            foreach (var def in coll.spriteDefinitions)
            {
                if (def?.material?.mainTexture == null || string.IsNullOrEmpty(def.name)) continue;
                string texName = def.material.mainTexture.name;
                bool   dIsT2D  = T2DUtil.IsT2DTexture(texName);
                string dClean  = dIsT2D ? T2DUtil.CleanTextureName(texName) : texName;
                if (!byCleanName.TryGetValue(dClean, out var entry)) continue;
                if (!entry.sprites.Contains(def.name)) entry.sprites.Add(def.name);
            }
        }

        var result = new List<T2DSceneEntry>(byCleanName.Count);
        foreach (var kvp in byCleanName)
        {
            var (tex, rawName, sprites, isT2D) = kvp.Value;
            string clean  = kvp.Key;
            bool hasSheet = SpritesheetOverrides.ContainsKey(rawName)
                         || SpritesheetOverrides.ContainsKey(clean);
            string prefix = clean + "/";
            int loaded    = 0;
            foreach (var k in _loadedSprites.Keys)
                if (k.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase)) loaded++;

            result.Add(new T2DSceneEntry(clean, rawName, tex, isT2D, sprites, hasSheet, loaded));
        }

        result.Sort((a, b) =>
        {
            if (a.IsT2D != b.IsT2D) return a.IsT2D ? -1 : 1;
            return string.Compare(a.CleanName, b.CleanName, System.StringComparison.OrdinalIgnoreCase);
        });
        return result;
    }

    /// <summary>
    /// Returns the first Sprite in the scene whose name and atlas clean name match
    /// the given parameters.  Used by T2DTextureController to get UV data for preview.
    /// </summary>
    public static Sprite FindSceneSprite(string cleanTexName, string spriteName)
    {
        string cacheKey = cleanTexName + "/" + spriteName;
        if (_spriteHighlightCache.TryGetValue(cacheKey, out var cached))
            return cached;

        // Search live renderers first (fastest path for currently-visible sprites)
        foreach (var sr in _knownRenderers)
        {
            var s = sr?.sprite;
            if (s == null) continue;
            if (s.name == spriteName && T2DUtil.CleanTextureName(s.texture?.name ?? "") == cleanTexName)
            { _spriteHighlightCache[cacheKey] = s; return s; }
        }
        foreach (var img in _knownImages)
        {
            var s = img?.sprite;
            if (s == null) continue;
            if (s.name == spriteName && T2DUtil.CleanTextureName(s.texture?.name ?? "") == cleanTexName)
            { _spriteHighlightCache[cacheKey] = s; return s; }
        }

        // Fall back to the seed cache — covers enriched sprites not on any live renderer
        // (animation frames, inactive prefabs, etc.)
        foreach (var s in _seededSprites)
        {
            if (s == null || s.texture == null) continue;
            if (s.name == spriteName && T2DUtil.CleanTextureName(s.texture.name) == cleanTexName)
            { _spriteHighlightCache[cacheKey] = s; return s; }
        }

        return null;
    }

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

        foreach (var sr in Resources.FindObjectsOfTypeAll<SpriteRenderer>())
        {
            if (sr == null || sr.sprite == null) continue;
            HandleLoad(sr, sr.sprite);
        }
        foreach (var img in Resources.FindObjectsOfTypeAll<Image>())
        {
            if (img == null || img.sprite == null) continue;
            HandleLoad(img, img.sprite);
        }

        // Pass 3: Pre-correct PPU and stamp atlas names for ALL vanilla sprites in memory,
        // including those on inactive/unspawned objects. Mirrors T2DHandler Pass 2 from the
        // original Ashiepaws implementation. Ensures effect sprites and other rarely-seen
        // sprites have the correct PPU before they're first rendered.
        PreCorrectAllPPU();
    }

    public static void PreloadAllTextures()
    {
        BuildSpritesheetOverrides();

        void ScanDirectory(string t2dRoot, string sourcePack)
        {
            if (!Directory.Exists(t2dRoot))
                return;

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
                        CreateAndStore(key, spriteName, file);
                }
            }

            // Flat path: T2D/[SpriteName].png — fallback when no atlas-qualified match exists.
            foreach (var file in Directory.GetFiles(t2dRoot, "*.png"))
            {
                string spriteName = Path.GetFileNameWithoutExtension(file);
                CreateAndStore(spriteName, spriteName, file);
            }
        }

        ScanDirectory(Path.Combine(SpriteLoader.LoadPath, "T2D"), null);
        foreach (var packPath in Plugin.PluginPackPaths)
            ScanDirectory(Path.Combine(packPath, "Sprites", "T2D"), packPath);
    }

    // ================================================================
    //  Individual sprite loading (HandleLoad)
    // ================================================================

    /// <summary>
    /// Returns the cached sprite with its PPU matched to <paramref name="targetPPU"/>.
    /// On the first encounter the preloaded 100f placeholder is re-created at the game's
    /// native PPU; subsequent calls are a no-op.
    /// <para>
    /// If <paramref name="vanillaTextureName"/> is provided (non-null) and the replacement
    /// texture still has no name, the atlas name is stamped onto it. This allows
    /// <see cref="EnforceT2DReplacements"/> and subsequent <see cref="HandleLoad"/> calls
    /// to find the replacement via the primary atlas-qualified path — matching the behaviour
    /// of the original T2DHandler (Ashiepaws) which sets <c>spriteTex.name = sprite.texture.name</c>.
    /// </para>
    /// </summary>
    private static Sprite EnsurePPU(string key, Sprite cached, float targetPPU,
        string vanillaTextureName = null)
    {
        Sprite result;
        if (System.Math.Abs(cached.pixelsPerUnit - targetPPU) <= 0.5f)
        {
            result = cached;
        }
        else
        {
            var corrected = Sprite.Create(cached.texture,
                new Rect(0, 0, cached.texture.width, cached.texture.height),
                new Vector2(0.5f, 0.5f), targetPPU);
            corrected.name = cached.name;
            corrected.hideFlags = HideFlags.DontUnloadUnusedAsset;
            Object.Destroy(cached);
            _loadedSprites[key] = corrected;
            result = corrected;
        }

        // Stamp the atlas name onto the replacement texture so that enforcement
        // (EnforceT2DReplacements → KeyForSprite) can locate this sprite via
        // the primary T2D path rather than the plain-name fallback.
        if (vanillaTextureName != null && result.texture != null
            && string.IsNullOrEmpty(result.texture.name))
            result.texture.name = vanillaTextureName;

        return result;
    }

    /// <summary>
    /// Pre-corrects PPU for every replacement sprite whose vanilla counterpart is currently
    /// in memory, and stamps the atlas name onto each replacement texture.
    /// <para>
    /// Mirrors <em>ApplyReplacementsInScene Pass 2</em> from the original T2DHandler: by
    /// scanning <c>Resources.FindObjectsOfTypeAll&lt;Sprite&gt;()</c> we reach sprites on
    /// inactive objects and unspawned effects — not just the active renderers visited by the
    /// <see cref="HandleLoad"/> sweep. Without this pass, sprites that were preloaded at
    /// the 100 PPU placeholder and haven't been seen by <see cref="HandleLoad"/> yet would
    /// display at the wrong size on first spawn.
    /// </para>
    /// </summary>
    private static void PreCorrectAllPPU()
    {
        if (_loadedSprites.Count == 0) return;
        foreach (var vanilla in Resources.FindObjectsOfTypeAll<Sprite>())
        {
            if (vanilla == null || vanilla.texture == null) continue;
            if (!T2DUtil.IsT2DTexture(vanilla.texture.name)) continue;
            string cleanTexName = T2DUtil.CleanTextureName(vanilla.texture.name);
            string key = T2DUtil.SpriteKey(cleanTexName, vanilla.name);
            if (!_loadedSprites.TryGetValue(key, out var cached) || cached == null) continue;
            EnsurePPU(key, cached, vanilla.pixelsPerUnit, vanilla.texture.name);
        }
    }

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
                    replacement = EnsurePPU(key, cached, sprite.pixelsPerUnit, sprite.texture.name);
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
                replacement = EnsurePPU(fallbackKey, fallback, sprite.pixelsPerUnit, sprite.texture.name);
            }

            if (replacement != null)
            {
                if (replacement != sprite)
                {
                    // Protect the vanilla sprite (and its texture) from eviction before we
                    // orphan it by setting our replacement on the renderer.
                    // Resources.UnloadUnusedAssets() — called implicitly on scene transitions
                    // — does NOT count static C# references as "in use"; without this flag the
                    // vanilla sprite can be evicted and the revert sweep in ReloadSpritesInScene
                    // (which uses FindObjectsOfTypeAll<Sprite>) would silently miss it, leaving
                    // the renderer pointing at an about-to-be-destroyed sprite object.
                    sprite.hideFlags |= HideFlags.DontUnloadUnusedAsset;
                    if (sprite.texture != null)
                        sprite.texture.hideFlags |= HideFlags.DontUnloadUnusedAsset;
                }
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

    // Wall-clock timestamp of the last uninit sweep — avoids FPS-dependent call rates.
    private static float _lastUninitCheckTime = float.MinValue;
    private const  float UninitCheckInterval  = 1f;   // seconds; independent of frame rate

    public static void CheckForUninitializedSprites()
    {
        // Needs to run for both spritesheet and individual-sprite packs.
        // Animators drive sprite changes through native code that bypasses the managed
        // Harmony setter patch; this sweep catches those renderers via FindObjectsByType
        // and re-triggers the setter via managed code so HandleLoad can process them.
        if (!HasT2DReplacements)
            return;

        // Wall-clock cooldown: FindObjectsByType is expensive (8-13ms).
        // Using a frame counter is FPS-dependent — at 300fps a 60-frame interval fires 5×/s.
        float now = UnityEngine.Time.unscaledTime;
        if (now - _lastUninitCheckTime < UninitCheckInterval)
            return;
        _lastUninitCheckTime = now;

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
        }
    }

    // ================================================================
    //  Hot reload and cache invalidation
    // ================================================================

    public static void ReloadSpritesInScene()
    {
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

        PreloadAllTextures();

        // Re-apply in-place texture swaps; also restore vanilla pixels for any texture whose
        // pack was just disabled (HasStoredOriginals is true while any originals are pending).
        if (SpritesheetOverrides.Count > 0 || HasStoredOriginals)
        {
            foreach (var tex in Resources.FindObjectsOfTypeAll<Texture2D>())
            {
                if (tex == null) continue;
                bool preSkipped = SkippedTextureIds.Contains(tex.GetInstanceID());
                if (!TrySwapTexture(tex) && !preSkipped)
                    TryRestoreTexture(tex);
            }
            if (SpritesheetOverrides.Count == 0)
                _originalTextureData.Clear();
        }

        foreach (var sr in Resources.FindObjectsOfTypeAll<SpriteRenderer>())
        {
            if (sr == null || sr.sprite == null || sr.sprite.texture == null) continue;
            HandleLoad(sr, sr.sprite);
        }
        foreach (var img in Resources.FindObjectsOfTypeAll<Image>())
        {
            if (img == null || img.sprite == null || img.sprite.texture == null) continue;
            HandleLoad(img, img.sprite);
        }

        // Pre-correct PPU for ALL vanilla sprites in memory (including unspawned effects).
        // The HandleLoad sweep above only reaches active renderers, so effect sprites on
        // inactive objects or pooled prefabs would keep the 100f placeholder until first spawn.
        // This pass fixes them upfront — exactly as master's ApplyReplacementsInScene Pass 2 does.
        PreCorrectAllPPU();

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
            foreach (var s in Resources.FindObjectsOfTypeAll<Sprite>())
            {
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
            Plugin.Logger.LogInfo($"[T2D-Reload] Revert sweep: {oldSprites.Count} old sprites, {vanillaByName.Count} vanilla candidates, {revertCount} reverted, {missCount} missed");
            if (missCount > 0)
                Plugin.Logger.LogWarning($"[T2D-Reload] {missCount} sprite(s) could not be reverted — see [T2D-Revert] warnings above for names");
        }

        // NOW destroy old sprites — renderers have been updated with new replacements
        foreach (var sprite in oldSprites)
        {
            if (sprite != null && sprite.texture != null)
                Object.Destroy(sprite.texture);
            if (sprite != null)
                Object.Destroy(sprite);
        }

    }


    public static void InvalidateSpritesheet(string texName)
    {
        // NOTE: May be called from the file watcher's background thread.
        // Do NOT call Object.Destroy here — only clear caches.
        string cleanName = T2DUtil.CleanTextureName(texName);
        SpritesheetOverrides.Remove(texName);
        ReplacedTextureIds.Clear();
        SkippedTextureIds.Clear();

        string prefix = cleanName + "/";
        foreach (var key in _loadedSprites.Keys.Where(k => k.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase)).ToList())
            _loadedSprites.Remove(key);
    }

    public static void InvalidateCache(string spriteName, string atlasName = null)
    {
        // NOTE: May be called from the file watcher's background thread.
        // Do NOT call Object.Destroy here — only clear caches.

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
