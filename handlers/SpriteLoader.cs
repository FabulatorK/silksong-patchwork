using System;
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

    // Original game textures, stored before the first replacement so reloads can restore them.
    // Keyed by collection name → material name.  Never cleared across reloads.
    private static readonly Dictionary<string, Dictionary<string, Texture>> _originalTextures = new();

    // Pre-built file indices: relative key (normalized, case-insensitive) → (absolute path, source pack).
    // Sprites:     key = "CollectionName/MaterialName/SpriteName.png"
    // Spritesheets: key = "CollectionName/MaterialName.png"
    // Built once per Reload() and lazily on first lookup before any Reload() fires.
    private static readonly Dictionary<string, (string FullPath, string Pack)> _spriteFileIndex =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, (string FullPath, string Pack)> _sheetFileIndex =
        new(StringComparer.OrdinalIgnoreCase);
    private static bool _fileIndexBuilt;

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

                // Persist the game's original texture so it can be used as the base on
                // future reloads — even after a previous load has overwritten mat.mainTexture
                // with one of our RenderTextures.
                if (!_originalTextures.TryGetValue(collection.name, out var origMap))
                    _originalTextures[collection.name] = origMap = new();
                if (!(unreadableTex is RenderTexture))
                    origMap[matname] = unreadableTex;          // fresh game texture — update
                else if (!origMap.ContainsKey(matname))
                {
                    // Best-effort: mat.mainTexture is already an RT (overwritten by a previous load)
                    // and we have no stored original for this mat yet.  Storing an RT as "original"
                    // will permanently break vanilla-restore — log so we can diagnose.
                    Plugin.Logger.LogWarning(
                        $"[tk2d-Restore] BEST-EFFORT TRIGGERED for '{collection.name}'/'{matname}': " +
                        $"mat.mainTexture is already a RenderTexture ({unreadableTex.width}x{unreadableTex.height}) " +
                        $"on first visit.  Vanilla restore for this material will be broken.");
                    origMap[matname] = unreadableTex;          // already overwritten; best-effort
                }

                var sheetResult = FindSpritesheet(collection, matnameAbbr, origMap[matname]);
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

    private static void RebuildFileIndex()
    {
        _spriteFileIndex.Clear();
        _sheetFileIndex.Clear();
        _fileIndexBuilt = true;

        void IndexDir(string root, string sourcePack,
            Dictionary<string, (string, string)> index, string conflictType)
        {
            if (!Directory.Exists(root)) return;
            foreach (var file in Directory.GetFiles(root, "*.png", SearchOption.AllDirectories))
            {
                // Compute path relative to the search root, normalised to forward slashes.
                string rel = file.Substring(root.Length)
                                 .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                                 .Replace('\\', '/');

                if (index.TryGetValue(rel, out var existing))
                    ConflictTracker.Record(conflictType, ConflictKey(conflictType, rel),
                        existing.Item2, sourcePack);
                else
                    index[rel] = (file, sourcePack);
            }
        }

        IndexDir(LoadPath,     null, _spriteFileIndex, "sprite");
        foreach (var p in Plugin.PluginPackPaths)
            IndexDir(Path.Combine(p, "Sprites"), p, _spriteFileIndex, "sprite");

        IndexDir(AtlasLoadPath, null, _sheetFileIndex, "sheet");
        foreach (var p in Plugin.PluginPackPaths)
            IndexDir(Path.Combine(p, "Spritesheets"), p, _sheetFileIndex, "sheet");
    }

    // Produces a conflict-tracker key matching the format used by the old per-sprite code.
    // Sprite rel:  "CollectionName/MaterialName/SpriteName.png" → "sprite:CollectionName/SpriteName"
    // Sheet  rel:  "CollectionName/MaterialName.png"            → "sheet:CollectionName/MaterialName"
    private static string ConflictKey(string type, string rel)
    {
        string noExt = rel.EndsWith(".png") ? rel.Substring(0, rel.Length - 4) : rel;
        var parts = noExt.Split('/');
        return type == "sprite" && parts.Length >= 3
            ? $"sprite:{parts[0]}/{parts[2]}"
            : $"{type}:{noExt}";
    }

    private static Texture2D FindSprite(string collectionName, string materialName, string spriteName)
    {
        if (!_fileIndexBuilt) RebuildFileIndex();
        string key = $"{collectionName}/{materialName}/{spriteName}.png";
        return _spriteFileIndex.TryGetValue(key, out var entry)
            ? TexUtil.LoadFromPNG(entry.FullPath)
            : null;
    }

    private static SpritesheetResult FindSpritesheet(tk2dSpriteCollectionData collection,
        string materialName, Texture originalTex)
    {
        if (!_fileIndexBuilt) RebuildFileIndex();
        string key = $"{collection.name}/{materialName}.png";

        if (_sheetFileIndex.TryGetValue(key, out var entry))
        {
            var tex2d = TexUtil.LoadFromPNG(entry.FullPath);
            RenderTexture rt = RenderTexture.GetTemporary(
                tex2d.width, tex2d.height, 0,
                RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            Graphics.Blit(tex2d, rt);
            UnityEngine.Object.Destroy(tex2d);
            return new SpritesheetResult { Texture = rt, FromCustom = true };
        }

        // No custom sheet — restore from the original game texture.
        if (originalTex is RenderTexture)
            Plugin.Logger.LogWarning(
                $"[tk2d-Restore] Vanilla restore for '{collection.name}'/'{key}': " +
                $"originalTex is a RenderTexture ({originalTex.width}x{originalTex.height}) — " +
                $"stored original was already an overwritten RT, restore may produce wrong pixels.");
        else
            Plugin.Logger.LogDebug(
                $"[tk2d-Restore] Vanilla restore for '{collection.name}'/'{key}' from native texture.");
        return new SpritesheetResult { Texture = TexUtil.GetReadable(originalTex), FromCustom = false };
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
        RebuildFileIndex();
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