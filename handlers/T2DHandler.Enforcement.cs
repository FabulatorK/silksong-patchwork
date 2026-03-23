using UnityEngine;
using UnityEngine.UI;

namespace Patchwork.Handlers;

public static partial class T2DHandler
{
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
            var sprite = spriteRenderer.sprite;

            if (!ConfirmedT2DSpriteNames.Contains(sprite.name))
                continue;

            string key = (sprite.texture != null && IsT2DTexture(sprite.texture.name))
                ? SpriteKey(CleanTextureName(sprite.texture.name), sprite.name)
                : sprite.name;

            bool nameChanged = !TrackedSpriteNames.TryGetValue(id, out string lastSprite) || lastSprite != sprite.name;
            bool replacementMissing = !nameChanged
                && LoadedT2DSprites.TryGetValue(key, out var cached)
                && sprite != cached;

            if (nameChanged || replacementMissing)
            {
                TrackedSpriteNames[id] = sprite.name;
                spriteRenderer.sprite = spriteRenderer.sprite;
            }
        }

        foreach (var image in Object.FindObjectsByType<Image>(FindObjectsSortMode.None))
        {
            if (image == null || image.sprite == null)
                continue;

            int id = image.GetInstanceID();
            var sprite = image.sprite;

            if (!ConfirmedT2DSpriteNames.Contains(sprite.name))
                continue;

            string key = (sprite.texture != null && IsT2DTexture(sprite.texture.name))
                ? SpriteKey(CleanTextureName(sprite.texture.name), sprite.name)
                : sprite.name;

            bool nameChanged = !TrackedSpriteNames.TryGetValue(id, out string lastSprite) || lastSprite != sprite.name;
            bool replacementMissing = !nameChanged
                && LoadedT2DSprites.TryGetValue(key, out var cached)
                && sprite != cached;

            if (nameChanged || replacementMissing)
            {
                TrackedSpriteNames[id] = sprite.name;
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
            // Only re-check tracked renderers/images that we've already identified
            // as T2D-backed. This avoids the expensive FindObjectsByType scene scan
            // every frame — new/cloned renderers are discovered by
            // CheckForUninitializedSprites on a periodic interval instead.
            foreach (var sr in KnownT2DSpriteRenderers)
            {
                if (sr == null || sr.sprite == null) continue;
                var sprite = sr.sprite;

                if (!ConfirmedT2DSpriteNames.Contains(sprite.name))
                    continue;

                string key = (sprite.texture != null && IsT2DTexture(sprite.texture.name))
                    ? SpriteKey(CleanTextureName(sprite.texture.name), sprite.name)
                    : sprite.name;

                if (LoadedT2DSprites.TryGetValue(key, out var replacement))
                {
                    if (sprite != replacement)
                        sr.sprite = replacement;
                }
            }

            foreach (var img in KnownT2DImages)
            {
                if (img == null || img.sprite == null) continue;
                var sprite = img.sprite;

                if (!ConfirmedT2DSpriteNames.Contains(sprite.name))
                    continue;

                string key = (sprite.texture != null && IsT2DTexture(sprite.texture.name))
                    ? SpriteKey(CleanTextureName(sprite.texture.name), sprite.name)
                    : sprite.name;

                if (LoadedT2DSprites.TryGetValue(key, out var replacement))
                {
                    if (sprite != replacement)
                        img.sprite = replacement;
                }
            }

            // Clean up destroyed references periodically (cached delegates to avoid per-frame closure allocations)
            KnownT2DSpriteRenderers.RemoveWhere(_srNullPredicate);
            KnownT2DImages.RemoveWhere(_imgNullPredicate);
        }
        finally
        {
            _enforcing = false;
        }
    }
}
