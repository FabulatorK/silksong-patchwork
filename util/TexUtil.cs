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

    /// <summary>
    /// Creates a readable RenderTexture copy of the input texture.
    /// ⚠️ CALLER MUST call RenderTexture.ReleaseTemporary() on the returned texture!
    /// </summary>
    /// <param name="tex">Source texture to copy</param>
    /// <returns>Temporary RenderTexture that must be released by caller</returns>
    public static RenderTexture GetReadable(Texture tex)
    {
        RenderTexture rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        Graphics.Blit(tex, rt);
        return rt;
    }

    /// <summary>
    /// Loads a Texture2D from a PNG file.
    /// ⚠️ CALLER MUST call Object.Destroy() on the returned texture when done!
    /// </summary>
    /// <param name="path">Path to PNG file</param>
    /// <returns>New Texture2D that must be destroyed by caller, or null if failed</returns>
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
            Object.Destroy(tex); //Cleanup on failure
            return null;
        }
        return tex;
    }
}