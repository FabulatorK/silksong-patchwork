using System.IO;
using HarmonyLib;
using UnityEngine;
using System.Linq;
using Patchwork.Util;
using System.Collections.Generic;
using UnityEngine.UI;

namespace Patchwork.Handlers;

[HarmonyPatch]
public static class T2DHandler
{
    public static string T2DDumpPath { get { return Path.Combine(SpriteDumper.DumpPath, "T2D"); } }
    public static string T2DAtlasLoadPath { get { return Path.Combine(SpriteLoader.AtlasLoadPath, "T2D"); } }

    // --- In-place spritesheet replacement (inspired by Customizer T2D) ---
    // Keyed by raw filename (without extension). At runtime, TrySwapTexture looks up
    // by raw texture name first, then falls back to clean name. This naturally handles
    // multiple atlases sharing the same clean name (e.g. sactx-1-2048x2048-BC7-Hornet-*
    // and sactx-0-4096x4096-BC7-Hornet-* are simply two different keys).
    private static readonly Dictionary<string, (byte[] PngData, int Width, int Height)>
        SpritesheetOverrides = new(System.StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<int> ReplacedTextureIds = new();
    private static readonly HashSet<int> SkippedTextureIds = new();

    // --- Individual sprite replacement (Patchwork's value-add) ---
    // Skin authors can replace individual frames without repacking an atlas.
    // These use Sprite.Create with the replacement PNG as a standalone texture.
    private static readonly Dictionary<string, Sprite> LoadedT2DSprites = new();
    private static readonly Dictionary<string, Texture2D> PreloadedT2DTextures = new();
    private static readonly Dictionary<string, HashSet<string>> SpriteAtlasMap = new();

    // Sprite names that are confirmed to belong to T2D atlas textures.
    // Used to scope enforcement/loading — prevents replacing UI sprites
    // that happen to share a name with a T2D replacement file.
    private static readonly HashSet<string> ConfirmedT2DSpriteNames = new();

    // Sprite names confirmed to have no replacement on disk.
    // Prevents repeated filesystem scans for the same missing sprite.
    private static readonly HashSet<string> NegativeSpriteCache = new();

    private static readonly Dictionary<int, string> TrackedSpriteNames = new();
    private static readonly HashSet<SpriteRenderer> KnownT2DSpriteRenderers = new();
    private static readonly HashSet<Image> KnownT2DImages = new();
    private static bool _enforcing = false;
    private static bool _handling = false;

    /// <summary>
    /// True when any T2D replacement data exists (spritesheets or individual sprites).
    /// Gates all expensive per-frame sweeps so Patchwork is near-zero-cost when no T2D
    /// assets are loaded.
    /// </summary>
    public static bool HasT2DReplacements =>
        SpritesheetOverrides.Count > 0 || PreloadedT2DTextures.Count > 0 || LoadedT2DSprites.Count > 0;


    // ================================================================
    //  Harmony patches — sprite/material setters
    // ================================================================

    [HarmonyPostfix]
    [HarmonyPatch(typeof(SpriteRenderer), nameof(SpriteRenderer.sprite), MethodType.Setter)]
    public static void SetSpritePostfix(SpriteRenderer __instance, Sprite value)
    {
        if (_handling || _enforcing || __instance == null || value == null || __instance.gameObject.name == "TempSpriteRenderer")
            return;

        if (Plugin.Config.DumpSprites && !string.IsNullOrEmpty(value.name) && value.texture != null && !string.IsNullOrEmpty(value.texture.name))
            HandleDump(value);

        if (!HasT2DReplacements)
            return;

        TrackedSpriteNames[__instance.GetInstanceID()] = value.name;

        if (value.texture != null)
            TrySwapTexture(value.texture);

        HandleLoad(__instance, value);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Image), nameof(Image.sprite), MethodType.Setter)]
    public static void SetImageSpritePostfix(Image __instance, Sprite value)
    {
        if (_handling || _enforcing || __instance == null || value == null)
            return;

        if (Plugin.Config.DumpSprites && !string.IsNullOrEmpty(value.name) && value.texture != null && !string.IsNullOrEmpty(value.texture.name))
            HandleDump(value);

        if (!HasT2DReplacements)
            return;

        TrackedSpriteNames[__instance.GetInstanceID()] = value.name;

        if (value.texture != null)
        {
            string cleanName = CleanTextureName(value.texture.name);
            bool swapped = TrySwapTexture(value.texture);
            if (swapped)
            {
                Plugin.Logger.LogWarning(
                    $"[T2D-UI] In-place texture swap hit UI Image! " +
                    $"Image '{__instance.gameObject.name}', sprite '{value.name}', " +
                    $"texture '{value.texture.name}' → clean '{cleanName}'. " +
                    $"This may cause UI elements to vanish if the replacement " +
                    $"doesn't include all sprites on this atlas.");
            }
        }

        HandleLoad(__instance, value);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Material), nameof(Material.mainTexture), MethodType.Setter)]
    public static void SetMaterialTexturePostfix(Texture value)
    {
        if (!HasT2DReplacements)
            return;

        if (value is Texture2D tex)
        {
            TrySwapTexture(tex);

            if (Plugin.Config.DumpSprites && IsT2DTexture(tex.name))
                DumpT2DAtlasTexture(tex);
        }
    }

    // ================================================================
    //  In-place texture swap (the Customizer T2D approach)
    // ================================================================

    /// <summary>
    /// Attempts to replace a Texture2D's pixel data in-place using a spritesheet override.
    /// Same texture object, new pixels. All sprites referencing this texture automatically
    /// display the new art without any rect/pivot changes.
    /// </summary>
    private static bool TrySwapTexture(Texture2D tex)
    {
        if (tex == null)
            return false;

        int id = tex.GetInstanceID();
        if (ReplacedTextureIds.Contains(id) || SkippedTextureIds.Contains(id))
            return ReplacedTextureIds.Contains(id);

        // Try raw texture name first (exact match), then fall back to clean name.
        // This lets sactx-1-2048x2048-BC7-Hornet-* and sactx-0-4096x4096-BC7-Hornet-*
        // each have their own replacement without colliding.
        string cleanName = CleanTextureName(tex.name);
        if (!SpritesheetOverrides.TryGetValue(tex.name, out var data)
            && !SpritesheetOverrides.TryGetValue(cleanName, out data))
        {
            SkippedTextureIds.Add(id);
            return false;
        }

        if (tex.width != data.Width || tex.height != data.Height)
        {
            Plugin.Logger.LogWarning(
                $"[T2D] Spritesheet size mismatch for '{cleanName}': " +
                $"runtime texture '{tex.name}' is {tex.width}x{tex.height}, " +
                $"replacement PNG is {data.Width}x{data.Height}. " +
                $"Skipping. (texture ID: {id})");
            LogAtlasContents(tex, cleanName);
            SkippedTextureIds.Add(id);
            return false;
        }

        if (tex.LoadImage(data.PngData))
        {
            ReplacedTextureIds.Add(id);
            Plugin.Logger.LogInfo(
                $"[T2D] Spritesheet applied in-place: '{cleanName}' " +
                $"(texture '{tex.name}', {tex.width}x{tex.height})");

            // Log all sprites sharing this atlas so modders know what they're affecting.
            // If UI sprites live on the same atlas, the modder needs to include them
            // in their replacement PNG or they'll vanish.
            LogAtlasContents(tex, cleanName);

            if (Plugin.Config.ConvertSpritesheets)
                ConvertT2DSpritesheet(tex, cleanName);

            return true;
        }

        Plugin.Logger.LogWarning($"[T2D] LoadImage failed for '{cleanName}' on texture '{tex.name}'");
        SkippedTextureIds.Add(id);
        return false;
    }

    /// <summary>
    /// Scans Spritesheets/T2D/ for PNG files and loads their raw bytes into SpritesheetOverrides.
    /// Each file is keyed by its raw filename (without extension). At runtime, TrySwapTexture
    /// looks up by raw texture name first, then falls back to clean name — so files named
    /// after the full sactx name naturally match their specific atlas, while a simple
    /// "Hornet.png" matches any Hornet atlas as a convenience fallback.
    /// </summary>
    private static void BuildSpritesheetOverrides()
    {
        SpritesheetOverrides.Clear();

        void ScanDirectory(string dir)
        {
            if (!Directory.Exists(dir)) return;
            foreach (var file in Directory.GetFiles(dir, "*.png", SearchOption.TopDirectoryOnly))
            {
                string rawName = Path.GetFileNameWithoutExtension(file);
                if (SpritesheetOverrides.ContainsKey(rawName)) continue;

                try
                {
                    byte[] pngData = File.ReadAllBytes(file);
                    Texture2D temp = new(2, 2);
                    if (temp.LoadImage(pngData))
                    {
                        SpritesheetOverrides[rawName] = (pngData, temp.width, temp.height);
                        Plugin.Logger.LogInfo($"[T2D] Loaded spritesheet override: '{rawName}' ({temp.width}x{temp.height})");
                    }
                    Object.Destroy(temp);
                }
                catch (System.Exception ex)
                {
                    Plugin.Logger.LogWarning($"[T2D] Failed to load spritesheet '{file}': {ex.Message}");
                }
            }
        }

        ScanDirectory(T2DAtlasLoadPath);
        foreach (var packPath in Plugin.PluginPackPaths)
            ScanDirectory(Path.Combine(packPath, "Spritesheets", "T2D"));
    }

    /// <summary>
    /// Converts a T2D spritesheet into individual Patchwork-compatible sprite PNGs.
    /// Uses sprite.rect from original sprites to extract each frame from the
    /// replacement atlas texture, saving to Converted/T2D/{cleanName}/.
    /// </summary>
    private static void ConvertT2DSpritesheet(Texture2D atlas, string cleanName)
    {
        string outDir = Path.Combine(SpriteDumper.ConvertPath, "T2D", cleanName);

        // The atlas has already been overwritten in-place with the replacement PNG,
        // so it's now RGBA32 and readable. Blit to a RenderTexture for ReadPixels.
        RenderTexture rt = RenderTexture.GetTemporary(atlas.width, atlas.height, 0, RenderTextureFormat.ARGB32);
        Graphics.Blit(atlas, rt);
        var previous = RenderTexture.active;
        RenderTexture.active = rt;

        try
        {
            int count = 0;
            foreach (var sprite in Resources.FindObjectsOfTypeAll<Sprite>())
            {
                if (sprite == null || sprite.texture == null)
                    continue;
                if (sprite.texture.GetInstanceID() != atlas.GetInstanceID())
                    continue;
                if (string.IsNullOrEmpty(sprite.name))
                    continue;

                string savePath = Path.Combine(outDir, sprite.name + ".png");
                if (File.Exists(savePath))
                    continue;

                Rect rect = sprite.rect;
                if (rect.width <= 0 || rect.height <= 0)
                    continue;

                Texture2D frameTex = new((int)rect.width, (int)rect.height, TextureFormat.RGBA32, false);
                try
                {
                    frameTex.ReadPixels(new Rect(rect.x, rect.y, rect.width, rect.height), 0, 0);
                    frameTex.Apply();

                    IOUtil.EnsureDirectoryExists(outDir);
                    File.WriteAllBytes(savePath, frameTex.EncodeToPNG());
                    count++;
                }
                finally
                {
                    Object.Destroy(frameTex);
                }
            }

            if (count > 0)
                Plugin.Logger.LogInfo($"[T2D] Converted spritesheet '{cleanName}' → {count} individual sprites in {outDir}");
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
        }
    }

    // ================================================================
    //  Periodic checks and enforcement
    // ================================================================

    public static void CheckForUninitializedSprites()
    {
        if (!HasT2DReplacements)
            return;

        foreach (var spriteRenderer in Object.FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None))
        {
            if (spriteRenderer == null || spriteRenderer.sprite == null)
                continue;

            int id = spriteRenderer.GetInstanceID();
            string currentName = spriteRenderer.sprite.name;

            // Only re-trigger setter for sprites confirmed to be T2D-backed.
            if (!ConfirmedT2DSpriteNames.Contains(currentName))
                continue;

            bool nameChanged = !TrackedSpriteNames.TryGetValue(id, out string lastSprite) || lastSprite != currentName;
            bool replacementMissing = !nameChanged
                && LoadedT2DSprites.TryGetValue(currentName, out var cached)
                && spriteRenderer.sprite != cached;

            if (nameChanged || replacementMissing)
            {
                TrackedSpriteNames[id] = currentName;
                spriteRenderer.sprite = spriteRenderer.sprite;
            }
        }

        foreach (var image in Object.FindObjectsByType<Image>(FindObjectsSortMode.None))
        {
            if (image == null || image.sprite == null)
                continue;

            int id = image.GetInstanceID();
            string currentName = image.sprite.name;

            // Only re-trigger setter for sprites confirmed to be T2D-backed.
            if (!ConfirmedT2DSpriteNames.Contains(currentName))
                continue;

            bool nameChanged = !TrackedSpriteNames.TryGetValue(id, out string lastSprite) || lastSprite != currentName;
            bool replacementMissing = !nameChanged
                && LoadedT2DSprites.TryGetValue(currentName, out var cached)
                && image.sprite != cached;

            if (nameChanged || replacementMissing)
            {
                TrackedSpriteNames[id] = currentName;
                image.sprite = image.sprite;
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
            // Sweep ALL active SpriteRenderers to catch newly-instantiated ones.
            // Object.Instantiate copies sprite references at the native level,
            // bypassing the C# property setter (and our Harmony postfix).
            // Without this sweep, cloned renderers show vanilla sprites until
            // CheckForUninitializedSprites runs (every 30 frames).
            foreach (var sr in Object.FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None))
            {
                if (sr == null || sr.sprite == null) continue;
                var sprite = sr.sprite;

                // Only enforce on sprites confirmed to be T2D-backed.
                if (!ConfirmedT2DSpriteNames.Contains(sprite.name))
                    continue;

                if (LoadedT2DSprites.TryGetValue(sprite.name, out var replacement))
                {
                    if (sprite != replacement)
                    {
                        sr.sprite = replacement;
                        KnownT2DSpriteRenderers.Add(sr);
                    }
                }
                else if (PreloadedT2DTextures.TryGetValue(sprite.name, out var tex))
                {
                    var newSprite = Sprite.Create(tex,
                        new Rect(0, 0, tex.width, tex.height),
                        new Vector2(0.5f, 0.5f), sprite.pixelsPerUnit);
                    newSprite.name = sprite.name;
                    LoadedT2DSprites[sprite.name] = newSprite;
                    PreloadedT2DTextures.Remove(sprite.name);
                    sr.sprite = newSprite;
                    KnownT2DSpriteRenderers.Add(sr);
                }
            }

            foreach (var img in Object.FindObjectsByType<Image>(FindObjectsSortMode.None))
            {
                if (img == null || img.sprite == null) continue;
                var sprite = img.sprite;

                // Only enforce on sprites confirmed to be T2D-backed.
                if (!ConfirmedT2DSpriteNames.Contains(sprite.name))
                    continue;

                if (LoadedT2DSprites.TryGetValue(sprite.name, out var replacement))
                {
                    if (sprite != replacement)
                    {
                        img.sprite = replacement;
                        KnownT2DImages.Add(img);
                    }
                }
                else if (PreloadedT2DTextures.TryGetValue(sprite.name, out var tex))
                {
                    var newSprite = Sprite.Create(tex,
                        new Rect(0, 0, tex.width, tex.height),
                        new Vector2(0.5f, 0.5f), sprite.pixelsPerUnit);
                    newSprite.name = sprite.name;
                    LoadedT2DSprites[sprite.name] = newSprite;
                    PreloadedT2DTextures.Remove(sprite.name);
                    img.sprite = newSprite;
                    KnownT2DImages.Add(img);
                }
            }

            // Clean up destroyed references periodically
            KnownT2DSpriteRenderers.RemoveWhere(sr => sr == null);
            KnownT2DImages.RemoveWhere(img => img == null);
        }
        finally
        {
            _enforcing = false;
        }
    }

    // ================================================================
    //  Scene load and preloading
    // ================================================================

    public static void ApplyT2DReplacementsInScene()
    {
        // Pass 1: In-place texture replacement for spritesheets.
        // Sweep all loaded Texture2Ds and overwrite any that match our overrides.
        if (SpritesheetOverrides.Count > 0)
        {
            foreach (var tex in Resources.FindObjectsOfTypeAll<Texture2D>())
            {
                if (tex != null)
                    TrySwapTexture(tex);
            }
        }

        // Pass 2: Convert remaining preloaded individual textures into Sprites.
        if (PreloadedT2DTextures.Count > 0)
        {
            foreach (var original in Resources.FindObjectsOfTypeAll<Sprite>())
            {
                if (original == null || original.texture == null)
                    continue;
                if (!IsT2DTexture(original.texture.name))
                    continue;

                ConfirmedT2DSpriteNames.Add(original.name);

                if (!PreloadedT2DTextures.TryGetValue(original.name, out var tex))
                    continue;

                Sprite newSprite = Sprite.Create(tex,
                    new Rect(0, 0, tex.width, tex.height),
                    new Vector2(0.5f, 0.5f), original.pixelsPerUnit);
                newSprite.name = original.name;

                LoadedT2DSprites[original.name] = newSprite;
                PreloadedT2DTextures.Remove(original.name);

                string texName = original.texture.name;
                if (!SpriteAtlasMap.ContainsKey(texName))
                    SpriteAtlasMap[texName] = new HashSet<string>();
                SpriteAtlasMap[texName].Add(original.name);
            }
        }

        // Pass 3: Apply individual sprite replacements to active renderers/images.
        // Skip the full scene sweep when no T2D replacements are loaded.
        if (!HasT2DReplacements)
            return;

        foreach (var sr in Object.FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None))
        {
            if (sr == null || sr.sprite == null)
                continue;
            HandleLoad(sr, sr.sprite);
        }
        foreach (var img in Object.FindObjectsByType<Image>(FindObjectsSortMode.None))
        {
            if (img == null || img.sprite == null)
                continue;
            HandleLoad(img, img.sprite);
        }
    }

    public static void PreloadAllT2DTextures()
    {
        // Load spritesheet overrides (in-place replacement data)
        BuildSpritesheetOverrides();

        // Load individual sprite replacements
        void ScanDirectory(string t2dRoot)
        {
            if (!Directory.Exists(t2dRoot)) return;
            foreach (var atlasDir in Directory.GetDirectories(t2dRoot))
            {
                foreach (var file in Directory.GetFiles(atlasDir, "*.png"))
                {
                    string spriteName = Path.GetFileNameWithoutExtension(file);
                    if (PreloadedT2DTextures.ContainsKey(spriteName) || LoadedT2DSprites.ContainsKey(spriteName))
                        continue;

                    Texture2D tex = TexUtil.LoadFromPNG(file);
                    if (tex != null)
                        PreloadedT2DTextures[spriteName] = tex;
                }
            }
        }

        ScanDirectory(Path.Combine(SpriteLoader.LoadPath, "T2D"));
        foreach (var packPath in Plugin.PluginPackPaths)
            ScanDirectory(Path.Combine(packPath, "Sprites", "T2D"));

        // Eagerly create replacement Sprites for any originals already in memory
        foreach (var original in Resources.FindObjectsOfTypeAll<Sprite>())
        {
            if (original == null || original.texture == null)
                continue;
            if (!IsT2DTexture(original.texture.name))
                continue;

            // Register this sprite name as confirmed T2D so enforcement
            // doesn't accidentally replace UI sprites with the same name.
            ConfirmedT2DSpriteNames.Add(original.name);

            if (PreloadedT2DTextures.TryGetValue(original.name, out var tex))
            {
                Sprite newSprite = Sprite.Create(tex,
                    new Rect(0, 0, tex.width, tex.height),
                    new Vector2(0.5f, 0.5f), original.pixelsPerUnit);
                newSprite.name = original.name;

                LoadedT2DSprites[original.name] = newSprite;
                PreloadedT2DTextures.Remove(original.name);

                string texName = original.texture.name;
                if (!SpriteAtlasMap.ContainsKey(texName))
                    SpriteAtlasMap[texName] = new HashSet<string>();
                SpriteAtlasMap[texName].Add(original.name);
            }
        }
    }

    // ================================================================
    //  Reload and invalidation
    // ================================================================

    public static void ReloadSpritesInScene()
    {
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

        // Rebuild everything from disk
        BuildSpritesheetOverrides();

        // Re-apply in-place texture swaps
        if (SpritesheetOverrides.Count > 0)
        {
            foreach (var tex in Resources.FindObjectsOfTypeAll<Texture2D>())
            {
                if (tex != null)
                    TrySwapTexture(tex);
            }
        }

        // Re-apply individual sprite replacements to all renderers
        // (renderers still have their old sprites, so sr.sprite != null)
        foreach (var spriteRenderer in Object.FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None))
        {
            if (spriteRenderer == null || spriteRenderer.sprite == null)
                continue;
            HandleLoad(spriteRenderer, spriteRenderer.sprite);
        }
        foreach (var image in Object.FindObjectsByType<Image>(FindObjectsSortMode.None))
        {
            if (image == null || image.sprite == null)
                continue;
            HandleLoad(image, image.sprite);
        }

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
    }

    public static void InvalidateSpritesheet(string texName)
    {
        // NOTE: This may be called from the file watcher's background thread.
        // Do NOT call Object.Destroy here — only clear caches.
        // Actual object cleanup happens in ReloadSpritesInScene on the main thread.
        string cleanName = CleanTextureName(texName);

        // Remove by raw name (the key used by BuildSpritesheetOverrides).
        SpritesheetOverrides.Remove(texName);
        ReplacedTextureIds.Clear();
        SkippedTextureIds.Clear();

        foreach (var kvp in SpriteAtlasMap.ToList())
        {
            if (CleanTextureName(kvp.Key) != cleanName)
                continue;

            foreach (var spriteName in kvp.Value.ToList())
                LoadedT2DSprites.Remove(spriteName);
        }
    }

    public static void InvalidateCache(string spriteName)
    {
        // NOTE: This is called from the file watcher's background thread.
        // Do NOT call Object.Destroy here — only clear caches.
        // Actual object cleanup happens in ReloadSpritesInScene on the main thread.
        LoadedT2DSprites.Remove(spriteName);
        PreloadedT2DTextures.Remove(spriteName);
        NegativeSpriteCache.Remove(spriteName);

        if (SpriteAtlasMap.TryGetValue(spriteName, out var atlasSprites))
        {
            foreach (var sprName in atlasSprites)
            {
                LoadedT2DSprites.Remove(sprName);
                PreloadedT2DTextures.Remove(sprName);
                NegativeSpriteCache.Remove(sprName);
            }
            SpriteAtlasMap.Remove(spriteName);
        }
    }

    // ================================================================
    //  Individual sprite loading (HandleLoad)
    // ================================================================

    private static void HandleLoad(object spriteContainer, Sprite sprite)
    {
        if (_handling || !HasT2DReplacements)
            return;

        _handling = true;
        try
        {
            // Check for a cached individual sprite replacement.
            // Gate on ConfirmedT2DSpriteNames to prevent replacing UI sprites
            // that happen to share a name with a T2D replacement file.
            if (ConfirmedT2DSpriteNames.Contains(sprite.name)
                && LoadedT2DSprites.TryGetValue(sprite.name, out var cached))
            {
                SetSprite(spriteContainer, cached);
                TrackT2DContainer(spriteContainer);
                return;
            }

            if (IsT2DTexture(sprite.texture.name))
            {
                ConfirmedT2DSpriteNames.Add(sprite.name);
                string cleanTexName = CleanTextureName(sprite.texture.name);

                // Bulk-load all individual replacement textures for this atlas on first encounter
                if (!SpriteAtlasMap.ContainsKey(sprite.texture.name))
                    PreloadT2DAtlasTextures(sprite.texture.name, cleanTexName);

                // Individual sprites take priority over spritesheets
                if (PreloadedT2DTextures.TryGetValue(sprite.name, out var spriteTex))
                {
                    Sprite newSprite = Sprite.Create(spriteTex,
                        new Rect(0, 0, spriteTex.width, spriteTex.height),
                        new Vector2(0.5f, 0.5f), sprite.pixelsPerUnit);
                    newSprite.name = sprite.name;

                    LoadedT2DSprites[sprite.name] = newSprite;
                    PreloadedT2DTextures.Remove(sprite.name);

                    SetSprite(spriteContainer, newSprite);
                    TrackT2DContainer(spriteContainer);
                    return;
                }

                // Spritesheet replacement is handled by TrySwapTexture (in-place on the texture).
                // No Sprite.Create needed — the texture itself has already been modified.
            }
            else
            {
                string texName = sprite.texture.name;

                // Non-atlas textures: individual sprite replacement
                if (LoadedT2DSprites.TryGetValue(texName, out var existing))
                {
                    SetSprite(spriteContainer, existing);
                    TrackT2DContainer(spriteContainer);
                    return;
                }

                // Skip filesystem lookup if we already know there's no replacement.
                if (NegativeSpriteCache.Contains(texName))
                    return;

                Texture2D spriteTex = FindT2DSprite(texName);
                if (spriteTex == null)
                {
                    NegativeSpriteCache.Add(texName);
                    return;
                }
                spriteTex.name = texName;
                Sprite newSprite = Sprite.Create(spriteTex, new Rect(0, 0, spriteTex.width, spriteTex.height), new Vector2(0.5f, 0.5f), sprite.pixelsPerUnit);
                newSprite.name = sprite.name;

                LoadedT2DSprites[texName] = newSprite;
                SetSprite(spriteContainer, newSprite);
                TrackT2DContainer(spriteContainer);
            }
        }
        finally
        {
            _handling = false;
        }
    }

    /// <summary>
    /// Sets the sprite on the container directly by type cast. Avoids reflection.
    /// </summary>
    private static void SetSprite(object container, Sprite sprite)
    {
        if (container is SpriteRenderer sr)
            sr.sprite = sprite;
        else if (container is Image img)
            img.sprite = sprite;
    }

    // ================================================================
    //  Atlas preloading (individual sprites from Sprites/T2D/)
    // ================================================================

    private static void PreloadT2DAtlasTextures(string textureName, string cleanTexName)
    {
        SpriteAtlasMap[textureName] = new HashSet<string>();

        void LoadFromDirectory(string dir)
        {
            if (!Directory.Exists(dir)) return;
            foreach (var file in Directory.GetFiles(dir, "*.png"))
            {
                string spriteName = Path.GetFileNameWithoutExtension(file);
                SpriteAtlasMap[textureName].Add(spriteName);

                if (PreloadedT2DTextures.ContainsKey(spriteName) || LoadedT2DSprites.ContainsKey(spriteName))
                    continue;

                Texture2D spriteTex = TexUtil.LoadFromPNG(file);
                if (spriteTex == null) continue;
                spriteTex.name = textureName;

                PreloadedT2DTextures[spriteName] = spriteTex;
            }
        }

        LoadFromDirectory(Path.Combine(SpriteLoader.LoadPath, "T2D", cleanTexName));
        string sanitizedRaw = SanitizeForFilesystem(textureName);
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
    //  Sprite lookup helpers
    // ================================================================

    private static void TrackT2DContainer(object spriteContainer)
    {
        if (spriteContainer is SpriteRenderer sr)
            KnownT2DSpriteRenderers.Add(sr);
        else if (spriteContainer is Image img)
            KnownT2DImages.Add(img);
    }

    private static Texture2D FindT2DSprite(string spriteName)
    {
        var files = Directory.GetFiles(SpriteLoader.LoadPath, spriteName + ".png", SearchOption.AllDirectories)
            .Where(f => Path.GetDirectoryName(f).EndsWith("T2D"));
        if (files.Any())
            return TexUtil.LoadFromPNG(files.First());

        foreach (var packPath in Plugin.PluginPackPaths)
        {
            if (!Directory.Exists(Path.Combine(packPath, "Sprites")))
                continue;
            var packFiles = Directory.GetFiles(Path.Combine(packPath, "Sprites"), spriteName + ".png", SearchOption.AllDirectories)
                .Where(f => Path.GetDirectoryName(f).EndsWith("T2D"));
            if (packFiles.Any())
                return TexUtil.LoadFromPNG(packFiles.First());
        }

        return null;
    }

    private static Texture2D FindT2DSprite(string texName, string spriteName)
    {
        var files = Directory.GetFiles(SpriteLoader.LoadPath, spriteName + ".png", SearchOption.AllDirectories)
            .Where(f => Path.GetDirectoryName(f).EndsWith(Path.Combine("T2D", texName)));
        if (files.Any())
            return TexUtil.LoadFromPNG(files.First());

        foreach (var packPath in Plugin.PluginPackPaths)
        {
            if (!Directory.Exists(Path.Combine(packPath, "Sprites")))
                continue;
            var packFiles = Directory.GetFiles(Path.Combine(packPath, "Sprites"), spriteName + ".png", SearchOption.AllDirectories)
                .Where(f => Path.GetDirectoryName(f).EndsWith(Path.Combine("T2D", texName)));
            if (packFiles.Any())
                return TexUtil.LoadFromPNG(packFiles.First());
        }

        return null;
    }

    // ================================================================
    //  Dumping
    // ================================================================

    public static void DumpAllT2DSprites()
    {
        HashSet<int> dumpedTextureIds = new();
        foreach (var sprite in Resources.FindObjectsOfTypeAll<Sprite>())
        {
            if (sprite == null || sprite.texture == null)
                continue;
            if (string.IsNullOrEmpty(sprite.name) || string.IsNullOrEmpty(sprite.texture.name))
                continue;

            if (IsT2DTexture(sprite.texture.name))
            {
                HandleDump(sprite);
            }
            else
            {
                // Non-T2D textures with Sprite objects (e.g. Particles_ash):
                // dump the full texture once, keyed by texture instance ID.
                if (!dumpedTextureIds.Add(sprite.texture.GetInstanceID()))
                    continue;
                HandleDump(sprite);
            }
        }

        DumpStandaloneTextures();
    }

    /// <summary>
    /// Dumps standalone Texture2D assets that have no corresponding Sprite objects
    /// and aren't T2D atlases. Sweeps all loaded Texture2D objects in memory rather
    /// than iterating specific renderer types, so nothing slips through the cracks.
    /// Saved to Dumps/T2D/_standalone/{textureName}.png.
    /// </summary>
    private static void DumpStandaloneTextures()
    {
        // Collect texture IDs that are referenced by Sprite objects — these are
        // already handled by the Sprite-based dump path and should be skipped.
        HashSet<int> spriteTextureIds = new();
        foreach (var sprite in Resources.FindObjectsOfTypeAll<Sprite>())
        {
            if (sprite != null && sprite.texture != null)
                spriteTextureIds.Add(sprite.texture.GetInstanceID());
        }

        string saveDirBase = Path.Combine(T2DDumpPath, "_standalone");
        int count = 0;

        foreach (var tex in Resources.FindObjectsOfTypeAll<Texture2D>())
        {
            if (tex == null || string.IsNullOrEmpty(tex.name))
                continue;

            // Skip T2D atlases — already dumped by HandleDump/DumpT2DAtlasTexture
            if (IsT2DTexture(tex.name))
                continue;

            // Skip textures that back Sprite objects — dumped via DumpAllT2DSprites
            if (spriteTextureIds.Contains(tex.GetInstanceID()))
                continue;

            // Skip Unity built-in and editor textures
            if (tex.name.StartsWith("unity_") || tex.name.StartsWith("UI"))
                continue;

            string safeName = SanitizeForFilesystem(tex.name);
            string savePath = Path.Combine(saveDirBase, safeName + ".png");
            if (File.Exists(savePath))
                continue;

            RenderTexture rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32);
            var previous = RenderTexture.active;
            Texture2D readable = null;

            try
            {
                Graphics.Blit(tex, rt);
                RenderTexture.active = rt;
                readable = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false);
                readable.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);
                readable.Apply();

                IOUtil.EnsureDirectoryExists(saveDirBase);
                File.WriteAllBytes(savePath, readable.EncodeToPNG());
                count++;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
                if (readable != null) Object.Destroy(readable);
            }
        }

        if (count > 0)
            Plugin.Logger.LogInfo($"[T2D] Dumped {count} standalone textures to {saveDirBase}");
    }

    /// <summary>
    /// Dumps a T2D atlas texture as a full PNG, plus extracts any individual
    /// Sprite frames that reference it.
    /// </summary>
    private static void DumpT2DAtlasTexture(Texture2D tex)
    {
        string cleanName = CleanTextureName(tex.name);
        string saveDir = Path.Combine(T2DDumpPath, cleanName);
        // Include dimensions in the atlas filename to avoid collisions when
        // multiple runtime atlases share the same clean name (e.g. Hornet
        // at both 2048x2048 and 4096x4096).
        string atlasPath = Path.Combine(saveDir, $"_atlas_{tex.width}x{tex.height}.png");

        if (File.Exists(atlasPath))
            return;

        // Blit to a readable RenderTexture
        RenderTexture rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32);
        var previous = RenderTexture.active;
        Texture2D readable = null;

        try
        {
            Graphics.Blit(tex, rt);
            RenderTexture.active = rt;
            readable = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false);
            readable.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);
            readable.Apply();

            IOUtil.EnsureDirectoryExists(saveDir);
            File.WriteAllBytes(atlasPath, readable.EncodeToPNG());
            Plugin.Logger.LogInfo($"[T2D] Dumped particle atlas: '{cleanName}' ({tex.width}x{tex.height})");
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
            if (readable != null) Object.Destroy(readable);
        }

        // Also dump any individual Sprite frames that reference this atlas
        foreach (var sprite in Resources.FindObjectsOfTypeAll<Sprite>())
        {
            if (sprite == null || sprite.texture == null)
                continue;
            if (sprite.texture.GetInstanceID() != tex.GetInstanceID())
                continue;
            if (string.IsNullOrEmpty(sprite.name))
                continue;
            HandleDump(sprite);
        }
    }

    private static void HandleDump(Sprite sprite)
    {
        if (IsT2DTexture(sprite.texture.name))
        {
            string cleanName = CleanTextureName(sprite.texture.name);
            string saveDir = Path.Combine(T2DDumpPath, cleanName);
            IOUtil.EnsureDirectoryExists(saveDir);
            string savePath = Path.Combine(saveDir, sprite.name + ".png");

            if (File.Exists(savePath))
                return;

            int width = (int)sprite.rect.width;
            int height = (int)sprite.rect.height;
            int renderLayer = 31;

            GameObject spriteGO = new GameObject("TempSpriteRenderer");
            SpriteRenderer tempSpriteRenderer = null;
            GameObject camGO = null;
            Camera cam = null;
            RenderTexture rt = null;
            Texture2D spriteTex = null;

            try
            {
                tempSpriteRenderer = spriteGO.AddComponent<SpriteRenderer>();
                tempSpriteRenderer.sprite = sprite;
                spriteGO.layer = renderLayer;
                spriteGO.transform.position = new Vector3(
                    (sprite.pivot.x - sprite.rect.width / 2) / sprite.pixelsPerUnit,
                    (sprite.pivot.y - sprite.rect.height / 2) / sprite.pixelsPerUnit,
                    0
                );

                camGO = new GameObject("TempCamera");
                cam = camGO.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0, 0, 0, 0);
                cam.orthographic = true;
                cam.cullingMask = 1 << renderLayer;
                cam.orthographicSize = height / sprite.pixelsPerUnit / 2f;
                cam.transform.position = new Vector3(0, 0, -10);

                rt = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32);
                rt.filterMode = FilterMode.Point;
                cam.targetTexture = rt;

                cam.Render();
                var previous = RenderTexture.active;
                RenderTexture.active = rt;
                spriteTex = new Texture2D(width, height, TextureFormat.ARGB32, false);
                spriteTex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                spriteTex.Apply();
                RenderTexture.active = previous;

                byte[] pngData = spriteTex.EncodeToPNG();
                File.WriteAllBytes(savePath, pngData);
            }
            finally
            {
                if (cam != null)
                    cam.targetTexture = null;
                if (spriteGO != null)
                    Object.DestroyImmediate(spriteGO);
                if (camGO != null)
                    Object.DestroyImmediate(camGO);
                if (rt != null)
                    Object.DestroyImmediate(rt);
                if (spriteTex != null)
                    Object.DestroyImmediate(spriteTex);
            }
        }
        else
        {
            string safeName = SanitizeForFilesystem(sprite.texture.name);
            string savePath = Path.Combine(T2DDumpPath, safeName + ".png");
            if (File.Exists(savePath))
                return;

            Plugin.Logger.LogInfo($"[T2D] Dumping non-atlas texture: '{sprite.texture.name}' -> {safeName}.png");

            RenderTexture spriteRT = null;
            Texture2D readableTex = null;

            try
            {
                spriteRT = TexUtil.GetReadable(sprite.texture);
                readableTex = new Texture2D(spriteRT.width, spriteRT.height, TextureFormat.ARGB32, false);
                var previous = RenderTexture.active;
                RenderTexture.active = spriteRT;
                readableTex.ReadPixels(new Rect(0, 0, spriteRT.width, spriteRT.height), 0, 0);
                readableTex.Apply();
                RenderTexture.active = previous;

                byte[] pngData = readableTex.EncodeToPNG();
                File.WriteAllBytes(savePath, pngData);
            }
            finally
            {
                if (spriteRT != null)
                    RenderTexture.ReleaseTemporary(spriteRT);
                if (readableTex != null)
                    Object.DestroyImmediate(readableTex);
            }
        }
    }

    // ================================================================
    //  Name utilities
    // ================================================================

    private static bool IsT2DTexture(string textureName)
    {
        return textureName.Contains("-BC7-") || textureName.Contains("DXT5|BC3-");
    }

    private static string SanitizeForFilesystem(string textureName)
    {
        return textureName
            .Replace("|", "_")
            .Replace("/", "_")
            .Replace("\\", "_")
            .Replace(":", "_");
    }

    /// <summary>
    /// Logs all Sprite objects that reference a given atlas texture.
    /// Helps modders identify when a shared atlas contains sprites from
    /// multiple systems (e.g. character animations + UI elements).
    /// </summary>
    private static void LogAtlasContents(Texture2D tex, string cleanName)
    {
        var sprites = Resources.FindObjectsOfTypeAll<Sprite>();
        var sharedSprites = new List<string>();

        foreach (var sprite in sprites)
        {
            if (sprite == null || sprite.texture == null)
                continue;
            if (sprite.texture.GetInstanceID() != tex.GetInstanceID())
                continue;
            sharedSprites.Add($"{sprite.name} ({sprite.rect.width}x{sprite.rect.height})");
        }

        if (sharedSprites.Count > 0)
        {
            Plugin.Logger.LogInfo(
                $"[T2D] Atlas '{cleanName}' ({tex.width}x{tex.height}, texture '{tex.name}') " +
                $"contains {sharedSprites.Count} sprites: " +
                string.Join(", ", sharedSprites));
        }
    }

    private static string CleanTextureName(string textureName)
    {
        if (textureName.Contains("-BC7-"))
        {
            string cleanName = textureName.Split(["-BC7-"], System.StringSplitOptions.None)[1];
            cleanName = string.Join("-", cleanName.Split('-').Take(cleanName.Split('-').Length - 1));
            return cleanName;
        }
        if (textureName.Contains("DXT5|BC3-") || textureName.Contains("DXT5_BC3-"))
        {
            string delimiter = textureName.Contains("DXT5|BC3-") ? "DXT5|BC3-" : "DXT5_BC3-";
            string cleanName = textureName.Split([delimiter], System.StringSplitOptions.None)[1];
            cleanName = string.Join("-", cleanName.Split('-').Take(cleanName.Split('-').Length - 1));
            return cleanName;
        }
        return textureName;
    }
}
