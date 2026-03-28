using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Patchwork.Util;

namespace Patchwork.Handlers;

/// <summary>
/// In-place spritesheet replacement for T2D textures (Customizer T2D approach).
/// Partial of T2DLoader — spritesheet state and methods live here.
/// </summary>
public static partial class T2DLoader
{
    // Keyed by raw filename (without extension). TrySwapTexture looks up by raw texture name
    // first, then falls back to clean name — so two atlases sharing the same clean name
    // (e.g. sactx-1-2048x2048-BC7-Hornet-* and sactx-0-4096x4096-BC7-Hornet-*)
    // each get their own replacement without colliding.
    private static readonly System.Collections.Generic.Dictionary<string, (byte[] PngData, int Width, int Height)>
        SpritesheetOverrides = new(System.StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<int> ReplacedTextureIds = new();
    private static readonly HashSet<int> SkippedTextureIds = new();

    // Original pixel data (PNG bytes) captured before the first in-place overwrite.
    // Keyed by tex.name (runtime texture name).  Never cleared — used to restore vanilla
    // pixels when a pack is disabled.
    private static readonly Dictionary<string, byte[]> _originalTextureData =
        new(System.StringComparer.OrdinalIgnoreCase);

    // Maps spritesheet rawName → source pack path (null = base folder). Used for conflict reporting.
    private static readonly Dictionary<string, string> _sheetProviders =
        new(System.StringComparer.OrdinalIgnoreCase);

    internal static bool HasStoredOriginals => _originalTextureData.Count > 0;

    // ================================================================
    //  In-place texture swap
    // ================================================================

    /// <summary>
    /// Replaces a Texture2D's pixel data in-place using a spritesheet override.
    /// Same texture object, new pixels — all sprites referencing it automatically
    /// display the new art without any rect/pivot changes.
    /// </summary>
    internal static bool TrySwapTexture(Texture2D tex)
    {
        if (tex == null)
            return false;

        int id = tex.GetInstanceID();
        if (ReplacedTextureIds.Contains(id) || SkippedTextureIds.Contains(id))
            return ReplacedTextureIds.Contains(id);

        string cleanName = T2DUtil.CleanTextureName(tex.name);
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

        // Capture original pixel data before the first overwrite so we can restore it later.
        if (!_originalTextureData.ContainsKey(tex.name))
        {
            byte[] original = CaptureTextureAsPng(tex);
            if (original != null)
                _originalTextureData[tex.name] = original;
        }

        if (tex.LoadImage(data.PngData))
        {
            ReplacedTextureIds.Add(id);
            Plugin.Logger.LogInfo(
                $"[T2D] Spritesheet applied in-place: '{cleanName}' " +
                $"(texture '{tex.name}', {tex.width}x{tex.height})");

            if (Plugin.Config.ConvertSpritesheets)
                ConvertSpritesheet(tex, cleanName);

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
        _sheetProviders.Clear();

        void ScanDirectory(string dir, string sourcePack)
        {
            if (!Directory.Exists(dir)) return;
            foreach (var file in Directory.GetFiles(dir, "*.png", SearchOption.TopDirectoryOnly))
            {
                string rawName = Path.GetFileNameWithoutExtension(file);
                if (SpritesheetOverrides.ContainsKey(rawName))
                {
                    ConflictTracker.Record("t2d-sheet", rawName,
                        _sheetProviders.GetValueOrDefault(rawName), sourcePack);
                    continue;
                }

                try
                {
                    byte[] pngData = File.ReadAllBytes(file);
                    if (!TryReadPngDimensions(pngData, out int w, out int h))
                    {
                        Plugin.Logger.LogWarning($"[T2D] Could not read PNG dimensions for spritesheet '{rawName}', skipping.");
                        continue;
                    }
                    SpritesheetOverrides[rawName] = (pngData, w, h);
                    _sheetProviders[rawName] = sourcePack;
                    Plugin.Logger.LogInfo($"[T2D] Loaded spritesheet override: '{rawName}' ({w}x{h})");
                }
                catch (System.Exception ex)
                {
                    Plugin.Logger.LogWarning($"[T2D] Failed to load spritesheet '{file}': {ex.Message}");
                }
            }
        }

        ScanDirectory(AtlasLoadPath, null);
        foreach (var packPath in Plugin.PluginPackPaths)
            ScanDirectory(Path.Combine(packPath, "Spritesheets", "T2D"), packPath);
    }

    /// <summary>
    /// Reads PNG image dimensions from the IHDR chunk without decoding the image or touching the GPU.
    /// PNG spec: 8-byte signature, then IHDR chunk (4-byte length, 4-byte type tag,
    /// 4-byte width, 4-byte height — all big-endian uint32).
    /// </summary>
    private static bool TryReadPngDimensions(byte[] data, out int width, out int height)
    {
        width = height = 0;
        // Minimum valid size: 8 (sig) + 4 (len) + 4 (IHDR) + 4 (w) + 4 (h) = 24 bytes
        if (data == null || data.Length < 24) return false;
        // Verify PNG signature: \x89 P N G \r \n \x1a \n
        if (data[0] != 0x89 || data[1] != 0x50 || data[2] != 0x4E || data[3] != 0x47 ||
            data[4] != 0x0D || data[5] != 0x0A || data[6] != 0x1A || data[7] != 0x0A)
            return false;
        width  = (data[16] << 24) | (data[17] << 16) | (data[18] << 8) | data[19];
        height = (data[20] << 24) | (data[21] << 16) | (data[22] << 8) | data[23];
        return width > 0 && height > 0;
    }

    /// <summary>
    /// Converts a T2D spritesheet into individual Patchwork-compatible sprite PNGs.
    /// Uses sprite.rect from original sprites to extract each frame from the
    /// replacement atlas texture, saving to Converted/T2D/{cleanName}/.
    /// </summary>
    private static void ConvertSpritesheet(Texture2D atlas, string cleanName)
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
    //  Original-texture capture and restore
    // ================================================================

    /// <summary>
    /// Captures a (potentially unreadable) Texture2D to PNG bytes via a RenderTexture blit.
    /// Returns null on failure.
    /// </summary>
    private static byte[] CaptureTextureAsPng(Texture2D tex)
    {
        if (tex == null) return null;
        try
        {
            RenderTexture rt = RenderTexture.GetTemporary(
                tex.width, tex.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            Graphics.Blit(tex, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var readable = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false);
            readable.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);
            readable.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            byte[] png = readable.EncodeToPNG();
            Object.Destroy(readable);
            Plugin.Logger.LogDebug($"[T2D] Captured {png.Length / 1024} KB original for '{tex.name}'");
            return png;
        }
        catch (System.Exception ex)
        {
            Plugin.Logger.LogWarning($"[T2D] Failed to capture original for '{tex.name}': {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Restores the vanilla pixel data for a texture that was previously replaced in-place.
    /// Returns true if a restore was performed.
    /// </summary>
    internal static bool TryRestoreTexture(Texture2D tex)
    {
        if (tex == null) return false;
        if (!_originalTextureData.TryGetValue(tex.name, out var png))
        {
            return false;
        }

        if (tex.LoadImage(png))
        {
            Plugin.Logger.LogInfo($"[T2D] Restored vanilla pixels for '{tex.name}'");
            // Do NOT remove the entry: during scene transitions Unity can have two instances
            // of the same atlas name in memory simultaneously. Removing after the first
            // restore leaves the second instance unrestorable. The stored PNG is the true
            // vanilla data and never changes, so keeping it is safe. PruneStaleOriginals
            // handles eviction once the texture is no longer in memory.
            return true;
        }

        Plugin.Logger.LogWarning($"[T2D] LoadImage failed while restoring '{tex.name}'");
        return false;
    }

    /// <summary>
    /// Removes <see cref="_originalTextureData"/> entries for textures that no longer exist in
    /// memory (destroyed when their scene was unloaded). Call on <c>SceneManager.sceneUnloaded</c>.
    /// </summary>
    internal static void PruneStaleOriginals()
    {
        if (_originalTextureData.Count == 0) return;

        var liveNames = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        foreach (var tex in Resources.FindObjectsOfTypeAll<Texture2D>())
            if (tex != null) liveNames.Add(tex.name);

        int before = _originalTextureData.Count;
        foreach (var key in new List<string>(_originalTextureData.Keys))
        {
            if (liveNames.Contains(key)) continue; // texture still alive, keep
            // Even if the texture is gone, keep its original-data entry when an active
            // spritesheet override covers it. If we pruned it and the atlas is later reloaded
            // (e.g. Inventory scene reopened) with replacement pixels still in-memory, TrySwapTexture
            // would recapture replacement pixels as "original" → permanent cascade failure.
            // When the pack is disabled SpritesheetOverrides is empty, so stale entries are
            // pruned normally and no memory leak occurs.
            string cleanKey = T2DUtil.CleanTextureName(key);
            if (SpritesheetOverrides.ContainsKey(key) || SpritesheetOverrides.ContainsKey(cleanKey))
                continue;
            _originalTextureData.Remove(key);
        }

        int pruned = before - _originalTextureData.Count;
        if (pruned > 0)
            Plugin.Logger.LogInfo($"[T2D] Pruned {pruned} stale original(s) after scene unload ({_originalTextureData.Count} remaining)");
    }
}
