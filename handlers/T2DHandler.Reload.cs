using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace Patchwork.Handlers;

public static partial class T2DHandler
{
    // ================================================================
    //  Reload and invalidation
    // ================================================================

    public static void ReloadSpritesInScene()
    {
        Plugin.Logger.LogInfo($"[T2D-Reload] Starting hot reload. " +
            $"Pre-reload state: {LoadedT2DSprites.Count} loaded sprites, " +
            $"{PreloadedT2DTextures.Count} preloaded textures, " +
            $"{SpritesheetOverrides.Count} spritesheet overrides, " +
            $"{SpriteAtlasMap.Count} atlas map entries");

        // Save old objects for deferred cleanup — do NOT destroy yet,
        // because renderers still reference these sprites. Destroying
        // now would leave renderers with null sprites, causing them to
        // be skipped during the re-apply loop below.
        var oldSprites = new List<Sprite>(LoadedT2DSprites.Values);
        var oldTextures = new List<Texture2D>(PreloadedT2DTextures.Values);

        // Clear caches (renderers still hold live references to old sprites)
        LoadedT2DSprites.Clear();
        PreloadedT2DTextures.Clear();
        SpriteAtlasMap.Clear();
        NegativeSpriteCache.Clear();
        ReplacedTextureIds.Clear();
        SkippedTextureIds.Clear();
        CleanTextureNameCache.Clear();

        // Rebuild everything from disk — PreloadAllT2DTextures handles both
        // spritesheet overrides AND individual sprite PNG scanning + eager Sprite creation.
        // Previously only BuildSpritesheetOverrides was called, which missed individual
        // sprite replacements — they were only recovered lazily via HandleLoad, but only
        // for renderers still alive in the scene.
        PreloadAllT2DTextures();
        Plugin.Logger.LogInfo($"[T2D-Reload] After PreloadAllT2DTextures: " +
            $"{SpritesheetOverrides.Count} spritesheet overrides, " +
            $"{PreloadedT2DTextures.Count} preloaded textures, " +
            $"{LoadedT2DSprites.Count} eagerly loaded sprites");

        // Re-apply in-place texture swaps
        if (SpritesheetOverrides.Count > 0)
        {
            int swapCount = 0;
            foreach (var tex in Resources.FindObjectsOfTypeAll<Texture2D>())
            {
                if (tex != null && TrySwapTexture(tex))
                    swapCount++;
            }
            Plugin.Logger.LogInfo($"[T2D-Reload] Swapped {swapCount} textures via spritesheets");
        }

        // Re-apply individual sprite replacements to all renderers
        // (renderers still have their old sprites, so sr.sprite != null)
        int srCount = 0, srLoaded = 0;
        foreach (var spriteRenderer in Resources.FindObjectsOfTypeAll<SpriteRenderer>())
        {
            if (spriteRenderer == null || spriteRenderer.sprite == null)
                continue;
            srCount++;
            int prevLoaded = LoadedT2DSprites.Count;
            HandleLoad(spriteRenderer, spriteRenderer.sprite);
            if (LoadedT2DSprites.Count > prevLoaded)
                srLoaded++;
        }
        int imgCount = 0, imgLoaded = 0;
        foreach (var image in Resources.FindObjectsOfTypeAll<Image>())
        {
            if (image == null || image.sprite == null)
                continue;
            imgCount++;
            int prevLoaded = LoadedT2DSprites.Count;
            HandleLoad(image, image.sprite);
            if (LoadedT2DSprites.Count > prevLoaded)
                imgLoaded++;
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
            if (tex != null)
                Object.Destroy(tex);
        }

        Plugin.Logger.LogInfo($"[T2D-Reload] Reload complete. " +
            $"Post-reload state: {LoadedT2DSprites.Count} loaded sprites, " +
            $"{PreloadedT2DTextures.Count} preloaded textures, " +
            $"{SpriteAtlasMap.Count} atlas map entries. " +
            $"Destroyed {oldSprites.Count} old sprites, {oldTextures.Count} old textures");
    }

    public static void InvalidateSpritesheet(string texName)
    {
        // NOTE: This may be called from the file watcher's background thread.
        // Do NOT call Object.Destroy here — only clear caches.
        // Actual object cleanup happens in ReloadSpritesInScene on the main thread.
        string cleanName = CleanTextureName(texName);
        Plugin.Logger.LogInfo($"[T2D-Invalidate] InvalidateSpritesheet('{texName}') → clean='{cleanName}'. " +
            $"Before: {SpritesheetOverrides.Count} overrides, {LoadedT2DSprites.Count} loaded sprites");

        // Remove by raw name (the key used by BuildSpritesheetOverrides).
        SpritesheetOverrides.Remove(texName);
        ReplacedTextureIds.Clear();
        SkippedTextureIds.Clear();

        int removedSprites = 0;
        foreach (var kvp in SpriteAtlasMap.ToList())
        {
            if (CleanTextureName(kvp.Key) != cleanName)
                continue;

            foreach (var spriteName in kvp.Value.ToList())
            {
                string key = SpriteKey(cleanName, spriteName);
                LoadedT2DSprites.Remove(key);
                removedSprites++;
            }
        }
        Plugin.Logger.LogInfo($"[T2D-Invalidate] InvalidateSpritesheet done. Removed {removedSprites} loaded sprites. " +
            $"After: {SpritesheetOverrides.Count} overrides, {LoadedT2DSprites.Count} loaded sprites");
    }

    public static void InvalidateCache(string spriteName, string atlasName = null)
    {
        // NOTE: This is called from the file watcher's background thread.
        // Do NOT call Object.Destroy here — only clear caches.
        // Actual object cleanup happens in ReloadSpritesInScene on the main thread.
        Plugin.Logger.LogInfo($"[T2D-Invalidate] InvalidateCache('{spriteName}', atlas='{atlasName}')");

        if (atlasName != null)
        {
            string key = SpriteKey(atlasName, spriteName);
            LoadedT2DSprites.Remove(key);
            PreloadedT2DTextures.Remove(key);
            NegativeSpriteCache.Remove(key);
        }
        else
        {
            // No atlas context — remove all composite keys ending with this sprite name
            foreach (var key in LoadedT2DSprites.Keys.Where(k => k == spriteName || k.EndsWith($"/{spriteName}")).ToList())
                LoadedT2DSprites.Remove(key);
            foreach (var key in PreloadedT2DTextures.Keys.Where(k => k == spriteName || k.EndsWith($"/{spriteName}")).ToList())
                PreloadedT2DTextures.Remove(key);
            NegativeSpriteCache.Remove(spriteName);
        }

        if (SpriteAtlasMap.TryGetValue(spriteName, out var atlasSprites))
        {
            string cleanName = CleanTextureName(spriteName);
            foreach (var sprName in atlasSprites)
            {
                string key = SpriteKey(cleanName, sprName);
                LoadedT2DSprites.Remove(key);
                PreloadedT2DTextures.Remove(key);
                NegativeSpriteCache.Remove(key);
            }
            SpriteAtlasMap.Remove(spriteName);
        }
    }
}
