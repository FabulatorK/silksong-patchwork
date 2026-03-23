using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using Patchwork.Util;

namespace Patchwork.Handlers;

public static partial class T2DHandler
{
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
            if (IsT2DTexture(sprite.texture.name))
            {
                string cleanTexName = CleanTextureName(sprite.texture.name);
                string key = SpriteKey(cleanTexName, sprite.name);

                // Confirm this sprite as T2D (gates against UI sprites sharing a name
                // with a T2D replacement file). IsT2DTexture already passed above, so
                // any sprite reaching here is from a genuine T2D atlas.
                ConfirmedT2DSpriteNames.Add(sprite.name);

                // Check for a cached individual sprite replacement.
                // This must come AFTER the confirm-add above: the eager pass in
                // PreloadAllT2DTextures may have already created the LoadedT2DSprites
                // entry before this renderer was first seen, so we must not gate on
                // a prior ConfirmedT2DSpriteNames check or the replacement is silently missed.
                if (LoadedT2DSprites.TryGetValue(key, out var cached))
                {
                    SetSprite(spriteContainer, cached);
                    TrackT2DContainer(spriteContainer);
                    return;
                }

                // Bulk-load all individual replacement textures for this atlas on first encounter
                if (!SpriteAtlasMap.ContainsKey(sprite.texture.name))
                    PreloadT2DAtlasTextures(sprite.texture.name, cleanTexName);

                // Individual sprites take priority over spritesheets.
                // Try atlas-qualified key first, then fall back to plain sprite name
                // (supports flat T2D/[SpriteName].png layout).
                Texture2D spriteTex = null;
                string matchedKey = null;
                if (PreloadedT2DTextures.TryGetValue(key, out spriteTex))
                    matchedKey = key;
                else if (PreloadedT2DTextures.TryGetValue(sprite.name, out spriteTex))
                    matchedKey = sprite.name;

                if (spriteTex != null)
                {
                    // Ensure the replacement texture carries the original atlas name
                    // so enforcement can recover the composite key later.
                    spriteTex.name = sprite.texture.name;

                    Sprite newSprite = Sprite.Create(spriteTex,
                        new Rect(0, 0, spriteTex.width, spriteTex.height),
                        new Vector2(0.5f, 0.5f), sprite.pixelsPerUnit);
                    newSprite.name = sprite.name;

                    // Always store under the atlas-qualified key for consistent lookup
                    LoadedT2DSprites[key] = newSprite;
                    PreloadedT2DTextures.Remove(matchedKey);

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

    private static void SetSprite(object container, Sprite sprite)
    {
        if (container is SpriteRenderer sr)
            sr.sprite = sprite;
        else if (container is Image img)
            img.sprite = sprite;
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
}
