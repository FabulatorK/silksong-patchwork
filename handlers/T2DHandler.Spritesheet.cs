using System.IO;
using UnityEngine;
using Patchwork.Util;

namespace Patchwork.Handlers;

public static partial class T2DHandler
{
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
            SkippedTextureIds.Add(id);
            return false;
        }

        if (tex.LoadImage(data.PngData))
        {
            ReplacedTextureIds.Add(id);
            Plugin.Logger.LogInfo(
                $"[T2D] Spritesheet applied in-place: '{cleanName}' " +
                $"(texture '{tex.name}', {tex.width}x{tex.height})");

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
}
