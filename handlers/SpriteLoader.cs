using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Patchwork.Util;
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

    // Vanilla texture backups, captured as persistent RenderTextures on first encounter.
    // Keyed by collection name → material name.  Never cleared across reloads.
    //
    // WHY RenderTexture instead of the raw Texture reference:
    // Once we set mat.mainTexture = ourRT the vanilla texture has no scene-level
    // reference remaining. Resources.UnloadUnusedAssets() (called implicitly on scene
    // transitions) does NOT count static C# dict entries as "in use" for Unity's native
    // asset tracking — the texture gets evicted, leaving a "fake null" Unity object.
    // A RenderTexture we own (HideFlags.DontUnloadUnusedAsset) is immune to eviction
    // and persists for the lifetime of the session.
    private static readonly Dictionary<string, Dictionary<string, RenderTexture>> _originalTextures = new();

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

            // Capture vanilla texture while we have it.
            // We blit it into a persistent RT immediately — BEFORE orphaning it by
            // setting mat.mainTexture = ourRT.  Only capture once (first-wins): vanilla
            // textures are fixed, and re-capturing from a possibly-stale reference on
            // later Init() calls would overwrite a good backup with a bad one.
            if (!_originalTextures.TryGetValue(collection.name, out var origMap))
                _originalTextures[collection.name] = origMap = new();
            if (mat.mainTexture is not RenderTexture && mat.mainTexture != null
                && !origMap.ContainsKey(matname))
            {
                var backup = new RenderTexture(
                    mat.mainTexture.width, mat.mainTexture.height, 0,
                    RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
                {
                    hideFlags = HideFlags.DontUnloadUnusedAsset,
                    name      = matname + "_vanilla"
                };
                Graphics.Blit(mat.mainTexture, backup);
                origMap[matname] = backup;
            }

            // If vanilla is not known yet (Reload() ran before this collection's Init()),
            // skip — InitPostfix will process this material when Init() provides a fresh texture.
            if (!origMap.ContainsKey(matname))
                continue;

            if (!LoadedAtlases.ContainsKey(collection.name))
                LoadedAtlases[collection.name] = new HashSet<string>();
            if (LoadedAtlases[collection.name].Add(matname))
            {
                var sheetResult = FindSpritesheet(collection, matnameAbbr, origMap[matname]);
                if (sheetResult.FromCustom)
                    hasCustomSpritesheets = true;
                mat.mainTexture = sheetResult.Texture;
                if (!LoadedAtlasesTextures.ContainsKey(collection.name))
                    LoadedAtlasesTextures[collection.name] = new Dictionary<string, RenderTexture>();
                LoadedAtlasesTextures[collection.name][matname] = mat.mainTexture as RenderTexture;
            }
            else
            {
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

        Texture blitSrc;
        bool fromCustom;
        Texture2D customTex = null;

        if (_sheetFileIndex.TryGetValue(key, out var entry))
        {
            customTex = TexUtil.LoadFromPNG(entry.FullPath);
            blitSrc   = customTex;
            fromCustom = true;
        }
        else
        {
            blitSrc    = originalTex;
            fromCustom = false;
        }

        // Persistent RenderTexture — NOT GetTemporary.
        // GetTemporary RTs are only valid within a single frame; Unity reclaims them
        // from the pool across scene transitions, leaving mat.mainTexture pointing at
        // a stale RT.  A plain `new RenderTexture` persists until explicitly destroyed.
        var rt = new RenderTexture(blitSrc.width, blitSrc.height, 0,
            RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
        {
            hideFlags = HideFlags.DontUnloadUnusedAsset
        };
        Graphics.Blit(blitSrc, rt);
        if (customTex != null) UnityEngine.Object.Destroy(customTex);
        return new SpritesheetResult { Texture = rt, FromCustom = fromCustom };
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
        LoadedAtlases.Clear();

        // Save old RTs for deferred cleanup.  We destroy them AFTER LoadCollection
        // has already written new textures to every material, so there is never a
        // frame where mat.mainTexture points to a released/destroyed RT.
        var oldRTs = new List<RenderTexture>();
        foreach (var colMap in LoadedAtlasesTextures.Values)
            foreach (var rt in colMap.Values)
                if (rt != null) oldRTs.Add(rt);
        LoadedAtlasesTextures.Clear();
        LoadedSprites.Clear();

        foreach (var collection in Resources.FindObjectsOfTypeAll<tk2dSpriteCollectionData>())
            LoadCollection(collection);

        // Now safe to destroy — materials already point at fresh RTs.
        foreach (var rt in oldRTs)
        {
            rt.Release();
            UnityEngine.Object.Destroy(rt);
        }

        Plugin.Logger.LogInfo($"[SpriteLoader] Reload complete: {LoadedSpriteCount} sprite(s) blitted across {LoadedAtlases.Count} atlas(es)");
    }

    internal class SpritesheetResult
    {
        public RenderTexture Texture;
        public bool FromCustom;
    }
}