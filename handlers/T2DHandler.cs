using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using Patchwork.Util;

namespace Patchwork.Handlers;

/// <summary>
/// Harmony patch coordinator for T2D sprite/texture setters.
/// All loading, enforcement, and dump logic is delegated to T2DLoader and T2DDumper.
/// </summary>
[HarmonyPatch]
public static class T2DHandler
{
    // [PW-PERF] Per-frame call counters for Harmony patches
    private static int _perfSpriteSetterCalls;
    private static int _perfImageSetterCalls;
    private static int _perfMatSetterCalls;
    private static int _perfLastFrame = -1;

    private static void PerfTickFrame()
    {
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
        PerfTickFrame();
        _perfSpriteSetterCalls++;
        if (T2DLoader.IsHandlingOrEnforcing || __instance == null || value == null || __instance.gameObject.name == "TempSpriteRenderer")
            return;

        if (Plugin.Config.DumpSprites && !string.IsNullOrEmpty(value.name) && value.texture != null && !string.IsNullOrEmpty(value.texture.name))
            T2DDumper.HandleDump(value);

        if (!T2DLoader.HasT2DReplacements)
            return;

        T2DLoader.OnSpriteSet(__instance, value);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Image), nameof(Image.sprite), MethodType.Setter)]
    public static void SetImageSpritePostfix(Image __instance, Sprite value)
    {
        PerfTickFrame();
        _perfImageSetterCalls++;
        if (T2DLoader.IsHandlingOrEnforcing || __instance == null || value == null)
            return;

        if (Plugin.Config.DumpSprites && !string.IsNullOrEmpty(value.name) && value.texture != null && !string.IsNullOrEmpty(value.texture.name))
            T2DDumper.HandleDump(value);

        if (!T2DLoader.HasT2DReplacements)
            return;

        T2DLoader.OnSpriteSet(__instance, value);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Material), nameof(Material.mainTexture), MethodType.Setter)]
    public static void SetMaterialTexturePostfix(Texture value)
    {
        PerfTickFrame();
        _perfMatSetterCalls++;
        if (!T2DLoader.HasT2DReplacements)
            return;

        if (value is Texture2D tex)
        {
            T2DLoader.TrySwapTexture(tex);

            if (Plugin.Config.DumpSprites && T2DUtil.IsT2DTexture(tex.name))
                T2DDumper.DumpAtlasTexture(tex);
        }
    }
}
