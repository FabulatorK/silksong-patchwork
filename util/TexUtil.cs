using System.IO;
using BepInEx;
using UnityEngine;

namespace Patchwork.Util;

public static class TexUtil
{
    public static Material RotateMaterial = null;

    public static void Initialize()
    {
        string bundlePath = Path.Combine(Plugin.BasePath, "patchwork.assetbundle");
        AssetBundle bundle = AssetBundle.LoadFromFile(bundlePath);
        var RotateShader = bundle.LoadAsset<Shader>("Assets/Patchwork/Rotate.shader");
        RotateMaterial = new Material(RotateShader);
    }

    public static RenderTexture GetReadable(Texture tex)
    {
        RenderTexture rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        Graphics.Blit(tex, rt);
        return rt;
    }

    public static Texture2D LoadFromPNG(string path)
    {
        byte[] pngData = FileCache.ReadBytes(path);
        if (pngData == null)
            return null;

        Texture2D tex = new(2, 2);
        if (!tex.LoadImage(pngData))
        {
            Plugin.Logger.LogWarning($"LoadFromPNG: Failed to decode image data from '{path}'");
            return null;
        }
        // Prevent Unity's UnloadUnusedAssets (auto-triggered on scene transitions) from
        // destroying runtime-created textures while our dictionaries still reference them.
        tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
        return tex;
    }

    /// <summary>Reads raw PNG bytes, served from FileCache when the file is unchanged.</summary>
    public static byte[] ReadBytesFromPNG(string path) => FileCache.ReadBytes(path);

    /// <summary>Creates a Texture2D from raw PNG bytes (GPU upload). Returns null on failure.</summary>
    public static Texture2D CreateTextureFromBytes(byte[] pngData)
    {
        Texture2D tex = new(2, 2);
        if (!tex.LoadImage(pngData))
        {
            Plugin.Logger.LogWarning("CreateTextureFromBytes: Failed to load image data");
            return null;
        }
        tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
        return tex;
    }
}