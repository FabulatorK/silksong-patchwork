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
    // These use Sprite.Create with the replacement PNG as a standalone texture.
    private static readonly Dictionary<string, Sprite>           _loadedSprites      = new();
    private static readonly Dictionary<string, Texture2D>        _preloadedTextures  = new();
    private static readonly Dictionary<string, HashSet<string>>  _spriteAtlasMap     = new();
    // Maps asset key → source pack path (null = base Patchwork folder). Used for conflict reporting.
    private static readonly Dictionary<string, string>           _t2dProviders       = new();

    // Sprite names that are confirmed to belong to T2D atlas textures.
    // Used to scope enforcement/loading — prevents replacing UI sprites
    // that happen to share a name with a T2D replacement file.
    private static readonly HashSet<string> _confirmedSpriteNames = new();

    // Sprite names confirmed to have no replacement on disk.
    // Prevents repeated filesystem scans for the same missing sprite.
    private static readonly HashSet<string> _negativeCache = new();

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
        SpritesheetOverrides.Count > 0 || _preloadedTextures.Count > 0 || _loadedSprites.Count > 0;

    /// <summary>True while a harmony setter or enforcement sweep is in progress.</summary>
    public static bool IsHandlingOrEnforcing => _handling || _enforcing;

    // Read-only stats for GUI
    public static int SpritesheetOverrideCount => SpritesheetOverrides.Count;
    public static int LoadedT2DSpriteCount => _loadedSprites.Count;
    public static int PreloadedT2DTextureCount => _preloadedTextures.Count;
    public static int TrackedRendererCount => _knownRenderers.Count + _knownImages.Count;
    public static IEnumerable<string> LoadedT2DSpriteNames => _loadedSprites.Keys;
    public static IEnumerable<string> SpritesheetOverrideNames => SpritesheetOverrides.Keys;
    public static IEnumerable<string> PreloadedT2DTextureNames => _preloadedTextures.Keys;

    // ================================================================
    //  Entry points called from T2DHandler harmony patches
    // ================================================================

    public static void OnSpriteSet(SpriteRenderer sr, Sprite value)
    {
        _trackedSpriteNames[sr.GetInstanceID()] = value.name;
        if (value.texture != null)
            TrySwapTexture(value.texture);
        HandleLoad(sr, value);
    }

    public static void OnSpriteSet(Image img, Sprite value)
    {
        _trackedSpriteNames[img.GetInstanceID()] = value.name;
        if (value.texture != null)
            TrySwapTexture(value.texture);
        HandleLoad(img, value);
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

        // Pass 2: Convert remaining preloaded individual textures into Sprites.
        if (_preloadedTextures.Count > 0)
        {
            foreach (var original in Resources.FindObjectsOfTypeAll<Sprite>())
            {
                if (original == null || original.texture == null)
                    continue;
                if (!T2DUtil.IsT2DTexture(original.texture.name))
                    continue;

                _confirmedSpriteNames.Add(original.name);

                string cleanTexName = T2DUtil.CleanTextureName(original.texture.name);
                string key = T2DUtil.SpriteKey(cleanTexName, original.name);
                if (!_preloadedTextures.TryGetValue(key, out var tex))
                    continue;

                tex.name = original.texture.name;

                Sprite newSprite = Sprite.Create(tex,
                    new Rect(0, 0, tex.width, tex.height),
                    new Vector2(0.5f, 0.5f), original.pixelsPerUnit);
                newSprite.name = original.name;

                _loadedSprites[key] = newSprite;
                _preloadedTextures.Remove(key);

                string texName = original.texture.name;
                if (!_spriteAtlasMap.ContainsKey(texName))
                    _spriteAtlasMap[texName] = new HashSet<string>();
                _spriteAtlasMap[texName].Add(original.name);
            }
        }

        // Pass 3: Apply individual sprite replacements to all renderers/images (including inactive).
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
    }

    public static void PreloadAllTextures()
    {
        BuildSpritesheetOverrides();

        void ScanDirectory(string t2dRoot, string sourcePack)
        {
            if (!Directory.Exists(t2dRoot))
                return;

            // Structured path: T2D/[AtlasName]/[SpriteName].png
            foreach (var atlasDir in Directory.GetDirectories(t2dRoot))
            {
                string atlasName = Path.GetFileName(atlasDir);
                foreach (var file in Directory.GetFiles(atlasDir, "*.png"))
                {
                    string spriteName = Path.GetFileNameWithoutExtension(file);
                    string key = T2DUtil.SpriteKey(atlasName, spriteName);
                    if (_preloadedTextures.ContainsKey(key) || _loadedSprites.ContainsKey(key))
                    {
                        ConflictTracker.Record("t2d-sprite", key,
                            _t2dProviders.GetValueOrDefault(key), sourcePack);
                        continue;
                    }

                    Texture2D tex = TexUtil.LoadFromPNG(file);
                    if (tex != null)
                    {
                        _preloadedTextures[key] = tex;
                        _t2dProviders[key] = sourcePack;
                    }
                }
            }

            // Flat path: T2D/[SpriteName].png — acts as a fallback when no atlas-qualified match exists.
            foreach (var file in Directory.GetFiles(t2dRoot, "*.png"))
            {
                string spriteName = Path.GetFileNameWithoutExtension(file);
                if (_preloadedTextures.ContainsKey(spriteName) || _loadedSprites.ContainsKey(spriteName))
                {
                    ConflictTracker.Record("t2d-sprite", spriteName,
                        _t2dProviders.GetValueOrDefault(spriteName), sourcePack);
                    continue;
                }

                Texture2D tex = TexUtil.LoadFromPNG(file);
                if (tex != null)
                {
                    _preloadedTextures[spriteName] = tex;
                    _t2dProviders[spriteName] = sourcePack;
                }
            }
        }

        ScanDirectory(Path.Combine(SpriteLoader.LoadPath, "T2D"), null);
        foreach (var packPath in Plugin.PluginPackPaths)
            ScanDirectory(Path.Combine(packPath, "Sprites", "T2D"), packPath);

        // Eagerly create replacement Sprites for any originals already in memory
        foreach (var original in Resources.FindObjectsOfTypeAll<Sprite>())
        {
            if (original == null || original.texture == null)
                continue;
            if (!T2DUtil.IsT2DTexture(original.texture.name))
                continue;

            _confirmedSpriteNames.Add(original.name);

            string cleanTexName = T2DUtil.CleanTextureName(original.texture.name);
            string key = T2DUtil.SpriteKey(cleanTexName, original.name);

            string matchedKey = null;
            Texture2D tex = null;
            if (_preloadedTextures.TryGetValue(key, out tex))
                matchedKey = key;
            else if (_preloadedTextures.TryGetValue(original.name, out tex))
                matchedKey = original.name;

            if (matchedKey != null)
            {
                tex.name = original.texture.name;

                Sprite newSprite = Sprite.Create(tex,
                    new Rect(0, 0, tex.width, tex.height),
                    new Vector2(0.5f, 0.5f), original.pixelsPerUnit);
                newSprite.name = original.name;

                _loadedSprites[key] = newSprite;
                _preloadedTextures.Remove(matchedKey);

                string texName = original.texture.name;
                if (!_spriteAtlasMap.ContainsKey(texName))
                    _spriteAtlasMap[texName] = new HashSet<string>();
                _spriteAtlasMap[texName].Add(original.name);
            }
        }
    }

    // ================================================================
    //  Atlas preloading (individual sprites from Sprites/T2D/)
    // ================================================================

    private static void PreloadAtlasTextures(string textureName, string cleanTexName)
    {
        _spriteAtlasMap[textureName] = new HashSet<string>();

        void LoadFromDirectory(string dir)
        {
            if (!Directory.Exists(dir)) return;
            foreach (var file in Directory.GetFiles(dir, "*.png"))
            {
                string spriteName = Path.GetFileNameWithoutExtension(file);
                _spriteAtlasMap[textureName].Add(spriteName);

                string key = T2DUtil.SpriteKey(cleanTexName, spriteName);
                if (_preloadedTextures.ContainsKey(key) || _loadedSprites.ContainsKey(key))
                    continue;

                Texture2D spriteTex = TexUtil.LoadFromPNG(file);
                if (spriteTex == null) continue;
                spriteTex.name = textureName;

                _preloadedTextures[key] = spriteTex;
            }
        }

        LoadFromDirectory(Path.Combine(SpriteLoader.LoadPath, "T2D", cleanTexName));
        string sanitizedRaw = T2DUtil.SanitizeForFilesystem(textureName);
        if (sanitizedRaw != cleanTexName)
            LoadFromDirectory(Path.Combine(SpriteLoader.LoadPath, "T2D", sanitizedRaw));

        foreach (var packPath in Plugin.PluginPackPaths)
        {
            LoadFromDirectory(Path.Combine(packPath, "Sprites", "T2D", cleanTexName));
            if (sanitizedRaw != cleanTexName)
                LoadFromDirectory(Path.Combine(packPath, "Sprites", "T2D", sanitizedRaw));
        }
    }

    // ================================================================
    //  Individual sprite loading (HandleLoad)
    // ================================================================

    internal static void HandleLoad(object spriteContainer, Sprite sprite)
    {
        if (_handling || !HasT2DReplacements)
            return;

        _handling = true;
        try
        {
            if (T2DUtil.IsT2DTexture(sprite.texture.name))
            {
                string cleanTexName = T2DUtil.CleanTextureName(sprite.texture.name);
                string key = T2DUtil.SpriteKey(cleanTexName, sprite.name);

                _confirmedSpriteNames.Add(sprite.name);

                if (_loadedSprites.TryGetValue(key, out var cached))
                {
                    SetSprite(spriteContainer, cached);
                    TrackContainer(spriteContainer);
                    return;
                }

                if (!_spriteAtlasMap.ContainsKey(sprite.texture.name))
                    PreloadAtlasTextures(sprite.texture.name, cleanTexName);

                Texture2D spriteTex = null;
                string matchedKey = null;
                if (_preloadedTextures.TryGetValue(key, out spriteTex))
                    matchedKey = key;
                else if (_preloadedTextures.TryGetValue(sprite.name, out spriteTex))
                    matchedKey = sprite.name;

                if (spriteTex != null)
                {
                    spriteTex.name = sprite.texture.name;

                    Sprite newSprite = Sprite.Create(spriteTex,
                        new Rect(0, 0, spriteTex.width, spriteTex.height),
                        new Vector2(0.5f, 0.5f), sprite.pixelsPerUnit);
                    newSprite.name = sprite.name;

                    _loadedSprites[key] = newSprite;
                    _preloadedTextures.Remove(matchedKey);

                    SetSprite(spriteContainer, newSprite);
                    TrackContainer(spriteContainer);
                }
                // Spritesheet replacement is handled by TrySwapTexture in-place.
            }
            else
            {
                string texName = sprite.texture.name;

                if (_loadedSprites.TryGetValue(texName, out var existing))
                {
                    SetSprite(spriteContainer, existing);
                    TrackContainer(spriteContainer);
                    return;
                }

                if (_negativeCache.Contains(texName))
                    return;

                Texture2D spriteTex = FindSprite(texName);
                if (spriteTex == null)
                {
                    _negativeCache.Add(texName);
                    return;
                }
                spriteTex.name = texName;
                Sprite newSprite = Sprite.Create(spriteTex, new Rect(0, 0, spriteTex.width, spriteTex.height), new Vector2(0.5f, 0.5f), sprite.pixelsPerUnit);
                newSprite.name = sprite.name;

                _loadedSprites[texName] = newSprite;
                SetSprite(spriteContainer, newSprite);
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

    private static Texture2D FindSprite(string spriteName)
    {
        var files = Directory.GetFiles(SpriteLoader.LoadPath, spriteName + ".png", SearchOption.AllDirectories)
            .Where(f => Path.GetDirectoryName(f).EndsWith("T2D"));
        if (files.Any())
            return TexUtil.LoadFromPNG(files.First());

        foreach (var packPath in Plugin.PluginPackPaths)
        {
            string packSprites = Path.Combine(packPath, "Sprites");
            if (!Directory.Exists(packSprites))
                continue;
            var packFiles = Directory.GetFiles(packSprites, spriteName + ".png", SearchOption.AllDirectories)
                .Where(f => Path.GetDirectoryName(f).EndsWith("T2D"));
            if (packFiles.Any())
                return TexUtil.LoadFromPNG(packFiles.First());
        }

        return null;
    }

    // ================================================================
    //  Periodic enforcement
    // ================================================================

    public static void CheckForUninitializedSprites()
    {
        if (!HasT2DReplacements)
            return;

        foreach (var sr in Object.FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None))
        {
            if (sr == null || sr.sprite == null) continue;
            CheckSprite(sr.GetInstanceID(), sr.sprite, out bool changed, out _);
            if (changed)
                sr.sprite = sr.sprite;
        }

        foreach (var img in Object.FindObjectsByType<Image>(FindObjectsSortMode.None))
        {
            if (img == null || img.sprite == null) continue;
            CheckSprite(img.GetInstanceID(), img.sprite, out bool changed, out _);
            if (changed)
                img.sprite = img.sprite;
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

    /// <summary>Checks if a sprite's name has changed or its replacement is missing from the container.</summary>
    private static void CheckSprite(int instanceId, Sprite sprite, out bool nameChanged, out bool replacementMissing)
    {
        if (!_confirmedSpriteNames.Contains(sprite.name))
        {
            nameChanged = false;
            replacementMissing = false;
            return;
        }

        string key = KeyForSprite(sprite);
        nameChanged = !_trackedSpriteNames.TryGetValue(instanceId, out string last) || last != sprite.name;
        replacementMissing = !nameChanged && _loadedSprites.TryGetValue(key, out var cached) && sprite != cached;

        if (nameChanged || replacementMissing)
            _trackedSpriteNames[instanceId] = sprite.name;
    }

    private static bool TryGetReplacement(Sprite sprite, out Sprite replacement)
    {
        if (!_confirmedSpriteNames.Contains(sprite.name))
        {
            replacement = null;
            return false;
        }
        return _loadedSprites.TryGetValue(KeyForSprite(sprite), out replacement);
    }

    private static string KeyForSprite(Sprite sprite)
        => (sprite.texture != null && T2DUtil.IsT2DTexture(sprite.texture.name))
            ? T2DUtil.SpriteKey(T2DUtil.CleanTextureName(sprite.texture.name), sprite.name)
            : sprite.name;

    // ================================================================
    //  Hot reload and cache invalidation
    // ================================================================

    public static void ReloadSpritesInScene()
    {
        Plugin.Logger.LogInfo($"[T2D-Reload] Starting hot reload. " +
            $"Pre-reload state: {_loadedSprites.Count} loaded sprites, " +
            $"{_preloadedTextures.Count} preloaded textures, " +
            $"{SpritesheetOverrides.Count} spritesheet overrides, " +
            $"{_spriteAtlasMap.Count} atlas map entries");

        // Save old objects for deferred cleanup — do NOT destroy yet,
        // because renderers still reference these sprites. Destroying now would leave
        // renderers with null sprites, causing them to be skipped during re-apply.
        var oldSprites = new List<Sprite>(_loadedSprites.Values);
        var oldTextures = new List<Texture2D>(_preloadedTextures.Values);

        // Clear all caches
        _loadedSprites.Clear();
        _preloadedTextures.Clear();
        _spriteAtlasMap.Clear();
        _negativeCache.Clear();
        _t2dProviders.Clear();
        ReplacedTextureIds.Clear();
        SkippedTextureIds.Clear();
        T2DUtil.ClearCleanNameCache();

        // Rebuild everything from disk
        PreloadAllTextures();
        Plugin.Logger.LogInfo($"[T2D-Reload] After PreloadAllTextures: " +
            $"{SpritesheetOverrides.Count} spritesheet overrides, " +
            $"{_preloadedTextures.Count} preloaded textures, " +
            $"{_loadedSprites.Count} eagerly loaded sprites");

        // Re-apply in-place texture swaps; also restore vanilla pixels for any texture whose
        // pack was just disabled (HasStoredOriginals is true while any originals are pending).
        if (SpritesheetOverrides.Count > 0 || HasStoredOriginals)
        {
            int swapCount = 0, restoreCount = 0;
            foreach (var tex in Resources.FindObjectsOfTypeAll<Texture2D>())
            {
                if (tex == null) continue;
                if (TrySwapTexture(tex))
                    swapCount++;
                else if (TryRestoreTexture(tex))
                    restoreCount++;
            }
            Plugin.Logger.LogInfo($"[T2D-Reload] Swapped {swapCount} textures via spritesheets, restored {restoreCount} to vanilla");
        }

        // Re-apply individual sprite replacements to all renderers
        int srCount = 0, srLoaded = 0;
        foreach (var sr in Resources.FindObjectsOfTypeAll<SpriteRenderer>())
        {
            if (sr == null || sr.sprite == null) continue;
            srCount++;
            int prev = _loadedSprites.Count;
            HandleLoad(sr, sr.sprite);
            if (_loadedSprites.Count > prev) srLoaded++;
        }
        int imgCount = 0, imgLoaded = 0;
        foreach (var img in Resources.FindObjectsOfTypeAll<Image>())
        {
            if (img == null || img.sprite == null) continue;
            imgCount++;
            int prev = _loadedSprites.Count;
            HandleLoad(img, img.sprite);
            if (_loadedSprites.Count > prev) imgLoaded++;
        }
        Plugin.Logger.LogInfo($"[T2D-Reload] HandleLoad pass: " +
            $"{srCount} SpriteRenderers ({srLoaded} new loads), " +
            $"{imgCount} Images ({imgLoaded} new loads)");

        // NOW destroy old objects — renderers have been updated with new replacements
        foreach (var sprite in oldSprites)
        {
            if (sprite != null && sprite.texture != null)
                Object.Destroy(sprite.texture);
            if (sprite != null)
                Object.Destroy(sprite);
        }
        foreach (var tex in oldTextures)
        {
            if (tex != null) Object.Destroy(tex);
        }

        Plugin.Logger.LogInfo($"[T2D-Reload] Reload complete. " +
            $"Post-reload state: {_loadedSprites.Count} loaded sprites, " +
            $"{_preloadedTextures.Count} preloaded textures, " +
            $"{_spriteAtlasMap.Count} atlas map entries. " +
            $"Destroyed {oldSprites.Count} old sprites, {oldTextures.Count} old textures");
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

        int removedSprites = 0;
        foreach (var kvp in _spriteAtlasMap.ToList())
        {
            if (T2DUtil.CleanTextureName(kvp.Key) != cleanName)
                continue;

            foreach (var spriteName in kvp.Value.ToList())
            {
                _loadedSprites.Remove(T2DUtil.SpriteKey(cleanName, spriteName));
                removedSprites++;
            }
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
            string key = T2DUtil.SpriteKey(atlasName, spriteName);
            _loadedSprites.Remove(key);
            _preloadedTextures.Remove(key);
            _negativeCache.Remove(key);
        }
        else
        {
            foreach (var key in _loadedSprites.Keys.Where(k => k == spriteName || k.EndsWith($"/{spriteName}")).ToList())
                _loadedSprites.Remove(key);
            foreach (var key in _preloadedTextures.Keys.Where(k => k == spriteName || k.EndsWith($"/{spriteName}")).ToList())
                _preloadedTextures.Remove(key);
            _negativeCache.Remove(spriteName);
        }

        if (_spriteAtlasMap.TryGetValue(spriteName, out var atlasSprites))
        {
            string cleanName = T2DUtil.CleanTextureName(spriteName);
            foreach (var sprName in atlasSprites)
            {
                string key = T2DUtil.SpriteKey(cleanName, sprName);
                _loadedSprites.Remove(key);
                _preloadedTextures.Remove(key);
                _negativeCache.Remove(key);
            }
            _spriteAtlasMap.Remove(spriteName);
        }
    }
}
