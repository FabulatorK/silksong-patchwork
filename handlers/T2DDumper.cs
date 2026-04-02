using System.IO;
using System.Collections.Generic;
using UnityEngine;
using Patchwork.Util;

namespace Patchwork.Handlers;

/// <summary>
/// Handles dumping of T2D (TextureAtlas2D) sprites and atlases to individual PNG files.
/// Mirrors the structure of SpriteDumper.
/// </summary>
public static class T2DDumper
{
    public static string DumpPath => Path.Combine(SpriteDumper.DumpPath, "T2D");

    public static void DumpAllT2DSprites()
    {
        // Track all texture IDs seen via Sprite objects so DumpStandaloneTextures
        // can skip them without a redundant FindObjectsOfTypeAll<Sprite> sweep.
        HashSet<int> spriteTextureIds = new();
        HashSet<int> dumpedTextureIds = new();
        // Per-call deduplication: prevents writing the same sprite file multiple times
        // when several Sprite objects share the same atlas and sprite name.
        HashSet<string> dumpedSpriteKeys = new();

        foreach (var sprite in Resources.FindObjectsOfTypeAll<Sprite>())
        {
            if (sprite == null || sprite.texture == null)
                continue;
            spriteTextureIds.Add(sprite.texture.GetInstanceID());
            if (string.IsNullOrEmpty(sprite.name) || string.IsNullOrEmpty(sprite.texture.name))
                continue;

            if (T2DUtil.IsT2DTexture(sprite.texture.name))
            {
                string key = T2DUtil.SpriteKey(T2DUtil.CleanTextureName(sprite.texture.name), sprite.name);
                if (!dumpedSpriteKeys.Add(key)) continue;
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

        DumpStandaloneTextures(spriteTextureIds);
    }

    /// <summary>
    /// Dumps standalone Texture2D assets that have no corresponding Sprite objects
    /// and aren't T2D atlases. Receives the set of texture IDs already handled by
    /// the Sprite-based dump path so it can skip them without a second full sweep.
    /// </summary>
    private static void DumpStandaloneTextures(HashSet<int> spriteTextureIds)
    {
        string saveDirBase = Path.Combine(DumpPath, "_standalone");
        bool dirCreated = false;
        int count = 0;

        // Collect every texture that belongs to a tk2d atlas material so they are
        // excluded below even when the texture name doesn't match the IsT2DTexture()
        // compression-based pattern (avoids leaking atlas PNGs into _standalone/).
        var tk2dAtlasIds = new HashSet<int>();
        foreach (var coll in Resources.FindObjectsOfTypeAll<tk2dSpriteCollectionData>())
        {
            if (coll?.materials == null) continue;
            foreach (var mat in coll.materials)
                if (mat?.mainTexture != null)
                    tk2dAtlasIds.Add(mat.mainTexture.GetInstanceID());
        }

        foreach (var tex in Resources.FindObjectsOfTypeAll<Texture2D>())
        {
            if (tex == null || string.IsNullOrEmpty(tex.name))
                continue;
            if (T2DUtil.IsT2DTexture(tex.name))
                continue;
            if (tk2dAtlasIds.Contains(tex.GetInstanceID()))
                continue;
            if (spriteTextureIds.Contains(tex.GetInstanceID()))
                continue;
            if (tex.name.StartsWith("unity_") || tex.name.StartsWith("UI"))
                continue;

            string safeName = T2DUtil.SanitizeForFilesystem(tex.name);
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

                if (!dirCreated) { IOUtil.EnsureDirectoryExists(saveDirBase); dirCreated = true; }
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
    public static void DumpAtlasTexture(Texture2D tex)
    {
        string cleanName = T2DUtil.CleanTextureName(tex.name);
        string saveDir = Path.Combine(DumpPath, cleanName);
        // Include dimensions in the atlas filename to avoid collisions when
        // multiple runtime atlases share the same clean name (e.g. Hornet
        // at both 2048x2048 and 4096x4096).
        string atlasPath = Path.Combine(saveDir, $"_atlas_{tex.width}x{tex.height}.png");

        if (File.Exists(atlasPath))
            return;

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

    public static void HandleDump(Sprite sprite)
    {
        if (T2DUtil.IsT2DTexture(sprite.texture.name))
        {
            string cleanName = T2DUtil.CleanTextureName(sprite.texture.name);
            string saveDir = Path.Combine(DumpPath, cleanName);
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

                File.WriteAllBytes(savePath, spriteTex.EncodeToPNG());
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
            string safeName = T2DUtil.SanitizeForFilesystem(sprite.texture.name);
            string savePath = Path.Combine(DumpPath, safeName + ".png");
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

                File.WriteAllBytes(savePath, readableTex.EncodeToPNG());
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
}
