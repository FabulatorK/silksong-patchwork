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

    private static readonly Dictionary<string, Sprite> LoadedT2DSprites = new();
    private static readonly Dictionary<string, Texture2D> PreloadedT2DTextures = new();
    private static readonly Dictionary<string, HashSet<string>> SpriteAtlasMap = new();
    private static readonly Dictionary<string, Texture2D> LoadedT2DSpritesheets = new();

    private static readonly Dictionary<int, string> TrackedSpriteNames = new();
    private static readonly HashSet<SpriteRenderer> KnownT2DSpriteRenderers = new();
    private static readonly HashSet<Image> KnownT2DImages = new();
    private static bool _enforcing = false;
    private static bool _handling = false;


    [HarmonyPostfix]
    [HarmonyPatch(typeof(SpriteRenderer), nameof(SpriteRenderer.sprite), MethodType.Setter)]
    public static void SetSpritePostfix(SpriteRenderer __instance, Sprite value)
    {
        if (_handling || _enforcing || __instance == null || value == null || __instance.gameObject.name == "TempSpriteRenderer")
            return;
        TrackedSpriteNames[__instance.GetInstanceID()] = value.name;

        if (Plugin.Config.DumpSprites && !string.IsNullOrEmpty(value.name) && !string.IsNullOrEmpty(value.texture.name))
            HandleDump(value);

        HandleLoad(__instance, value);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Image), nameof(Image.sprite), MethodType.Setter)]
    public static void SetImageSpritePostfix(Image __instance, Sprite value)
    {
        if (_handling || _enforcing || __instance == null || value == null)
            return;
        TrackedSpriteNames[__instance.GetInstanceID()] = value.name;

        if (Plugin.Config.DumpSprites && !string.IsNullOrEmpty(value.name) && !string.IsNullOrEmpty(value.texture.name))
            HandleDump(value);

        HandleLoad(__instance, value);
    }

    public static void CheckForUninitializedSprites()
    {
        foreach (var spriteRenderer in Object.FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None))
        {
            if (spriteRenderer == null || spriteRenderer.sprite == null)
                continue;

            int id = spriteRenderer.GetInstanceID();
            string currentName = spriteRenderer.sprite.name;

            bool nameChanged = !TrackedSpriteNames.TryGetValue(id, out string lastSprite) || lastSprite != currentName;
            // Catch stale TrackedSpriteNames entries from recycled instance IDs after scene changes:
            // if a cached replacement exists but this renderer isn't using it, re-trigger the setter
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
        _enforcing = true;
        try
        {
            KnownT2DSpriteRenderers.RemoveWhere(sr => sr == null);
            foreach (var sr in KnownT2DSpriteRenderers)
            {
                if (sr.sprite == null) continue;
                if (LoadedT2DSprites.TryGetValue(sr.sprite.name, out var replacement) && sr.sprite != replacement)
                    sr.sprite = replacement;
            }

            KnownT2DImages.RemoveWhere(img => img == null);
            foreach (var img in KnownT2DImages)
            {
                if (img.sprite == null) continue;
                if (LoadedT2DSprites.TryGetValue(img.sprite.name, out var replacement) && img.sprite != replacement)
                    img.sprite = replacement;
            }
        }
        finally
        {
            _enforcing = false;
        }
    }

    public static void ApplyT2DReplacementsInScene()
    {
        // Eagerly convert any remaining PreloadedT2DTextures into LoadedT2DSprites
        // using original sprites now in memory, so replacements are instant when
        // VFX or other systems set sprites on renderers later this frame.
        if (PreloadedT2DTextures.Count > 0)
        {
            foreach (var original in Resources.FindObjectsOfTypeAll<Sprite>())
            {
                if (original == null || original.texture == null)
                    continue;
                if (!original.texture.name.Contains("-BC7-") && !original.texture.name.Contains("DXT5|BC3-"))
                    continue;
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

        // Eagerly create sprites from T2D spritesheets for any originals not yet replaced
        foreach (var original in Resources.FindObjectsOfTypeAll<Sprite>())
        {
            if (original == null || original.texture == null)
                continue;
            if (LoadedT2DSprites.ContainsKey(original.name))
                continue;
            if (!original.texture.name.Contains("-BC7-") && !original.texture.name.Contains("DXT5|BC3-"))
                continue;

            string cleanTexName = CleanTextureName(original.texture.name);
            Texture2D spritesheet = FindT2DSpritesheet(cleanTexName, original.texture.name);
            if (spritesheet == null)
                continue;

            Sprite newSprite = CreateSpriteFromSpritesheet(spritesheet, original);
            if (newSprite == null)
                continue;

            LoadedT2DSprites[original.name] = newSprite;

            string texName = original.texture.name;
            if (!SpriteAtlasMap.ContainsKey(texName))
                SpriteAtlasMap[texName] = new HashSet<string>();
            SpriteAtlasMap[texName].Add(original.name);
        }

        // Now apply replacements to all active renderers/images
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

    public static void ReloadSpritesInScene()
    {
        // Collect spritesheet textures so we don't double-destroy them
        var spritesheetTextures = new HashSet<Texture2D>(LoadedT2DSpritesheets.Values);

        // Destroy old sprites before clearing cache
        foreach (var sprite in LoadedT2DSprites.Values)
        {
            if (sprite != null && sprite.texture != null && !spritesheetTextures.Contains(sprite.texture))
                Object.Destroy(sprite.texture);
            if (sprite != null)
                Object.Destroy(sprite);
        }
        LoadedT2DSprites.Clear();
        foreach (var tex in PreloadedT2DTextures.Values)
        {
            if (tex != null)
                Object.Destroy(tex);
        }
        PreloadedT2DTextures.Clear();
        SpriteAtlasMap.Clear();
        foreach (var tex in LoadedT2DSpritesheets.Values)
        {
            if (tex != null)
                Object.Destroy(tex);
        }
        LoadedT2DSpritesheets.Clear();
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
    }

    public static void InvalidateSpritesheet(string texName)
    {
        // texName may be a clean name or a raw/sanitized filename from the file watcher
        string cleanTexName = CleanTextureName(texName);

        if (!LoadedT2DSpritesheets.TryGetValue(cleanTexName, out var sheetTex))
            return;

        // Remove the spritesheet texture itself
        if (sheetTex != null)
            Object.Destroy(sheetTex);
        LoadedT2DSpritesheets.Remove(cleanTexName);

        // Invalidate all sprites that were created from this spritesheet
        // by finding atlas map entries whose clean name matches
        foreach (var kvp in SpriteAtlasMap.ToList())
        {
            if (CleanTextureName(kvp.Key) != cleanTexName)
                continue;

            foreach (var spriteName in kvp.Value.ToList())
            {
                if (LoadedT2DSprites.TryGetValue(spriteName, out var sprite))
                {
                    // Only destroy sprites whose texture is the spritesheet (not individual replacements)
                    if (sprite != null)
                        Object.Destroy(sprite);
                    LoadedT2DSprites.Remove(spriteName);
                }
            }
        }
    }

    public static void InvalidateCache(string spriteName)
    {
        // Destroy before removing from cache
        if (LoadedT2DSprites.TryGetValue(spriteName, out var sprite))
        {
            if (sprite != null && sprite.texture != null && !LoadedT2DSpritesheets.ContainsValue(sprite.texture))
                Object.Destroy(sprite.texture);
            if (sprite != null)
                Object.Destroy(sprite);
            LoadedT2DSprites.Remove(spriteName);
        }

        if (PreloadedT2DTextures.TryGetValue(spriteName, out var tex))
        {
            if (tex != null)
                Object.Destroy(tex);
            PreloadedT2DTextures.Remove(spriteName);
        }

        if (SpriteAtlasMap.TryGetValue(spriteName, out var atlasSprites))
        {
            foreach (var sprName in atlasSprites)
            {
                if (LoadedT2DSprites.TryGetValue(sprName, out var atlasSprite))
                {
                    if (atlasSprite != null && atlasSprite.texture != null)
                        Object.Destroy(atlasSprite.texture);
                    if (atlasSprite != null)
                        Object.Destroy(atlasSprite);
                    LoadedT2DSprites.Remove(sprName);
                }
                if (PreloadedT2DTextures.TryGetValue(sprName, out var atlasTex))
                {
                    if (atlasTex != null)
                        Object.Destroy(atlasTex);
                    PreloadedT2DTextures.Remove(sprName);
                }
            }
            SpriteAtlasMap.Remove(spriteName);
        }
    }

    private static void HandleLoad(object spriteContainer, Sprite sprite)
    {
        if (_handling)
            return;

        var spriteSetter = spriteContainer.GetType().GetProperty("sprite").GetSetMethod();
        if (spriteSetter == null)
        {
            Plugin.Logger.LogError($"T2DHandler: Could not find sprite setter for {spriteContainer.GetType().Name}");
            return;
        }

        _handling = true;
        try
        {
            if (LoadedT2DSprites.ContainsKey(sprite.name))
            {
                spriteSetter.Invoke(spriteContainer, [LoadedT2DSprites[sprite.name]]);
                TrackT2DContainer(spriteContainer);
                return;
            }

            if (sprite.texture.name.Contains("-BC7-") || sprite.texture.name.Contains("DXT5|BC3-"))
            {
                string cleanTexName = CleanTextureName(sprite.texture.name);

                // Bulk-load all replacement textures for this atlas on first encounter (disk I/O)
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

                    spriteSetter.Invoke(spriteContainer, [newSprite]);
                    TrackT2DContainer(spriteContainer);
                    return;
                }

                // Fall back to T2D spritesheet if no individual sprite replacement exists
                Texture2D spritesheet = FindT2DSpritesheet(cleanTexName, sprite.texture.name);
                if (spritesheet != null)
                {
                    Sprite newSprite = CreateSpriteFromSpritesheet(spritesheet, sprite);
                    if (newSprite != null)
                    {
                        LoadedT2DSprites[sprite.name] = newSprite;

                        if (!SpriteAtlasMap.ContainsKey(sprite.texture.name))
                            SpriteAtlasMap[sprite.texture.name] = new HashSet<string>();
                        SpriteAtlasMap[sprite.texture.name].Add(sprite.name);

                        spriteSetter.Invoke(spriteContainer, [newSprite]);
                        TrackT2DContainer(spriteContainer);
                    }
                }
            }
            else
            {
                if (LoadedT2DSprites.ContainsKey(sprite.texture.name))
                {
                    spriteSetter.Invoke(spriteContainer, [LoadedT2DSprites[sprite.texture.name]]);
                    TrackT2DContainer(spriteContainer);
                    return;
                }

                Texture2D spriteTex = FindT2DSprite(sprite.texture.name);
                if (spriteTex == null)
                    return;
                spriteTex.name = sprite.texture.name;
                Sprite newSprite = Sprite.Create(spriteTex, new Rect(0, 0, spriteTex.width, spriteTex.height), new Vector2(0.5f, 0.5f), sprite.pixelsPerUnit);
                newSprite.name = sprite.name;

                // Texture ownership transfers to sprite
                LoadedT2DSprites[sprite.texture.name] = newSprite;
                spriteSetter.Invoke(spriteContainer, [newSprite]);
                TrackT2DContainer(spriteContainer);
            }
        }
        finally
        {
            _handling = false;
        }
    }

    private static void PreloadT2DAtlasTextures(string textureName, string cleanTexName)
    {
        // Initialize the atlas map entry so we don't re-scan on subsequent calls
        SpriteAtlasMap[textureName] = new HashSet<string>();

        void LoadFromDirectory(string dir)
        {
            if (!Directory.Exists(dir)) return;
            foreach (var file in Directory.GetFiles(dir, "*.png"))
            {
                string spriteName = Path.GetFileNameWithoutExtension(file);
                // Always register in atlas map for invalidation tracking
                SpriteAtlasMap[textureName].Add(spriteName);

                if (PreloadedT2DTextures.ContainsKey(spriteName) || LoadedT2DSprites.ContainsKey(spriteName))
                    continue;

                Texture2D spriteTex = TexUtil.LoadFromPNG(file);
                if (spriteTex == null) continue;
                spriteTex.name = textureName;

                PreloadedT2DTextures[spriteName] = spriteTex;
            }
        }

        // Try clean name directory, then raw/sanitized name for T2D customizer compat
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

    public static void PreloadAllT2DTextures()
    {
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

        // Eagerly create replacement Sprites for any originals already in memory,
        // so VFX sprites are ready in LoadedT2DSprites before the effect ever plays.
        foreach (var original in Resources.FindObjectsOfTypeAll<Sprite>())
        {
            if (original == null || original.texture == null)
                continue;
            if (!original.texture.name.Contains("-BC7-") && !original.texture.name.Contains("DXT5|BC3-"))
                continue;

            // Individual sprites take priority
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
                continue;
            }

            // Fall back to spritesheet
            if (LoadedT2DSprites.ContainsKey(original.name))
                continue;

            string cleanTexName = CleanTextureName(original.texture.name);
            Texture2D spritesheet = FindT2DSpritesheet(cleanTexName, original.texture.name);
            if (spritesheet == null)
                continue;

            Sprite sheetSprite = CreateSpriteFromSpritesheet(spritesheet, original);
            if (sheetSprite == null)
                continue;

            LoadedT2DSprites[original.name] = sheetSprite;

            string tName = original.texture.name;
            if (!SpriteAtlasMap.ContainsKey(tName))
                SpriteAtlasMap[tName] = new HashSet<string>();
            SpriteAtlasMap[tName].Add(original.name);
        }
    }

    private static void TrackT2DContainer(object spriteContainer)
    {
        if (spriteContainer is SpriteRenderer sr)
            KnownT2DSpriteRenderers.Add(sr);
        else if (spriteContainer is Image img)
            KnownT2DImages.Add(img);
    }

    public static void DumpAllT2DSprites()
    {
        foreach (var sprite in Resources.FindObjectsOfTypeAll<Sprite>())
        {
            if (sprite == null || sprite.texture == null)
                continue;
            if (string.IsNullOrEmpty(sprite.name) || string.IsNullOrEmpty(sprite.texture.name))
                continue;
            if (sprite.texture.name.Contains("-BC7-") || sprite.texture.name.Contains("DXT5|BC3-"))
                HandleDump(sprite);
        }
    }

    private static void HandleDump(Sprite sprite)
    {
        if (sprite.texture.name.Contains("-BC7-") || sprite.texture.name.Contains("DXT5|BC3-"))
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
                // Cleanup everything
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
            string savePath = Path.Combine(T2DDumpPath, sprite.texture.name + ".png");
            if (File.Exists(savePath))
                return;

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
                // Cleanup
                if (spriteRT != null)
                    RenderTexture.ReleaseTemporary(spriteRT);
                if (readableTex != null)
                    Object.DestroyImmediate(readableTex);
            }
        }
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

    private static Texture2D FindT2DSpritesheet(string cleanTexName, string rawTexName = null)
    {
        if (LoadedT2DSpritesheets.TryGetValue(cleanTexName, out var cached))
            return cached;

        // Build candidate filenames: clean name first, then raw/sanitized name for
        // backwards compatibility with T2D customizer exports that use the full atlas name
        var candidates = new List<string> { cleanTexName };
        if (rawTexName != null)
        {
            string sanitized = SanitizeForFilesystem(rawTexName);
            if (sanitized != cleanTexName)
                candidates.Add(sanitized);
        }

        foreach (var candidate in candidates)
        {
            string path = Path.Combine(T2DAtlasLoadPath, candidate + ".png");
            if (File.Exists(path))
            {
                var tex = TexUtil.LoadFromPNG(path);
                if (tex != null)
                {
                    LoadedT2DSpritesheets[cleanTexName] = tex;
                    return tex;
                }
            }
        }

        foreach (var packPath in Plugin.PluginPackPaths)
        {
            foreach (var candidate in candidates)
            {
                string packFile = Path.Combine(packPath, "Spritesheets", "T2D", candidate + ".png");
                if (File.Exists(packFile))
                {
                    var tex = TexUtil.LoadFromPNG(packFile);
                    if (tex != null)
                    {
                        LoadedT2DSpritesheets[cleanTexName] = tex;
                        return tex;
                    }
                }
            }
        }

        return null;
    }

    private static Sprite CreateSpriteFromSpritesheet(Texture2D spritesheet, Sprite original)
    {
        // Use the original sprite's rect/pivot/ppu against the new spritesheet texture.
        // The spritesheet must have the same layout as the original atlas.
        Rect rect = original.rect;

        // Clamp rect to spritesheet bounds in case of dimension mismatch
        if (rect.xMax > spritesheet.width || rect.yMax > spritesheet.height)
        {
            Plugin.Logger.LogWarning(
                $"T2D spritesheet size mismatch for {original.name}: " +
                $"sprite rect ({rect.x},{rect.y},{rect.width},{rect.height}) " +
                $"exceeds spritesheet ({spritesheet.width}x{spritesheet.height}). Skipping.");
            return null;
        }

        Vector2 pivot = new(
            (original.pivot.x) / rect.width,
            (original.pivot.y) / rect.height
        );

        Sprite newSprite = Sprite.Create(spritesheet, rect, pivot, original.pixelsPerUnit);
        newSprite.name = original.name;
        return newSprite;
    }

    private static string SanitizeForFilesystem(string textureName)
    {
        return textureName.Replace("|", "_");
    }

    private static string CleanTextureName(string textureName)
    {
        if (textureName.Contains("-BC7-"))
        {
            string cleanName = textureName.Split(["-BC7-"], System.StringSplitOptions.None)[1];
            cleanName = string.Join("-", cleanName.Split('-').Take(cleanName.Split('-').Length - 1));
            return cleanName;
        }
        // Handle both in-game name (DXT5|BC3-) and filesystem name (DXT5_BC3-)
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