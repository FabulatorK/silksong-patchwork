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

    // Vanilla texture references, captured before the first replacement.
    // Keyed by collection name → material name.  Never cleared across reloads.
    //
    // We store the raw Texture (not a RT copy) and set DontUnloadUnusedAsset on it so
    // Resources.UnloadUnusedAssets() cannot evict it.  Once marked, the texture stays
    // alive for the entire session regardless of scene transitions.
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

            // Capture vanilla texture on first encounter.
            // Mark it DontUnloadUnusedAsset so Resources.UnloadUnusedAssets() cannot
            // evict it even after we orphan it by setting mat.mainTexture = our RT.
            if (!_originalTextures.TryGetValue(collection.name, out var origMap))
                _originalTextures[collection.name] = origMap = new();
            if (mat.mainTexture is not RenderTexture && mat.mainTexture != null
                && !origMap.ContainsKey(matname))
            {
                mat.mainTexture.hideFlags |= HideFlags.DontUnloadUnusedAsset;
                origMap[matname] = mat.mainTexture;
            }

            // If vanilla is not known yet (Reload() ran before this collection's Init()),
            // skip — InitPostfix will process this material when Init() provides a fresh texture.
            if (!origMap.TryGetValue(matname, out var vanillaTex) || vanillaTex == null)
                continue;

            if (!LoadedAtlases.ContainsKey(collection.name))
                LoadedAtlases[collection.name] = new HashSet<string>();

            if (LoadedAtlases[collection.name].Add(matname))
            {
                // First time in this reload cycle: (re-)blit the atlas for this material.
                //
                // We REUSE the existing RenderTexture when possible (same dimensions).
                // Blitting new content into the existing RT is safe because mat.mainTexture
                // already points at that RT — nothing ever sees a null/destroyed texture.
                // A new RT is only created on the very first encounter or on a dimension change.
                Texture2D customTex = FindSpritesheetTex(collection, matnameAbbr);
                Texture blitSrc = (Texture)customTex ?? vanillaTex;
                if (customTex != null) hasCustomSpritesheets = true;

                if (!LoadedAtlasesTextures.TryGetValue(collection.name, out var atlasMap))
                    LoadedAtlasesTextures[collection.name] = atlasMap = new();

                if (!atlasMap.TryGetValue(matname, out var atlasRT) || atlasRT == null
                    || atlasRT.width != blitSrc.width || atlasRT.height != blitSrc.height)
                {
                    // Destroy the old RT only when we're about to replace it — at this point
                    // we are still about to set mat.mainTexture immediately below, so there
                    // is no frame where the material holds a destroyed texture.
                    if (atlasRT != null) { atlasRT.Release(); UnityEngine.Object.Destroy(atlasRT); }
                    atlasRT = new RenderTexture(blitSrc.width, blitSrc.height, 0,
                        RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
                    {
                        hideFlags = HideFlags.DontUnloadUnusedAsset,
                        name      = matname
                    };
                    atlasMap[matname] = atlasRT;
                }

                // Blit the full atlas source into the RT (replaces all previous content).
                Graphics.Blit(blitSrc, atlasRT);
                if (customTex != null) UnityEngine.Object.Destroy(customTex);

                mat.mainTexture = atlasRT;
            }
            else
            {
                // Already processed in this reload cycle (duplicate collection instance).
                // Just ensure the material points at our RT.
                if (LoadedAtlasesTextures.TryGetValue(collection.name, out var atlasMap)
                    && atlasMap.TryGetValue(matname, out var atlasRT))
                    mat.mainTexture = atlasRT;
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
                // Texture served its purpose (blitted onto the atlas RT). Destroy it now
                // so the GPU memory is reclaimed at end-of-frame. Without this, every
                // Reload() call (which clears LoadedSprites and redraws every sprite)
                // leaked one DontUnloadUnusedAsset Texture2D per sprite per reload —
                // after enough pack toggles these accumulated until the graphics system
                // ran out of memory, causing hard failures requiring a process restart.
                UnityEngine.Object.Destroy(spriteTex);
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

    /// <summary>
    /// Returns a freshly loaded Texture2D for the custom spritesheet, or null if no
    /// custom sheet is found for this collection/material.  Caller must Destroy the
    /// returned texture when it is no longer needed.
    /// </summary>
    private static Texture2D FindSpritesheetTex(tk2dSpriteCollectionData collection,
        string materialName)
    {
        if (!_fileIndexBuilt) RebuildFileIndex();
        string key = $"{collection.name}/{materialName}.png";
        return _sheetFileIndex.TryGetValue(key, out var entry)
            ? TexUtil.LoadFromPNG(entry.FullPath)
            : null;
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
        LoadedSprites.Clear();
        // LoadedAtlasesTextures is intentionally NOT cleared here.
        // Each atlas RT is reused in-place: we blit new content into the existing RT
        // rather than destroying and recreating it.  This means mat.mainTexture never
        // points to a destroyed RT, regardless of which collections FindObjectsOfTypeAll
        // happens to return at reload time (e.g. scene-scoped collections not yet loaded).

        foreach (var collection in Resources.FindObjectsOfTypeAll<tk2dSpriteCollectionData>())
            LoadCollection(collection);

        Plugin.Logger.LogInfo($"[SpriteLoader] Reload complete: {LoadedSpriteCount} sprite(s) blitted across {LoadedAtlases.Count} atlas(es)");
    }
}
