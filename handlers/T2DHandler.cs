using System.IO;
using HarmonyLib;
using UnityEngine;
using System.Linq;
using Patchwork.Util;
using System.Collections.Generic;
using UnityEngine.UI;

namespace Patchwork.Handlers;

[HarmonyPatch]
public static partial class T2DHandler
{
    public static string T2DDumpPath { get { return Path.Combine(SpriteDumper.DumpPath, "T2D"); } }
    public static string T2DAtlasLoadPath { get { return Path.Combine(SpriteLoader.AtlasLoadPath, "T2D"); } }

    // --- In-place spritesheet replacement (inspired by Customizer T2D) ---
    // Keyed by raw filename (without extension). At runtime, TrySwapTexture looks up
    // by raw texture name first, then falls back to clean name. This naturally handles
    // multiple atlases sharing the same clean name (e.g. sactx-1-2048x2048-BC7-Hornet-*
    // and sactx-0-4096x4096-BC7-Hornet-* are simply two different keys).
    private static readonly Dictionary<string, (byte[] PngData, int Width, int Height)>
        SpritesheetOverrides = new(System.StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<int> ReplacedTextureIds = new();
    private static readonly HashSet<int> SkippedTextureIds = new();

    // --- Individual sprite replacement (Patchwork's value-add) ---
    // Skin authors can replace individual frames without repacking an atlas.
    // These use Sprite.Create with the replacement PNG as a standalone texture.
    private static readonly Dictionary<string, Sprite> LoadedT2DSprites = new();
    private static readonly Dictionary<string, Texture2D> PreloadedT2DTextures = new();
    private static readonly Dictionary<string, HashSet<string>> SpriteAtlasMap = new();

    // Sprite names that are confirmed to belong to T2D atlas textures.
    // Used to scope enforcement/loading — prevents replacing UI sprites
    // that happen to share a name with a T2D replacement file.
    private static readonly HashSet<string> ConfirmedT2DSpriteNames = new();

    // Sprite names confirmed to have no replacement on disk.
    // Prevents repeated filesystem scans for the same missing sprite.
    private static readonly HashSet<string> NegativeSpriteCache = new();

    private static readonly Dictionary<int, string> TrackedSpriteNames = new();
    private static readonly HashSet<SpriteRenderer> KnownT2DSpriteRenderers = new();
    private static readonly HashSet<Image> KnownT2DImages = new();
    private static bool _enforcing = false;
    private static bool _handling = false;

    // Cached delegates to avoid per-frame closure allocations in RemoveWhere
    private static readonly System.Predicate<SpriteRenderer> _srNullPredicate = sr => sr == null;
    private static readonly System.Predicate<Image> _imgNullPredicate = img => img == null;

    // Cache for CleanTextureName results — avoids repeated string splits in per-frame enforcement
    private static readonly Dictionary<string, string> CleanTextureNameCache = new();

    /// <summary>
    /// True when any T2D replacement data exists (spritesheets or individual sprites).
    /// Gates all expensive per-frame sweeps so Patchwork is near-zero-cost when no T2D
    /// assets are loaded.
    /// </summary>
    public static bool HasT2DReplacements =>
        SpritesheetOverrides.Count > 0 || PreloadedT2DTextures.Count > 0 || LoadedT2DSprites.Count > 0;

    // Read-only stats for GUI
    public static int SpritesheetOverrideCount => SpritesheetOverrides.Count;
    public static int LoadedT2DSpriteCount => LoadedT2DSprites.Count;
    public static int PreloadedT2DTextureCount => PreloadedT2DTextures.Count;
    public static int TrackedRendererCount => KnownT2DSpriteRenderers.Count + KnownT2DImages.Count;
    public static IEnumerable<string> LoadedT2DSpriteNames => LoadedT2DSprites.Keys;
    public static IEnumerable<string> SpritesheetOverrideNames => SpritesheetOverrides.Keys;
    public static IEnumerable<string> PreloadedT2DTextureNames => PreloadedT2DTextures.Keys;


    // ================================================================
    //  Harmony patches — sprite/material setters
    // ================================================================

    // [PW-PERF] Per-frame call counters for Harmony patches
    private static int _perfSpriteSetterCalls;
    private static int _perfImageSetterCalls;
    private static int _perfMatSetterCalls;
    private static int _perfLastFrame = -1;

    private static void PerfTickFrame()
    {
        // [PW-PERF] Dump previous frame's patch call counts
        int frame = Time.frameCount;
        if (frame != _perfLastFrame)
        {
            if (_perfSpriteSetterCalls > 20 || _perfImageSetterCalls > 20 || _perfMatSetterCalls > 20)
                Plugin.Logger.LogWarning($"[PW-PERF] T2D patch calls in frame {_perfLastFrame}: SpriteSet={_perfSpriteSetterCalls}, ImageSet={_perfImageSetterCalls}, MatSet={_perfMatSetterCalls}");
            _perfSpriteSetterCalls = 0;
            _perfImageSetterCalls = 0;
            _perfMatSetterCalls = 0;
            _perfLastFrame = frame;
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(SpriteRenderer), nameof(SpriteRenderer.sprite), MethodType.Setter)]
    public static void SetSpritePostfix(SpriteRenderer __instance, Sprite value)
    {
        PerfTickFrame(); // [PW-PERF]
        _perfSpriteSetterCalls++; // [PW-PERF]
        if (_handling || _enforcing || __instance == null || value == null || __instance.gameObject.name == "TempSpriteRenderer")
            return;

        if (Plugin.Config.DumpSprites && !string.IsNullOrEmpty(value.name) && value.texture != null && !string.IsNullOrEmpty(value.texture.name))
            HandleDump(value);

        if (!HasT2DReplacements)
            return;

        TrackedSpriteNames[__instance.GetInstanceID()] = value.name;

        if (value.texture != null)
            TrySwapTexture(value.texture);

        HandleLoad(__instance, value);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Image), nameof(Image.sprite), MethodType.Setter)]
    public static void SetImageSpritePostfix(Image __instance, Sprite value)
    {
        PerfTickFrame(); // [PW-PERF]
        _perfImageSetterCalls++; // [PW-PERF]
        if (_handling || _enforcing || __instance == null || value == null)
            return;

        if (Plugin.Config.DumpSprites && !string.IsNullOrEmpty(value.name) && value.texture != null && !string.IsNullOrEmpty(value.texture.name))
            HandleDump(value);

        if (!HasT2DReplacements)
            return;

        TrackedSpriteNames[__instance.GetInstanceID()] = value.name;

        if (value.texture != null)
            TrySwapTexture(value.texture);

        HandleLoad(__instance, value);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Material), nameof(Material.mainTexture), MethodType.Setter)]
    public static void SetMaterialTexturePostfix(Texture value)
    {
        PerfTickFrame(); // [PW-PERF]
        _perfMatSetterCalls++; // [PW-PERF]
        if (!HasT2DReplacements)
            return;

        if (value is Texture2D tex)
        {
            TrySwapTexture(tex);

            if (Plugin.Config.DumpSprites && IsT2DTexture(tex.name))
                DumpT2DAtlasTexture(tex);
        }
    }
}
