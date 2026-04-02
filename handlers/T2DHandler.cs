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
    [HarmonyPostfix]
    [HarmonyPatch(typeof(SpriteRenderer), nameof(SpriteRenderer.sprite), MethodType.Setter)]
    public static void SetSpritePostfix(SpriteRenderer __instance, Sprite value)
    {
        if (T2DLoader.IsHandlingOrEnforcing || __instance == null || value == null || __instance.gameObject.name == "TempSpriteRenderer")
            return;

        if (Plugin.Config.DumpSprites && !string.IsNullOrEmpty(value.name) && value.texture != null && !string.IsNullOrEmpty(value.texture.name))
            T2DDumper.HandleDump(value);

        T2DLoader.TrackRenderer(__instance);  // always track for T2D browser

        if (!T2DLoader.HasT2DReplacements)
            return;

        T2DLoader.OnSpriteSet(__instance, value);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Image), nameof(Image.sprite), MethodType.Setter)]
    public static void SetImageSpritePostfix(Image __instance, Sprite value)
    {
        if (T2DLoader.IsHandlingOrEnforcing || __instance == null || value == null)
            return;

        if (Plugin.Config.DumpSprites && !string.IsNullOrEmpty(value.name) && value.texture != null && !string.IsNullOrEmpty(value.texture.name))
            T2DDumper.HandleDump(value);

        T2DLoader.TrackImage(__instance);  // always track for T2D browser

        if (!T2DLoader.HasT2DReplacements)
            return;

        T2DLoader.OnSpriteSet(__instance, value);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Material), nameof(Material.mainTexture), MethodType.Setter)]
    public static void SetMaterialTexturePostfix(Texture value)
    {
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
