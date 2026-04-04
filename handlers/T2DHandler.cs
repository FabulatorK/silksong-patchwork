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

        if (T2DLoader.IsT2DLogActive && value.texture != null)
            T2DLoader.RaiseT2DTrigger(
                T2DUtil.CleanTextureName(value.texture.name), value.name);

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

        if (T2DLoader.IsT2DLogActive && value.texture != null)
            T2DLoader.RaiseT2DTrigger(
                T2DUtil.CleanTextureName(value.texture.name), value.name);

        if (!T2DLoader.HasT2DReplacements)
            return;

        T2DLoader.OnSpriteSet(__instance, value);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Material), nameof(Material.mainTexture), MethodType.Setter)]
    public static void SetMaterialTexturePostfix(Texture value)
    {
        if (!(value is Texture2D tex)) return;

        // Log T2D-named texture assignments for the discovery log.
        // Filtered to T2D textures only — plain UI/FX materials produce too much noise.
        if (T2DLoader.IsT2DLogActive && T2DUtil.IsT2DTexture(tex.name))
            T2DLoader.RaiseT2DTrigger(T2DUtil.CleanTextureName(tex.name), "");

        if (!T2DLoader.HasT2DReplacements) return;

        T2DLoader.TrySwapTexture(tex);

        if (Plugin.Config.DumpSprites && T2DUtil.IsT2DTexture(tex.name))
            T2DDumper.DumpAtlasTexture(tex);
    }

    // ─── OnEnable patches ────────────────────────────────────────────────────────
    // Unity's Instantiate copies component state via native serialization, which
    // bypasses the managed C# property setter. The sprite setter Harmony postfix
    // therefore never fires for freshly-spawned objects — their textures stay
    // vanilla until something else re-sets the sprite property in managed code.
    //
    // OnEnable always fires in managed code after the object is fully initialised
    // (including after Instantiate, scene load, pool recycling, etc.). Patching
    // it gives us an immediate, zero-polling hook on every renderer activation.

    [HarmonyPostfix]
    [HarmonyPatch(typeof(SpriteRenderer), "OnEnable")]
    public static void SpriteRendererOnEnablePostfix(SpriteRenderer __instance)
    {
        if (T2DLoader.IsHandlingOrEnforcing || __instance == null || __instance.sprite == null
            || __instance.gameObject.name == "TempSpriteRenderer")
            return;

        T2DLoader.TrackRenderer(__instance);

        if (!T2DLoader.HasT2DReplacements) return;

        T2DLoader.OnSpriteSet(__instance, __instance.sprite);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Image), "OnEnable")]
    public static void ImageOnEnablePostfix(Image __instance)
    {
        if (T2DLoader.IsHandlingOrEnforcing || __instance == null || __instance.sprite == null)
            return;

        T2DLoader.TrackImage(__instance);

        if (!T2DLoader.HasT2DReplacements) return;

        T2DLoader.OnSpriteSet(__instance, __instance.sprite);
    }
}
