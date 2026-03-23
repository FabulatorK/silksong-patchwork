using System;
using System.Reflection;
using HarmonyLib;
using Patchwork.GUI;
using UnityEngine.InputSystem.Controls;

namespace Patchwork.Handlers;

internal static class RawKeyboardLeakBlocker
{
    private static bool _patched;

    public static void ApplyPatches(Harmony harmony)
    {
        if (_patched)
            return;

        try
        {
            Type buttonControlType = typeof(ButtonControl);

            // Button-style polling used by KeyControl and AnyKeyControl.
            PatchPropertyGetter(harmony, buttonControlType, "isPressed", nameof(BlockBoolPrefix));
            PatchPropertyGetter(harmony, buttonControlType, "wasPressedThisFrame", nameof(BlockBoolPrefix));
            PatchPropertyGetter(harmony, buttonControlType, "wasReleasedThisFrame", nameof(BlockBoolPrefix));

            // Raw float value reads from ButtonControl / KeyControl.
            PatchMethod(harmony, buttonControlType, "ReadValue", Type.EmptyTypes, nameof(BlockFloatPrefix));

            _patched = true;
            Plugin.Logger.LogInfo("[RawKeyboardLeakBlocker] ButtonControl polling patches applied.");
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogWarning($"[RawKeyboardLeakBlocker] Failed to apply patches: {ex}");
        }
    }

    private static void PatchPropertyGetter(Harmony harmony, Type targetType, string propertyName, string prefixName)
    {
        MethodInfo target = AccessTools.PropertyGetter(targetType, propertyName);
        PatchWithPrefix(harmony, target, prefixName, $"{targetType.Name}.{propertyName}");
    }

    private static void PatchMethod(Harmony harmony, Type targetType, string methodName, Type[] args, string prefixName)
    {
        MethodInfo target = AccessTools.Method(targetType, methodName, args);
        PatchWithPrefix(harmony, target, prefixName, $"{targetType.Name}.{methodName}()");
    }

    private static void PatchWithPrefix(Harmony harmony, MethodInfo target, string prefixName, string label)
    {
        if (target == null)
        {
            Plugin.Logger.LogWarning($"[RawKeyboardLeakBlocker] Target not found: {label}");
            return;
        }

        MethodInfo prefix = AccessTools.Method(typeof(RawKeyboardLeakBlocker), prefixName);
        if (prefix == null)
        {
            Plugin.Logger.LogWarning($"[RawKeyboardLeakBlocker] Prefix not found: {prefixName}");
            return;
        }

        harmony.Patch(target, prefix: new HarmonyMethod(prefix));
        Plugin.Logger.LogInfo($"[RawKeyboardLeakBlocker] Patched {label}");
    }

    private static bool ShouldBlock(ButtonControl __instance)
    {
        if (!GUIHelper.IsTextFieldFocused)
            return false;

        if (__instance == null)
            return false;

        // Restrict this probe to keyboard-derived controls only.
        // KeyControl derives from ButtonControl, and Keyboard.anyKey is AnyKeyControl,
        // which also derives from ButtonControl.
        var device = __instance.device;
        return device is UnityEngine.InputSystem.Keyboard;
    }

    private static bool BlockBoolPrefix(ButtonControl __instance, ref bool __result)
    {
        if (!ShouldBlock(__instance))
            return true;

        __result = false;
        return false;
    }

    private static bool BlockFloatPrefix(ButtonControl __instance, ref float __result)
    {
        if (!ShouldBlock(__instance))
            return true;

        __result = 0f;
        return false;
    }
}