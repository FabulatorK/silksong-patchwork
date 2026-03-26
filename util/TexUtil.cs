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
        if (!File.Exists(path))
        {
            Plugin.Logger.LogWarning($"LoadFromPNG: File {path} does not exist");
            return null;
        }

        byte[] pngData = File.ReadAllBytes(path);
        Texture2D tex = new(2, 2);
        if (!tex.LoadImage(pngData))
        {
            Plugin.Logger.LogWarning($"LoadFromPNG: Failed to load image data from {path}");
            return null;
        }
        return tex;
    }

    /// <summary>Reads raw PNG bytes from disk without uploading to the GPU.</summary>
    public static byte[] ReadBytesFromPNG(string path)
    {
        if (!File.Exists(path))
        {
            Plugin.Logger.LogWarning($"ReadBytesFromPNG: File {path} does not exist");
            return null;
        }
        return File.ReadAllBytes(path);
    }

    /// <summary>Creates a Texture2D from raw PNG bytes (GPU upload). Returns null on failure.</summary>
    public static Texture2D CreateTextureFromBytes(byte[] pngData)
    {
        Texture2D tex = new(2, 2);
        if (!tex.LoadImage(pngData))
        {
            Plugin.Logger.LogWarning("CreateTextureFromBytes: Failed to load image data");
            return null;
        }
        return tex;
    }
}