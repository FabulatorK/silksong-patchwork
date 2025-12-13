using System.IO;
using System.Linq;
using UnityEngine;
using Patchwork.Util;

namespace Patchwork.Handlers;

/// <summary>
/// Handles dumping of sprites from sprite collections to individual PNG files.
/// </summary>
public static class SpriteDumper
{
    public static string DumpPath { get { return Path.Combine(Plugin.BasePath, "Dumps"); } }
    public static string ConvertPath { get { return Path.Combine(Plugin.BasePath, "Converted"); } }

    public static void DumpCollection(tk2dSpriteCollectionData collection, bool convert = false)
    {
        string baseDir = convert ? ConvertPath : DumpPath;
        foreach (var mat in collection.materials)
        {
            if (mat == null || mat.mainTexture == null)
                continue;

            Texture matTex = mat.mainTexture;
            if (matTex.width == 0 || matTex.height == 0)
                continue;
            if (!matTex.isReadable || matTex is not RenderTexture)
                matTex = TexUtil.GetReadable(matTex);

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = matTex as RenderTexture;
            GL.PushMatrix();
            GL.LoadPixelMatrix(0, matTex.width, matTex.height, 0);

            string matname = mat.name.Split(' ')[0];
            tk2dSpriteDefinition[] spriteDefinitions = [.. collection.spriteDefinitions.Where(def => def.material == mat)];
            foreach (var def in spriteDefinitions)
            {
                if (string.IsNullOrEmpty(def.name)) continue;
                if (File.Exists(Path.Combine(baseDir, collection.name, matname, def.name + ".png")))
                    continue;

                Rect spriteRect = SpriteUtil.GetSpriteRect(def, matTex);
                if (spriteRect.width == 0 || spriteRect.height == 0)
                    continue;

                Texture2D spriteTex = new((int)spriteRect.width, (int)spriteRect.height, TextureFormat.RGBA32, false);
                spriteTex.ReadPixels(spriteRect, 0, 0);
                spriteTex.Apply();

                if (def.flipped == tk2dSpriteDefinition.FlipMode.None)
                {
                    var png = spriteTex.EncodeToPNG();
                    IOUtil.EnsureDirectoryExists(Path.Combine(baseDir, collection.name, matname));
                    File.WriteAllBytes(Path.Combine(baseDir, collection.name, matname, def.name + ".png"), png);
                }
                else
                {
                    RenderTexture rotated = RenderTexture.GetTemporary((int)spriteRect.height, (int)spriteRect.width, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                    var prev = RenderTexture.active;
                    RenderTexture.active = rotated;
                    GL.PushMatrix();
                    GL.LoadPixelMatrix(0, rotated.height, rotated.width, 0);

                    Vector2 uBasis, vBasis;
                    switch (def.flipped)
                    {
                        case tk2dSpriteDefinition.FlipMode.Tk2d:
                            uBasis = Vector2.up; vBasis = Vector2.left;
                            break;
                        case tk2dSpriteDefinition.FlipMode.TPackerCW:
                            uBasis = Vector2.down; vBasis = Vector2.right;
                            break;
                        default:
                            uBasis = Vector2.right; vBasis = Vector2.up;
                            break;
                    }
                    TexUtil.RotateMaterial.SetVector("_Basis", new Vector4(uBasis.x, uBasis.y, vBasis.x, vBasis.y));
                    Graphics.DrawTextureImpl(new Rect(0, 0, spriteTex.width, spriteTex.height), spriteTex, new Rect(0, 0, 1, 1), 0, 0, 0, 0, Color.white, TexUtil.RotateMaterial, 0);

                    Texture2D finalTex = new((int)spriteRect.height, (int)spriteRect.width, TextureFormat.RGBA32, false);
                    finalTex.ReadPixels(new Rect(0, 0, rotated.width, rotated.height), 0, 0);
                    finalTex.Apply();

                    RenderTexture.active = prev;
                    GL.PopMatrix();
                    RenderTexture.ReleaseTemporary(rotated);

                    var png = finalTex.EncodeToPNG();
                    IOUtil.EnsureDirectoryExists(Path.Combine(baseDir, collection.name, matname));
                    File.WriteAllBytes(Path.Combine(baseDir, collection.name, matname, def.name + ".png"), png);
                }
            }

            GL.PopMatrix();
            RenderTexture.active = previous;
        }
    }
    
    public static void DumpSingleSprite(tk2dSpriteDefinition def, tk2dSpriteCollectionData collection)
    {
        foreach (var mat in collection.materials)
        {
            if (def.material != mat)
                continue;

            if (mat == null || mat.mainTexture == null)
                continue;

            Texture matTex = mat.mainTexture;
            if (matTex.width == 0 || matTex.height == 0)
                continue;
            if (!matTex.isReadable || matTex is not RenderTexture)
                matTex = TexUtil.GetReadable(matTex);

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = matTex as RenderTexture;
            GL.PushMatrix();
            GL.LoadPixelMatrix(0, matTex.width, matTex.height, 0);

            string matname = mat.name.Split(' ')[0];

            if (string.IsNullOrEmpty(def.name)) return;
            if (File.Exists(Path.Combine(DumpPath, collection.name, matname, def.name + ".png")))
                return;

            Rect spriteRect = SpriteUtil.GetSpriteRect(def, matTex);
            if (spriteRect.width == 0 || spriteRect.height == 0)
                return;

            Texture2D spriteTex = new((int)spriteRect.width, (int)spriteRect.height, TextureFormat.RGBA32, false);
            spriteTex.ReadPixels(spriteRect, 0, 0);
            spriteTex.Apply();

            if (def.flipped == tk2dSpriteDefinition.FlipMode.None)
            {
                var pngNoFlip = spriteTex.EncodeToPNG();
                IOUtil.EnsureDirectoryExists(Path.Combine(DumpPath, collection.name, matname));
                File.WriteAllBytes(Path.Combine(DumpPath, collection.name, matname, def.name + ".png"), pngNoFlip);

                GL.PopMatrix();
                RenderTexture.active = previous;
                return;
            }

            RenderTexture rotated = RenderTexture.GetTemporary((int)spriteRect.height, (int)spriteRect.width, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var prev = RenderTexture.active;
            RenderTexture.active = rotated;
            GL.PushMatrix();
            GL.LoadPixelMatrix(0, rotated.height, rotated.width, 0);
            Vector2 uBasis, vBasis;
            switch (def.flipped)
            {
                case tk2dSpriteDefinition.FlipMode.Tk2d:
                    uBasis = Vector2.up; vBasis = Vector2.left;
                    break;
                case tk2dSpriteDefinition.FlipMode.TPackerCW:
                    uBasis = Vector2.down; vBasis = Vector2.right;
                    break;
                default:
                    uBasis = Vector2.right; vBasis = Vector2.up;
                    break;
            }
            TexUtil.RotateMaterial.SetVector("_Basis", new Vector4(uBasis.x, uBasis.y, vBasis.x, vBasis.y));
            Graphics.DrawTextureImpl(new Rect(0, 0, spriteTex.width, spriteTex.height), spriteTex, new Rect(0, 0, 1, 1), 0, 0, 0, 0, Color.white, TexUtil.RotateMaterial, 0);
            Texture2D finalTex = new((int)spriteRect.height, (int)spriteRect.width, TextureFormat.RGBA32, false);
            finalTex.ReadPixels(new Rect(0, 0, rotated.width, rotated.height), 0, 0);
            finalTex.Apply();
            RenderTexture.active = prev;
            GL.PopMatrix();
            RenderTexture.ReleaseTemporary(rotated);

            var png = finalTex.EncodeToPNG();
            IOUtil.EnsureDirectoryExists(Path.Combine(DumpPath, collection.name, matname));
            File.WriteAllBytes(Path.Combine(DumpPath, collection.name, matname, def.name + ".png"), png);

            GL.PopMatrix();
            RenderTexture.active = previous;
        }
    }
}