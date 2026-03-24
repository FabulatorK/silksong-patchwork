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

    // Read-only stats for GUI
    public static int LoadedCollectionCount => LoadedAtlases.Count;
    public static int LoadedSpriteCount
    {
        get
        {
            int count = 0;
            foreach (var col in LoadedSprites.Values)
                foreach (var mat in col.Values)
                    count += mat.Count;
            return count;
        }
    }

    public static void ApplyPatches(Harmony harmony)
    {
        harmony.Patch(
            original: AccessTools.Method(typeof(tk2dSpriteCollectionData), nameof(tk2dSpriteCollectionData.Init)),
            postfix: new HarmonyMethod(typeof(SpriteLoader), nameof(InitPostfix))
        );
    }

    private static void InitPostfix(tk2dSpriteCollectionData __instance)
    {
        LoadCollection(__instance);
    }

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

        if (hasCustomSpritesheets && Plugin.Config.ConvertSpritesheets)
            SpriteDumper.DumpCollection(collection, true);
    }

    private static Texture2D FindSprite(string collectionName, string materialName, string spriteName)
    {
        string suffix = Path.Combine(collectionName, materialName);
        string match = FindFileWithSuffix(LoadPath, $"{spriteName}.png", suffix);
        if (match != null)
            return TexUtil.LoadFromPNG(match);

        foreach (var packPath in Plugin.PluginPackPaths)
        {
            string packSpritesDir = Path.Combine(packPath, "Sprites");
            if (!Directory.Exists(packSpritesDir))
                continue;
            match = FindFileWithSuffix(packSpritesDir, $"{spriteName}.png", suffix);
            if (match != null)
                return TexUtil.LoadFromPNG(match);
        }

        return null;
    }

    private static string FindFileWithSuffix(string searchDir, string searchPattern, string dirSuffix)
    {
        var files = Directory.GetFiles(searchDir, searchPattern, SearchOption.AllDirectories);
        foreach (var f in files)
        {
            if (Path.GetDirectoryName(f).EndsWith(dirSuffix))
                return f;
        }
        return null;
    }

    private static SpritesheetResult FindSpritesheet(tk2dSpriteCollectionData collection, string materialName)
    {
        string match = FindFileWithSuffix(AtlasLoadPath, $"{materialName}.png", collection.name);
        if (match != null)
        {
            var tex2d = TexUtil.LoadFromPNG(match);
            RenderTexture rt = RenderTexture.GetTemporary(tex2d.width, tex2d.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            Graphics.Blit(tex2d, rt);
            Object.Destroy(tex2d);
            return new SpritesheetResult { Texture = rt, FromCustom = true };
        }

        foreach (var packPath in Plugin.PluginPackPaths)
        {
            string packSheetsDir = Path.Combine(packPath, "Spritesheets");
            if (!Directory.Exists(packSheetsDir))
                continue;
            match = FindFileWithSuffix(packSheetsDir, $"{materialName}.png", collection.name);
            if (match != null)
            {
                var tex2d = TexUtil.LoadFromPNG(match);
                RenderTexture rt = RenderTexture.GetTemporary(tex2d.width, tex2d.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                Graphics.Blit(tex2d, rt);
                Object.Destroy(tex2d);
                return new SpritesheetResult { Texture = rt, FromCustom = true };
            }
        }

        var mat = collection.materials.FirstOrDefault(m => m.name.StartsWith(materialName + " ") || m.name == materialName);
        var tex = TexUtil.GetReadable(mat?.mainTexture);
        return new SpritesheetResult { Texture = tex, FromCustom = false };
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
        Plugin.Logger.LogInfo($"[tk2d-Reload] Starting sprite reload for scene {SceneManager.GetActiveScene().name}. " +
            $"Pre-reload: {LoadedSpriteCount} sprites in {LoadedCollectionCount} collections, " +
            $"{LoadedAtlases.Sum(kv => kv.Value.Count)} atlas entries");
        LoadedAtlases.Clear();
        LoadedAtlasesTextures.Clear();
        LoadedSprites.Clear();
        var spriteCollections = Resources.FindObjectsOfTypeAll<tk2dSpriteCollectionData>();
        Plugin.Logger.LogInfo($"[tk2d-Reload] Found {spriteCollections.Length} sprite collections to process");
        foreach (var collection in spriteCollections)
            LoadCollection(collection);
        Plugin.Logger.LogInfo($"[tk2d-Reload] Finished reload. " +
            $"Post-reload: {LoadedSpriteCount} sprites in {LoadedCollectionCount} collections, " +
            $"{LoadedAtlases.Sum(kv => kv.Value.Count)} atlas entries");
    }

    internal class SpritesheetResult
    {
        public RenderTexture Texture;
        public bool FromCustom;
    }
}