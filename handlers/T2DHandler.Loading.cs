using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Patchwork.Util;

namespace Patchwork.Handlers;

public static partial class T2DHandler
{
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

                string cleanTexName = CleanTextureName(original.texture.name);
                string key = SpriteKey(cleanTexName, original.name);
                if (!PreloadedT2DTextures.TryGetValue(key, out var tex))
                    continue;

                tex.name = original.texture.name;

                Sprite newSprite = Sprite.Create(tex,
                    new Rect(0, 0, tex.width, tex.height),
                    new Vector2(0.5f, 0.5f), original.pixelsPerUnit);
                newSprite.name = original.name;

                LoadedT2DSprites[key] = newSprite;
                PreloadedT2DTextures.Remove(key);

                string texName = original.texture.name;
                if (!SpriteAtlasMap.ContainsKey(texName))
                    SpriteAtlasMap[texName] = new HashSet<string>();
                SpriteAtlasMap[texName].Add(original.name);
            }
        }

        // Pass 3: Apply individual sprite replacements to all renderers/images (including inactive).
        // Uses Resources.FindObjectsOfTypeAll to ensure nothing is missed at scene load,
        // at the cost of a longer load. Skip entirely when no T2D replacements are loaded.
        if (!HasT2DReplacements)
            return;

        foreach (var sr in Resources.FindObjectsOfTypeAll<SpriteRenderer>())
        {
            if (sr == null || sr.sprite == null)
                continue;
            HandleLoad(sr, sr.sprite);
        }
        foreach (var img in Resources.FindObjectsOfTypeAll<Image>())
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
            if (!Directory.Exists(t2dRoot))
                return;

            // Structured path: T2D/[AtlasName]/[SpriteName].png
            var atlasDirs = Directory.GetDirectories(t2dRoot);
            foreach (var atlasDir in atlasDirs)
            {
                string atlasName = Path.GetFileName(atlasDir);
                var files = Directory.GetFiles(atlasDir, "*.png");
                foreach (var file in files)
                {
                    string spriteName = Path.GetFileNameWithoutExtension(file);
                    string key = SpriteKey(atlasName, spriteName);
                    if (PreloadedT2DTextures.ContainsKey(key) || LoadedT2DSprites.ContainsKey(key))
                        continue;

                    Texture2D tex = TexUtil.LoadFromPNG(file);
                    if (tex != null)
                        PreloadedT2DTextures[key] = tex;
                }
            }

            // Flat path: T2D/[SpriteName].png — keyed by sprite name only,
            // acts as a fallback when no atlas-qualified match exists.
            foreach (var file in Directory.GetFiles(t2dRoot, "*.png"))
            {
                string spriteName = Path.GetFileNameWithoutExtension(file);
                if (PreloadedT2DTextures.ContainsKey(spriteName) || LoadedT2DSprites.ContainsKey(spriteName))
                    continue;

                Texture2D tex = TexUtil.LoadFromPNG(file);
                if (tex != null)
                    PreloadedT2DTextures[spriteName] = tex;
            }
        }

        string mainT2DPath = Path.Combine(SpriteLoader.LoadPath, "T2D");
        ScanDirectory(mainT2DPath);
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

            string cleanTexName = CleanTextureName(original.texture.name);
            string key = SpriteKey(cleanTexName, original.name);

            // Try atlas-qualified key first, then fall back to plain sprite name
            // (supports flat T2D/[SpriteName].png layout)
            string matchedKey = null;
            Texture2D tex = null;
            if (PreloadedT2DTextures.TryGetValue(key, out tex))
                matchedKey = key;
            else if (PreloadedT2DTextures.TryGetValue(original.name, out tex))
                matchedKey = original.name;

            if (matchedKey != null)
            {
                // Ensure the replacement texture carries the original atlas name
                // so enforcement can recover the composite key later.
                tex.name = original.texture.name;

                Sprite newSprite = Sprite.Create(tex,
                    new Rect(0, 0, tex.width, tex.height),
                    new Vector2(0.5f, 0.5f), original.pixelsPerUnit);
                newSprite.name = original.name;

                // Always store under the atlas-qualified key for consistent lookup
                LoadedT2DSprites[key] = newSprite;
                PreloadedT2DTextures.Remove(matchedKey);

                string texName = original.texture.name;
                if (!SpriteAtlasMap.ContainsKey(texName))
                    SpriteAtlasMap[texName] = new HashSet<string>();
                SpriteAtlasMap[texName].Add(original.name);
            }
        }
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

                string key = SpriteKey(cleanTexName, spriteName);
                if (PreloadedT2DTextures.ContainsKey(key) || LoadedT2DSprites.ContainsKey(key))
                    continue;

                Texture2D spriteTex = TexUtil.LoadFromPNG(file);
                if (spriteTex == null) continue;
                spriteTex.name = textureName;

                PreloadedT2DTextures[key] = spriteTex;
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
}
