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

    private static Dictionary<string, string> _spritePathCache = new();
    private static bool _cacheBuilt = false;

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
        bool hasCustomSpritesheets = false;
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
                var sheetResult = FindSpritesheet(collection, matnameAbbr);
                if (sheetResult.FromCustom)
                    hasCustomSpritesheets = true;
                mat.mainTexture = sheetResult.Texture;
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

                try
                {
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
                finally
                {
                    // ✅ Destroy texture after drawing - it's been copied to the RT
                    Object.Destroy(spriteTex);
                }
            }

            mat.mainTexture.IncrementUpdateCount();
            GL.PopMatrix();
            RenderTexture.active = previous;
        }

        if (hasCustomSpritesheets && Plugin.Config.ConvertSpritesheets)
            SpriteDumper.DumpCollection(collection, true);
    }

    private static Texture2D FindSprite(string collectionName, string materialName, string spriteName)
    {
        if (!_cacheBuilt) BuildPathCache();
        
        string key = $"{collectionName}/{materialName}/{spriteName}";
        if (_spritePathCache.TryGetValue(key, out string path))
            return TexUtil.LoadFromPNG(path);
        
        return null;
    }
    
    private static SpritesheetResult FindSpritesheet(tk2dSpriteCollectionData collection, string materialName)
    {
        var files = Directory.GetFiles(AtlasLoadPath, $"{materialName}.png", SearchOption.AllDirectories)
            .Where(f => Path.GetDirectoryName(f).EndsWith(collection.name));
        if (files.Any())
        {
            var tex2d = TexUtil.LoadFromPNG(files.First());
            try
            {
                RenderTexture rt = RenderTexture.GetTemporary(tex2d.width, tex2d.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                Graphics.Blit(tex2d, rt);
                return new SpritesheetResult { Texture = rt, FromCustom = true };
            }
            finally
            {
                Object.Destroy(tex2d); // ✅ Destroy after blit
            }
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
                try
                {
                    RenderTexture rt = RenderTexture.GetTemporary(tex2d.width, tex2d.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                    Graphics.Blit(tex2d, rt);
                    return new SpritesheetResult { Texture = rt, FromCustom = true };
                }
                finally
                {
                    Object.Destroy(tex2d); // ✅ Destroy after blit
                }
            }
        }

        var mat = collection.materials.FirstOrDefault(m => m.name.StartsWith(materialName + " ") || m.name == materialName);
        var tex = TexUtil.GetReadable(mat?.mainTexture);
        return new SpritesheetResult { Texture = tex, FromCustom = false };
    }

    /// <summary>
    /// Cleanup cached RenderTextures for a collection.
    /// Call this when unloading scenes or when memory pressure is high.
    /// </summary>
    public static void CleanupCollection(string collectionName)
    {
        if (LoadedAtlasesTextures.TryGetValue(collectionName, out var textures))
        {
            foreach (var rt in textures.Values)
            {
                if (rt != null)
                    RenderTexture.ReleaseTemporary(rt);
            }
            textures.Clear();
        }
        LoadedAtlases.Remove(collectionName);
        LoadedSprites.Remove(collectionName);
    }

    /// <summary>
    /// Cleanup all cached textures. Use sparingly - forces full reload.
    /// </summary>
    public static void CleanupAll()
    {
        foreach (var collectionName in LoadedAtlasesTextures.Keys.ToList())
            CleanupCollection(collectionName);
    }

    public static void MarkReloadSprite(string collectionName, string atlasName, string spriteName)
    {
        lock (LoadedSprites)
        {
            if (!LoadedSprites.ContainsKey(collectionName))
                return;
            foreach (string key in LoadedSprites[collectionName].Keys.ToList())
            {
                if (key.StartsWith(atlasName))
                    LoadedSprites[collectionName][key].Remove(spriteName);
            }
        }
    }


    public static void MarkReloadAtlas(string collectionName, string atlasName)
    {
        lock (LoadedAtlases)
        {
            // ✅ Release old RTs before removing from cache
            if (LoadedAtlasesTextures.TryGetValue(collectionName, out var textures))
            {
                foreach (string key in textures.Keys.Where(a => a.StartsWith(atlasName)).ToList())
                {
                    if (textures[key] != null)
                        RenderTexture.ReleaseTemporary(textures[key]);
                    textures.Remove(key);
                }
            }

            if (LoadedAtlases.ContainsKey(collectionName))
            {
                foreach (string key in LoadedAtlases[collectionName].Where(a => a.StartsWith(atlasName)).ToList())
                    LoadedAtlases[collectionName].Remove(key);
            }
            if (LoadedSprites.ContainsKey(collectionName))
            {
                foreach (string key in LoadedSprites[collectionName].Keys.Where(a => a.StartsWith(atlasName)).ToList())
                    LoadedSprites[collectionName][key].Clear();
            }
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

    internal class SpritesheetResult
    {
        public RenderTexture Texture;
        public bool FromCustom;
    }
    public static void BuildPathCache()
    {
        _spritePathCache.Clear();
        
        // Cache main Sprites folder
        if (Directory.Exists(LoadPath))
        {
            foreach (var file in Directory.GetFiles(LoadPath, "*.png", SearchOption.AllDirectories))
            {
                string relativePath = Path.GetRelativePath(LoadPath, file);
                string[] parts = relativePath.Split(Path.DirectorySeparatorChar);
                
                // Expecting: collection/material/sprite.png
                if (parts.Length >= 3)
                {
                    string key = $"{parts[^3]}/{parts[^2]}/{Path.GetFileNameWithoutExtension(parts[^1])}";
                    _spritePathCache[key] = file;
                }
            }
        }
        
        // Cache plugin pack folders
        foreach (var packPath in Plugin.PluginPackPaths)
        {
            string packSpritePath = Path.Combine(packPath, "Sprites");
            if (!Directory.Exists(packSpritePath)) continue;
            
            foreach (var file in Directory.GetFiles(packSpritePath, "*.png", SearchOption.AllDirectories))
            {
                string relativePath = Path.GetRelativePath(packSpritePath, file);
                string[] parts = relativePath.Split(Path.DirectorySeparatorChar);
                
                if (parts.Length >= 3)
                {
                    string key = $"{parts[^3]}/{parts[^2]}/{Path.GetFileNameWithoutExtension(parts[^1])}";
                    _spritePathCache[key] = file;  // Overwrites - pack takes priority
                }
            }
        }
        
        _cacheBuilt = true;
        Plugin.Logger.LogInfo($"Sprite path cache built: {_spritePathCache.Count} entries");
    }
    public static void InvalidatePathCache()
    {
        _cacheBuilt = false;
    }
}