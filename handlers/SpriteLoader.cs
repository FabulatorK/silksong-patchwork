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

    // Runtime state dictionaries — keyed by instance key (see InstanceKey()) so that two
    // tk2dSpriteCollectionData objects with the same .name (e.g. "Slab Prisoner Cln Data"
    // appearing in both Assets/Collections/ and Assets/Collections/Hornet NPCs/) never
    // collide.  File-index lookups still use collection.name — pack authors address folders
    // by name, and applying a replacement to every same-named collection is correct.
    private static readonly Dictionary<string, HashSet<string>> LoadedAtlases = new();
    private static readonly Dictionary<string, Dictionary<string, RenderTexture>> LoadedAtlasesTextures = new();
    private static readonly Dictionary<string, Dictionary<string, HashSet<string>>> LoadedSprites = new();

    // Vanilla texture backups, captured before the first replacement.
    // Keyed by mat.GetInstanceID() — materials are persistent shared Unity assets that survive
    // scene transitions, so the same ID is seen across every collection instance that references
    // this material.  Never cleared across reloads.
    //
    // We blit the vanilla texture into a runtime-owned RenderTexture on first encounter.
    // Runtime RTs are NOT part of any AssetBundle, so AssetBundle.Unload(true) cannot
    // destroy them — unlike a raw Texture reference which becomes a "fake null" after
    // Unload(true), causing Graphics.Blit to silently write nothing (transparent atlas).
    // DontUnloadUnusedAsset prevents Resources.UnloadUnusedAssets() from evicting them.
    private static readonly Dictionary<int, RenderTexture> _originalTextures = new();

    // Maps instance key → collection.name for MarkReload* lookups (which receive a name
    // string from the file watcher and need to find all live instances with that name).
    private static readonly Dictionary<string, string> _instanceKeyToName = new();

    // Maps mat.GetInstanceID() → the vanilla texture's .name, captured before the first
    // replacement.  Used to resolve instance-specific spritesheet/sprite paths when two
    // collections share the same .name and material abbreviation.
    // Never cleared — mirrors _originalTextures lifetime (vanilla names don't change).
    private static readonly Dictionary<int, string> _vanillaTexNames = new();

    // UV and position backups for sprite defs that have been expanded.
    // Keyed by (matId, spriteName) — both are stable: matId is a persistent shared asset ID,
    // spriteName is fixed at tk2d export time.
    // Captured before the first expansion of each def; never cleared.
    // Restored before each reload cycle so that defs whose replacements are removed or
    // resized back to vanilla correctly revert, and the next pass can re-expand from scratch.
    private static readonly Dictionary<(int matId, string name), Vector2[]> _originalUVs       = new();
    private static readonly Dictionary<(int matId, string name), Vector3[]> _originalPositions = new();

    // Expansion plan for one material, computed fresh at the start of each LoadCollection
    // pass for that material.  Describes the enlarged atlas layout and which sprite defs
    // get UV + position remapping this cycle.
    private sealed class AtlasExpansionPlan
    {
        // Expanded atlas pixel dimensions.
        public int Width;
        public int Height;
        // All rects are in TEXTURE pixel space: (0,0) = bottom-left, Y increases upward.
        // This matches Unity's RenderTexture/UV convention and is what SpriteUtil.GetSpriteRect returns.
        public readonly Dictionary<string, Rect>             AllocatedRects = new(); // key = spriteName
        public readonly Dictionary<string, Rect>             VanillaRects   = new();
        public readonly Dictionary<string, string>           Anchors        = new();
        public readonly Dictionary<string, (int w, int h)>   RepDimensions  = new();
    }

    /// <summary>
    /// Stable runtime key for a collection instance.
    /// Combines the human-readable name with the Unity instance ID so two collections
    /// sharing the same name are never confused.
    /// </summary>
    private static string InstanceKey(tk2dSpriteCollectionData coll)
        => coll.name + "\x00" + coll.GetInstanceID();

    // Pre-built file indices: relative key (normalized, case-insensitive) → (absolute path, source pack).
    // Sprites:     key = "CollectionName/MaterialName/SpriteName.png"
    // Spritesheets: key = "CollectionName/MaterialName.png"
    // Built once per Reload() and lazily on first lookup before any Reload() fires.
    private static readonly Dictionary<string, (string FullPath, string Pack)> _spriteFileIndex =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, (string FullPath, string Pack)> _sheetFileIndex =
        new(StringComparer.OrdinalIgnoreCase);
    // Collection names (case-insensitive) that have at least one file in either index.
    // Used to skip the atlasRT creation/swap for collections with no replacements at all,
    // so Patchwork doesn't alter mat.mainTexture when there's nothing to apply.
    private static readonly HashSet<string> _collectionsWithFiles =
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
        // Ensure the file index (and _collectionsWithFiles) is current before the gate below.
        // LoadCollection fires from the tk2d Init postfix, which can run before any Reload()
        // call that would otherwise trigger RebuildFileIndex().  Without this, _collectionsWithFiles
        // is empty on first load and the gate skips every collection → no skins appear.
        if (!_fileIndexBuilt) RebuildFileIndex();

        string ikey = InstanceKey(collection);
        bool nameCollision = _instanceKeyToName.ContainsValue(collection.name);
        _instanceKeyToName[ikey] = collection.name;

        // Warn on first detection of a same-name collision and tell pack authors what
        // texture name to use for instance-specific targeting.
        if (nameCollision)
            Plugin.Logger.LogWarning(
                $"[SpriteLoader] Duplicate collection name '{collection.name}' " +
                $"(instanceId {collection.GetInstanceID()}). " +
                $"Use the vanilla texture name as the folder/file name to target this " +
                $"instance specifically: Sprites/{collection.name}/{{texName}}/... or " +
                $"Spritesheets/{collection.name}/{{texName}}.png  " +
                $"(texture names logged per material below when vanilla is first captured).");

        bool hasCustomSpritesheets = false;
        foreach (var mat in collection.materials)
        {
            if (mat == null)
                continue;

            string matname = mat.name;
            string matnameAbbr = mat.name.Split(' ')[0];
            int matId = mat.GetInstanceID();

            // Capture vanilla texture on first encounter (blit into a persistent backup RT).
            // Keyed by matId: materials are persistent shared assets — the same matId is seen
            // on every collection instance that references this material, including new instances
            // created after scene transitions.  When mat.mainTexture is already one of our RTs
            // (the material was processed in a prior instance's cycle) the capture is skipped and
            // the existing backup is found immediately by matId on the lookup below.
            if (mat.mainTexture is not RenderTexture && mat.mainTexture != null
                && !_originalTextures.ContainsKey(matId))
            {
                // Blit vanilla into a new runtime-owned RT.  Runtime RTs survive
                // AssetBundle.Unload(true); a raw Texture reference does not.
                var backupRT = new RenderTexture(mat.mainTexture.width, mat.mainTexture.height, 0,
                    RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
                {
                    hideFlags = HideFlags.DontUnloadUnusedAsset,
                    name      = matname + "_vanilla"
                };
                Graphics.Blit(mat.mainTexture, backupRT);
                _originalTextures[matId] = backupRT;
                // Record vanilla texture name for instance-specific load path resolution.
                string vTexName = mat.mainTexture.name;
                _vanillaTexNames[matId] = vTexName;
                if (nameCollision)
                    Plugin.Logger.LogWarning(
                        $"[SpriteLoader]   mat '{matnameAbbr}' → vanilla texture '{vTexName}'");
            }

            if (!_originalTextures.TryGetValue(matId, out var vanillaTex) || vanillaTex == null)
                continue; // vanilla not captured yet — skip until next Init()

            // Compute matIndex here — needed by both the vanilla-restore gate and the
            // expansion pipeline below. Array.IndexOf on collection.materials is safe
            // because mat comes directly from iterating that same array.
            int matIndex = System.Array.IndexOf(collection.materials, mat);

            // If no pack has any files for this collection, restore vanilla and skip the
            // custom blit.  The shared material may already hold a custom RT from a prior
            // instance's cycle, so unconditional restore is required — we cannot assume the
            // material is still showing vanilla just because the current ikey has no atlas entry.
            if (!_collectionsWithFiles.Contains(collection.name))
            {
                RestoreVanillaDefsForMat(collection, matId, matIndex);
                mat.mainTexture = vanillaTex;
                continue;
            }

            // Use materialId (authoritative index set at tk2d export time) rather than
            // def.material reference equality.  Unity silently creates material instances
            // when any renderer accesses .material instead of .sharedMaterial; this makes
            // def.material diverge from collection.materials[i], causing entire sprite
            // batches to be missed and those atlas regions to show wrong content.

            if (!LoadedAtlases.ContainsKey(ikey))
                LoadedAtlases[ikey] = new HashSet<string>();

            if (LoadedAtlases[ikey].Add(matname))
            {
                // First time in this reload cycle: build the atlas for this material.
                //
                // Step 1 — restore any UV/position data remapped in a previous cycle.
                // This ensures defs whose enlarged replacements were removed or resized
                // back to vanilla revert correctly before we re-evaluate the plan.
                RestoreVanillaDefsForMat(collection, matId, matIndex);

                // Step 2 — compute the expansion plan (null if no enlarged replacements).
                AtlasExpansionPlan plan = matIndex >= 0
                    ? PlanExpansion(collection, matIndex, matId, matnameAbbr, vanillaTex)
                    : null;

                Texture2D customTex = FindSpritesheetTex(collection, matId, matnameAbbr);
                Texture blitSrc = (Texture)customTex ?? vanillaTex;
                if (customTex != null) hasCustomSpritesheets = true;

                // Step 3 — create or resize the atlas RT.
                // Target dimensions come from the expansion plan when one exists;
                // otherwise match the blit source (vanilla or custom spritesheet).
                int targetW = plan?.Width  ?? blitSrc.width;
                int targetH = plan?.Height ?? blitSrc.height;

                if (!LoadedAtlasesTextures.TryGetValue(ikey, out var atlasMap))
                    LoadedAtlasesTextures[ikey] = atlasMap = new();

                if (!atlasMap.TryGetValue(matname, out var atlasRT) || atlasRT == null
                    || atlasRT.width != targetW || atlasRT.height != targetH)
                {
                    // Destroy the old RT only when we're about to replace it — at this point
                    // we are still about to set mat.mainTexture immediately below, so there
                    // is no frame where the material holds a destroyed texture.
                    if (atlasRT != null) { atlasRT.Release(); UnityEngine.Object.Destroy(atlasRT); }
                    atlasRT = new RenderTexture(targetW, targetH, 0,
                        RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
                    {
                        hideFlags = HideFlags.DontUnloadUnusedAsset,
                        name      = matname
                    };
                    atlasMap[matname] = atlasRT;
                }

                // Step 4 — blit source content into the atlas RT.
                // For an expanded atlas the extra area must be cleared first, then the
                // vanilla/custom-sheet content is pixel-copied into the bottom region only.
                // Graphics.CopyTexture is a direct GPU-side pixel copy (no interpolation,
                // no Y-flip) and correctly handles both RenderTexture and Texture2D sources.
                if (plan != null)
                {
                    var prevActive = RenderTexture.active;
                    RenderTexture.active = atlasRT;
                    GL.Clear(false, true, Color.clear);
                    RenderTexture.active = prevActive;
                    // Place vanilla/custom source at (0,0) = bottom-left in texture space.
                    // Enlarged sprite slots occupy the rows above vanillaH — left clear here,
                    // filled by individual sprite draws in the GL block below.
                    Graphics.CopyTexture(blitSrc, 0, 0, 0, 0,
                        blitSrc.width, blitSrc.height, atlasRT, 0, 0, 0, 0);
                }
                else
                {
                    Graphics.Blit(blitSrc, atlasRT);
                }
                if (customTex != null) UnityEngine.Object.Destroy(customTex);

                // Step 5 — remap UV coordinates and quad vertices for enlarged sprites.
                // Must happen before the GL block so GetSpriteRect returns the allocated rect.
                if (plan != null)
                    ApplyExpansionPlan(collection, plan, matId, matIndex,
                        atlasRT.width, atlasRT.height);

                mat.mainTexture = atlasRT;
            }
            else
            {
                // Already processed in this reload cycle (same instance seen twice — e.g.
                // FindObjectsOfTypeAll returning it more than once on some Unity versions).
                // Just ensure the material points at our RT.
                if (LoadedAtlasesTextures.TryGetValue(ikey, out var atlasMap)
                    && atlasMap.TryGetValue(matname, out var atlasRT))
                    mat.mainTexture = atlasRT;
            }

            var previous = RenderTexture.active;
            RenderTexture.active = mat.mainTexture as RenderTexture;
            GL.PushMatrix();
            GL.LoadPixelMatrix(0, mat.mainTexture.width, mat.mainTexture.height, 0);

            tk2dSpriteDefinition[] spriteDefinitions = matIndex < 0
                ? System.Array.Empty<tk2dSpriteDefinition>()
                : [.. collection.spriteDefinitions.Where(def => def.materialId == matIndex)];
            foreach (var def in spriteDefinitions)
            {
                if (string.IsNullOrEmpty(def.name)) continue;
                if (!LoadedSprites.ContainsKey(ikey))
                    LoadedSprites[ikey] = new Dictionary<string, HashSet<string>>();
                if (!LoadedSprites[ikey].ContainsKey(matname))
                    LoadedSprites[ikey][matname] = new HashSet<string>();
                if (!LoadedSprites[ikey][matname].Add(def.name)) continue;

                Texture2D spriteTex = FindSprite(collection.name, matId, matnameAbbr, def.name);
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

        // Build collection-name presence set — first path segment of every key is the collection name.
        _collectionsWithFiles.Clear();
        foreach (var key in _spriteFileIndex.Keys)
        {
            int slash = key.IndexOf('/');
            if (slash > 0) _collectionsWithFiles.Add(key.Substring(0, slash));
        }
        foreach (var key in _sheetFileIndex.Keys)
        {
            int slash = key.IndexOf('/');
            if (slash > 0) _collectionsWithFiles.Add(key.Substring(0, slash));
        }
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

    /// <summary>
    /// Returns the full filesystem path to the replacement PNG for a sprite, or
    /// <c>null</c> if no replacement exists.  Instance-specific path is tried first.
    /// </summary>
    private static string FindSpritePath(string collectionName, int matId,
        string matnameAbbr, string spriteName)
    {
        if (!_fileIndexBuilt) RebuildFileIndex();

        if (_vanillaTexNames.TryGetValue(matId, out var texName) && !string.IsNullOrEmpty(texName))
        {
            string specific = $"{collectionName}/{texName}/{spriteName}.png";
            if (_spriteFileIndex.TryGetValue(specific, out var s)) return s.FullPath;
        }
        string general = $"{collectionName}/{matnameAbbr}/{spriteName}.png";
        return _spriteFileIndex.TryGetValue(general, out var g) ? g.FullPath : null;
    }

    /// <summary>
    /// Loads and returns a replacement Texture2D for a sprite, or <c>null</c> if none found.
    /// Caller must <c>Destroy</c> the returned texture when it is no longer needed.
    /// </summary>
    private static Texture2D FindSprite(string collectionName, int matId,
        string matnameAbbr, string spriteName)
    {
        string path = FindSpritePath(collectionName, matId, matnameAbbr, spriteName);
        return path != null ? TexUtil.LoadFromPNG(path) : null;
    }

    /// <summary>
    /// Returns a freshly loaded Texture2D for the custom spritesheet, or null if no
    /// custom sheet is found for this collection/material.  Caller must Destroy the
    /// returned texture when it is no longer needed.
    /// Tries an instance-specific path keyed by the vanilla texture name first, then
    /// falls back to the material-abbreviation path for backward compatibility.
    /// </summary>
    private static Texture2D FindSpritesheetTex(tk2dSpriteCollectionData collection,
        int matId, string matnameAbbr)
    {
        if (!_fileIndexBuilt) RebuildFileIndex();

        // Instance-specific: Spritesheets/{collName}/{vanillaTexName}.png
        if (_vanillaTexNames.TryGetValue(matId, out var texName)
            && !string.IsNullOrEmpty(texName))
        {
            string specific = $"{collection.name}/{texName}.png";
            if (_sheetFileIndex.TryGetValue(specific, out var s))
                return TexUtil.LoadFromPNG(s.FullPath);
        }

        // General: Spritesheets/{collName}/{matAbbr}.png
        string general = $"{collection.name}/{matnameAbbr}.png";
        return _sheetFileIndex.TryGetValue(general, out var g)
            ? TexUtil.LoadFromPNG(g.FullPath)
            : null;
    }

    // ── Canvas expansion ─────────────────────────────────────────────────────

    /// <summary>
    /// Scans replacement PNGs for this material pass and builds a layout plan for any
    /// that are larger than their vanilla sprite rect.  Returns null when no sprite needs
    /// expansion.  All rects are in TEXTURE pixel space (Y=0 at bottom).
    /// </summary>
    private static AtlasExpansionPlan PlanExpansion(
        tk2dSpriteCollectionData collection, int matIndex,
        int matId, string matnameAbbr, RenderTexture vanillaTex)
    {
        int vanillaW = vanillaTex.width;
        int vanillaH = vanillaTex.height;
        int atlasW   = vanillaW;
        int curX = 0, curRowH = 0, extraH = 0;
        AtlasExpansionPlan plan = null;

        foreach (var def in collection.spriteDefinitions)
        {
            if (def.materialId != matIndex || string.IsNullOrEmpty(def.name)) continue;

            string path = FindSpritePath(collection.name, matId, matnameAbbr, def.name);
            if (path == null) continue;

            var (repW, repH) = IOUtil.ReadPngDimensions(path);
            if (repW <= 0 || repH <= 0) continue;

            Rect vRect = SpriteUtil.GetSpriteRect(def, vanillaTex);
            if (repW <= (int)vRect.width && repH <= (int)vRect.height) continue;

            plan ??= new AtlasExpansionPlan();

            string anchor = IOUtil.ReadAnchorEntry(
                LoadPath, collection.name, matnameAbbr, def.name) ?? "bottom-center";

            // Shelf-pack into the extra rows above the vanilla atlas (texture Y-up, so above = higher Y).
            // If a replacement is wider than the current atlas, expand the atlas width.
            if (repW > atlasW) atlasW = repW;
            if (curX + repW > atlasW) { extraH += curRowH; curX = 0; curRowH = 0; }

            // Texture-space rect: Y starts at vanillaH and increases upward.
            var allocRect = new Rect(curX, vanillaH + extraH, repW, repH);
            curX    += repW;
            curRowH  = Math.Max(curRowH, repH);

            plan.AllocatedRects[def.name] = allocRect;
            plan.VanillaRects[def.name]   = vRect;
            plan.Anchors[def.name]        = anchor;
            plan.RepDimensions[def.name]  = (repW, repH);

            Plugin.Logger.LogInfo(
                $"[SpriteLoader] Expand: {collection.name}/{matnameAbbr}/{def.name}  " +
                $"{(int)vRect.width}×{(int)vRect.height} → {repW}×{repH}  " +
                $"anchor={anchor}  slot=({(int)allocRect.x},{(int)allocRect.y})");
        }

        if (plan != null)
        {
            extraH      += curRowH;
            plan.Width   = atlasW;
            plan.Height  = vanillaH + extraH;
        }
        return plan;
    }

    /// <summary>
    /// Remaps <c>def.uvs</c> and <c>def.positions</c> for every sprite that has an
    /// allocated slot in <paramref name="plan"/>.  Backups are captured once (copy-on-first-write)
    /// so the originals can be restored on revert.
    /// </summary>
    private static void ApplyExpansionPlan(
        tk2dSpriteCollectionData collection, AtlasExpansionPlan plan,
        int matId, int matIndex, int newW, int newH)
    {
        foreach (var def in collection.spriteDefinitions)
        {
            if (def.materialId != matIndex || string.IsNullOrEmpty(def.name)) continue;
            if (!plan.AllocatedRects.TryGetValue(def.name, out var allocRect)) continue;
            if (!plan.VanillaRects.TryGetValue(def.name,   out var vRect))     continue;
            if (!plan.RepDimensions.TryGetValue(def.name,  out var repDim))    continue;

            var key = (matId, def.name);

            // ── Backup before first modification ─────────────────────────────
            if (def.uvs != null && !_originalUVs.ContainsKey(key))
                _originalUVs[key] = (Vector2[])def.uvs.Clone();

            if (def.positions != null && !_originalPositions.ContainsKey(key))
            {
                // Copy-on-write: if another def shares this positions array, clone it first
                // so our modification doesn't silently affect unrelated sprites.
                foreach (var other in collection.spriteDefinitions)
                    if (!ReferenceEquals(other, def) && ReferenceEquals(other.positions, def.positions))
                        { def.positions = (Vector3[])def.positions.Clone(); break; }
                _originalPositions[key] = (Vector3[])def.positions.Clone();
            }

            // ── UV remap ─────────────────────────────────────────────────────
            // Map each vanilla UV vertex linearly from the old [minU..maxU]×[minV..maxV]
            // range into the new range corresponding to the allocated slot.
            if (def.uvs != null && _originalUVs.TryGetValue(key, out var origUVs))
            {
                float oldMinU = float.MaxValue, oldMaxU = float.MinValue;
                float oldMinV = float.MaxValue, oldMaxV = float.MinValue;
                foreach (var v in origUVs)
                {
                    if (v.x < oldMinU) oldMinU = v.x; if (v.x > oldMaxU) oldMaxU = v.x;
                    if (v.y < oldMinV) oldMinV = v.y; if (v.y > oldMaxV) oldMaxV = v.y;
                }

                // Convert allocated rect (texture pixel space, Y-up) to UV.
                float newMinU = allocRect.x                       / newW;
                float newMaxU = (allocRect.x + allocRect.width)   / newW;
                float newMinV = allocRect.y                       / newH;
                float newMaxV = (allocRect.y + allocRect.height)  / newH;

                float rangeU = oldMaxU - oldMinU;
                float rangeV = oldMaxV - oldMinV;
                for (int i = 0; i < def.uvs.Length && i < origUVs.Length; i++)
                {
                    float tU = rangeU > 1e-6f ? (origUVs[i].x - oldMinU) / rangeU : 0.5f;
                    float tV = rangeV > 1e-6f ? (origUVs[i].y - oldMinV) / rangeV : 0.5f;
                    def.uvs[i] = new Vector2(
                        newMinU + tU * (newMaxU - newMinU),
                        newMinV + tV * (newMaxV - newMinV));
                }
            }

            // ── Position remap ────────────────────────────────────────────────
            // Compute new quad vertex positions in local unit space by expanding
            // the vanilla bounding box according to the chosen anchor.
            if (def.positions != null && _originalPositions.TryGetValue(key, out var origPos)
                && def.positions.Length == origPos.Length && origPos.Length >= 4)
            {
                float oldMinX = float.MaxValue, oldMaxX = float.MinValue;
                float oldMinY = float.MaxValue, oldMaxY = float.MinValue;
                foreach (var p in origPos)
                {
                    if (p.x < oldMinX) oldMinX = p.x; if (p.x > oldMaxX) oldMaxX = p.x;
                    if (p.y < oldMinY) oldMinY = p.y; if (p.y > oldMaxY) oldMaxY = p.y;
                }

                float unitSpanX = oldMaxX - oldMinX;
                float unitSpanY = oldMaxY - oldMinY;
                if (unitSpanX < 1e-6f || unitSpanY < 1e-6f) continue; // degenerate — skip

                // Derive units-per-pixel from vanilla position span and vanilla pixel rect.
                float uppX = unitSpanX / vRect.width;
                float uppY = unitSpanY / vRect.height;

                int dW = repDim.w - (int)vRect.width;
                int dH = repDim.h - (int)vRect.height;

                float newMinX, newMaxX, newMinY, newMaxY;
                string anchor = plan.Anchors.GetValueOrDefault(def.name, "bottom-center");
                switch (anchor)
                {
                    case "top-center":
                        newMinX = oldMinX - dW * 0.5f * uppX;
                        newMaxX = oldMaxX + dW * 0.5f * uppX;
                        newMaxY = oldMaxY;                        // top fixed
                        newMinY = oldMinY - dH * uppY;
                        break;
                    case "left-center":
                        newMinX = oldMinX;                        // left fixed
                        newMaxX = oldMaxX + dW * uppX;
                        newMinY = oldMinY - dH * 0.5f * uppY;
                        newMaxY = oldMaxY + dH * 0.5f * uppY;
                        break;
                    case "right-center":
                        newMaxX = oldMaxX;                        // right fixed
                        newMinX = oldMinX - dW * uppX;
                        newMinY = oldMinY - dH * 0.5f * uppY;
                        newMaxY = oldMaxY + dH * 0.5f * uppY;
                        break;
                    case "center":
                        newMinX = oldMinX - dW * 0.5f * uppX;
                        newMaxX = oldMaxX + dW * 0.5f * uppX;
                        newMinY = oldMinY - dH * 0.5f * uppY;
                        newMaxY = oldMaxY + dH * 0.5f * uppY;
                        break;
                    default: // "bottom-center"
                        newMinX = oldMinX - dW * 0.5f * uppX;
                        newMaxX = oldMaxX + dW * 0.5f * uppX;
                        newMinY = oldMinY;                        // bottom fixed
                        newMaxY = oldMaxY + dH * uppY;
                        break;
                }

                float rangeX = unitSpanX;
                float rangeY = unitSpanY;
                for (int i = 0; i < def.positions.Length && i < origPos.Length; i++)
                {
                    float tX = (origPos[i].x - oldMinX) / rangeX;
                    float tY = (origPos[i].y - oldMinY) / rangeY;
                    def.positions[i] = new Vector3(
                        newMinX + tX * (newMaxX - newMinX),
                        newMinY + tY * (newMaxY - newMinY),
                        origPos[i].z);
                }
            }
        }
    }

    /// <summary>
    /// Restores backed-up UV and position arrays for all sprite defs on this material.
    /// Called before each reload cycle's expansion pass so that previously-enlarged
    /// sprites that no longer have oversized replacements correctly revert to vanilla.
    /// Also called when no files exist for the collection (full vanilla restore path).
    /// </summary>
    private static void RestoreVanillaDefsForMat(
        tk2dSpriteCollectionData collection, int matId, int matIndex)
    {
        if (matIndex < 0) return;
        foreach (var def in collection.spriteDefinitions)
        {
            if (def.materialId != matIndex || string.IsNullOrEmpty(def.name)) continue;
            var key = (matId, def.name);
            if (_originalUVs.TryGetValue(key, out var origUVs)
                && def.uvs != null && def.uvs.Length == origUVs.Length)
                Array.Copy(origUVs, def.uvs, origUVs.Length);
            if (_originalPositions.TryGetValue(key, out var origPos)
                && def.positions != null && def.positions.Length == origPos.Length)
                Array.Copy(origPos, def.positions, origPos.Length);
        }
    }

    public static void MarkReloadSprite(string collectionName, string atlasName, string spriteName)
    {
        lock (LoadedSprites)
        {
            foreach (var ikey in InstanceKeysForName(collectionName))
            {
                if (!LoadedSprites.TryGetValue(ikey, out var matMap)) continue;
                foreach (string key in matMap.Keys.ToList())
                    if (key.StartsWith(atlasName))
                        matMap[key].Remove(spriteName);
            }
        }
    }

    public static void MarkReloadAtlas(string collectionName, string atlasName)
    {
        lock (LoadedAtlases)
        {
            foreach (var ikey in InstanceKeysForName(collectionName))
            {
                if (LoadedAtlases.TryGetValue(ikey, out var atlasSet))
                    foreach (string key in atlasSet.Where(a => a.StartsWith(atlasName)).ToList())
                        atlasSet.Remove(key);
                if (LoadedSprites.TryGetValue(ikey, out var matMap))
                    foreach (string key in matMap.Keys.Where(a => a.StartsWith(atlasName)).ToList())
                        matMap[key].Clear();
            }
        }
    }

    /// <summary>Returns all live instance keys whose collection name matches <paramref name="collectionName"/>.</summary>
    private static IEnumerable<string> InstanceKeysForName(string collectionName)
    {
        foreach (var kvp in _instanceKeyToName)
            if (string.Equals(kvp.Value, collectionName, StringComparison.Ordinal))
                yield return kvp.Key;
    }

    public static void Reload()
    {
        RebuildFileIndex();
        LoadedAtlases.Clear();
        LoadedSprites.Clear();
        _instanceKeyToName.Clear();
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
