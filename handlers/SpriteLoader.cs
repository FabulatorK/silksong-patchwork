using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Patchwork.Util;
using UnityEngine.SceneManagement;
using HarmonyLib;

namespace Patchwork.Handlers;

[HarmonyPatch]
public static class SpriteLoader
{
    public static string LoadPath { get { return Path.Combine(Plugin.BasePath, "Sprites"); } }
    public static string AtlasLoadPath { get { return Path.Combine(Plugin.BasePath, "Spritesheets"); } }

    private static readonly Dictionary<string, HashSet<string>> LoadedAtlases = new();
    private static readonly Dictionary<string, Dictionary<string, RenderTexture>> LoadedAtlasesTextures = new();
    private static readonly Dictionary<string, Dictionary<string, HashSet<string>>> LoadedSprites = new();

    public static void ApplyPatches(Harmony harmony)
    {
        harmony.Patch(
            original: AccessTools.Method(typeof(tk2dSpriteCollectionData), nameof(tk2dSpriteCollectionData.Init)),
            postfix: new HarmonyMethod(typeof(SpriteLoader), nameof(InitPostfix))
        );
    }

    private static void InitPostfix(tk2dSpriteCollectionData __instance) => LoadCollection(__instance);

    public static void LoadCollection(tk2dSpriteCollectionData collection)
    {
        foreach (var mat in collection.materials)
        {
            if (mat == null)
                continue;
            
            string matname = mat.name;
            string matnameAbbr = mat.name.Split(' ')[0];
            if (!LoadedAtlases.ContainsKey(collection.name))
                LoadedAtlases[collection.name] = new HashSet<string>();
            if (LoadedAtlases[collection.name].Add(matname))
            {
                var unreadableTex = mat.mainTexture;
                mat.mainTexture = FindSpritesheet(collection, matnameAbbr);
                if (!LoadedAtlasesTextures.ContainsKey(collection.name))
                    LoadedAtlasesTextures[collection.name] = new Dictionary<string, RenderTexture>();
                LoadedAtlasesTextures[collection.name][matname] = mat.mainTexture as RenderTexture;
            } else {
                mat.mainTexture = LoadedAtlasesTextures[collection.name][matname];
            }

            var previous = RenderTexture.active;
            RenderTexture.active = mat.mainTexture as RenderTexture;
            GL.PushMatrix();
            GL.LoadPixelMatrix(0, mat.mainTexture.width, mat.mainTexture.height, 0);
            tk2dSpriteDefinition[] spriteDefinitions = [.. collection.spriteDefinitions.Where(def => def.material == mat)];
            foreach (var def in spriteDefinitions)
            {
                if (string.IsNullOrEmpty(def.name)) continue;
                if (!LoadedSprites.ContainsKey(collection.name))
                    LoadedSprites[collection.name] = new Dictionary<string, HashSet<string>>();
                if (!LoadedSprites[collection.name].ContainsKey(matname))
                    LoadedSprites[collection.name][matname] = new HashSet<string>();
                if (!LoadedSprites[collection.name][matname].Add(def.name)) continue;

                Texture2D spriteTex = FindSprite(collection.name, matnameAbbr, def.name);
                if (spriteTex == null) continue;

                Rect spriteRect = SpriteUtil.GetSpriteRect(def, mat.mainTexture);
                spriteRect.y = mat.mainTexture.height - spriteRect.y - spriteRect.height;
                Vector2 uBasis, vBasis;
                switch (def.flipped)
                {
                    case tk2dSpriteDefinition.FlipMode.Tk2d:
                        uBasis = Vector2.down; vBasis = Vector2.right;
                        break;

                    case tk2dSpriteDefinition.FlipMode.TPackerCW:
                        uBasis = Vector2.up; vBasis = Vector2.left;
                        break;

                    default:
                        uBasis = Vector2.right; vBasis = Vector2.up;
                        break;
                }

                TexUtil.RotateMaterial.SetVector("_Basis", new Vector4(uBasis.x, uBasis.y, vBasis.x, vBasis.y));
                Graphics.DrawTextureImpl(spriteRect, spriteTex, new Rect(0, 0, 1, 1), 0, 0, 0, 0, Color.white, TexUtil.RotateMaterial, 0);
            }

            mat.mainTexture.IncrementUpdateCount();
            GL.PopMatrix();
            RenderTexture.active = previous;
        }
    }

    private static Texture2D FindSprite(string collectionName, string materialName, string spriteName)
    {
        var files = Directory.GetFiles(LoadPath, $"{spriteName}.png", SearchOption.AllDirectories)
            .Where(f => Path.GetDirectoryName(f).EndsWith(Path.Combine(collectionName, materialName)));
        if (files.Any())
            return TexUtil.LoadFromPNG(files.First());

        foreach (var packPath in Plugin.PluginPackPaths)
        {
            if (!Directory.Exists(Path.Combine(packPath, "Sprites")))
                continue;
            var packFiles = Directory.GetFiles(Path.Combine(packPath, "Sprites"), $"{spriteName}.png", SearchOption.AllDirectories)
                .Where(f => Path.GetDirectoryName(f).EndsWith(Path.Combine(collectionName, materialName)));
            if (packFiles.Any())
                return TexUtil.LoadFromPNG(packFiles.First());
        }

        return null;
    }

    private static RenderTexture FindSpritesheet(tk2dSpriteCollectionData collection, string materialName)
    {
        var files = Directory.GetFiles(AtlasLoadPath, $"{materialName}.png", SearchOption.AllDirectories)
            .Where(f => Path.GetDirectoryName(f).EndsWith(collection.name));
        if (files.Any())
        {
            var tex2d = TexUtil.LoadFromPNG(files.First());
            RenderTexture rt = RenderTexture.GetTemporary(tex2d.width, tex2d.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            Graphics.Blit(tex2d, rt);
            Object.Destroy(tex2d);
            return rt;
        }

        foreach (var packPath in Plugin.PluginPackPaths)
        {
            if(!Directory.Exists(Path.Combine(packPath, "Spritesheets")))
                continue;
            var packFiles = Directory.GetFiles(Path.Combine(packPath, "Spritesheets"), $"{materialName}.png", SearchOption.AllDirectories)
                .Where(f => Path.GetDirectoryName(f).EndsWith(collection.name));
            if (packFiles.Any())
            {
                var tex2d = TexUtil.LoadFromPNG(packFiles.First());
                RenderTexture rt = RenderTexture.GetTemporary(tex2d.width, tex2d.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                Graphics.Blit(tex2d, rt);
                Object.Destroy(tex2d);
                return rt;
            }
        }

        var mat = collection.materials.FirstOrDefault(m => m.name.StartsWith(materialName + " ") || m.name == materialName);
        return TexUtil.GetReadable(mat?.mainTexture);
    }

    public static void MarkReloadSprite(string collectionName, string atlasName, string spriteName)
    {
        lock (LoadedSprites)
        {
            if (LoadedSprites.ContainsKey(collectionName) && LoadedSprites[collectionName].ContainsKey(atlasName))
                LoadedSprites[collectionName][atlasName].Remove(spriteName);
        }
    }


    public static void MarkReloadAtlas(string collectionName, string atlasName)
    {
        lock (LoadedAtlases)
        {
            if (LoadedAtlases.ContainsKey(collectionName))
                LoadedAtlases[collectionName].Remove(atlasName);

            if (LoadedSprites.ContainsKey(collectionName) && LoadedSprites[collectionName].ContainsKey(atlasName))
                LoadedSprites[collectionName][atlasName].Clear();
        }
    }
    
    public static void Reload()
    {
        Plugin.Logger.LogInfo($"Reloading sprites for scene {SceneManager.GetActiveScene().name}");
        var spriteCollections = Resources.FindObjectsOfTypeAll<tk2dSpriteCollectionData>();
        foreach (var collection in spriteCollections)
            LoadCollection(collection);
        Plugin.Logger.LogInfo($"Finished reloading sprites for scene {SceneManager.GetActiveScene().name}");
    }
}