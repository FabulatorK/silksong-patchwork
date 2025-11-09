using System.Collections.Generic;
using HarmonyLib;
using Patchwork;
using UnityEngine;

[HarmonyPatch]
public static class AnimationController
{
    private static Vector2 scrollPosition = Vector2.zero;
    private static Rect windowRect = new Rect(Screen.width / 3, 10, 600, 800);

    public static string SelectedAnimator { get; private set; } = null;

    private static bool Paused = false;
    private static bool FrameChangeRequested = false;
    private static bool Frozen = false;
    private static Vector3 FrozenPosition;

    private static Dictionary<string, tk2dSpriteAnimator> animators = new Dictionary<string, tk2dSpriteAnimator>();

    public static void RegisterAnimator(tk2dSpriteAnimator animator)
    {
        if (animator != null && !animators.ContainsKey(animator.gameObject.name))
            animators.Add(animator.gameObject.name, animator);
    }

    public static void ClearAnimators()
    {
        animators.Clear();
    }

    #region Patching
    public static void ApplyPatches(Harmony harmony)
    {
        harmony.Patch(
            AccessTools.Method(typeof(tk2dSpriteAnimator), nameof(tk2dSpriteAnimator.Play)),
            prefix: new HarmonyMethod(typeof(AnimationController), nameof(PlayPatch))
        );
        harmony.Patch(
            AccessTools.Method(typeof(tk2dSpriteAnimator), nameof(tk2dSpriteAnimator.Play), new[] { typeof(string) }),
            prefix: new HarmonyMethod(typeof(AnimationController), nameof(PlayWithNamePatch))
        );
        harmony.Patch(
            AccessTools.Method(typeof(tk2dSpriteAnimator), nameof(tk2dSpriteAnimator.Play), new[] { typeof(tk2dSpriteAnimationClip) }),
            prefix: new HarmonyMethod(typeof(AnimationController), nameof(PlayWithClipPatch))
        );

        harmony.Patch(
            AccessTools.Method(typeof(tk2dSpriteAnimator), nameof(tk2dSpriteAnimator.PlayFromFrame), new[] { typeof(int) }),
            prefix: new HarmonyMethod(typeof(AnimationController), nameof(PlayFromFramePatch))
        );
        harmony.Patch(
            AccessTools.Method(typeof(tk2dSpriteAnimator), nameof(tk2dSpriteAnimator.PlayFromFrame), new[] { typeof(string), typeof(int) }),
            prefix: new HarmonyMethod(typeof(AnimationController), nameof(PlayFromFrameWithNamePatch))
        );
        harmony.Patch(
            AccessTools.Method(typeof(tk2dSpriteAnimator), nameof(tk2dSpriteAnimator.PlayFromFrame), new[] { typeof(tk2dSpriteAnimationClip), typeof(int) }),
            prefix: new HarmonyMethod(typeof(AnimationController), nameof(PlayFromFrameWithClipPatch))
        );

        harmony.Patch(
            AccessTools.Method(typeof(tk2dSpriteAnimator), nameof(tk2dSpriteAnimator.PlayFrom), new[] { typeof(float) }),
            prefix: new HarmonyMethod(typeof(AnimationController), nameof(PlayFromPatch))
        );
        harmony.Patch(
            AccessTools.Method(typeof(tk2dSpriteAnimator), nameof(tk2dSpriteAnimator.PlayFrom), new[] { typeof(string), typeof(float) }),
            prefix: new HarmonyMethod(typeof(AnimationController), nameof(PlayFromWithNamePatch))
        );
        harmony.Patch(
            AccessTools.Method(typeof(tk2dSpriteAnimator), nameof(tk2dSpriteAnimator.PlayFrom), new[] { typeof(tk2dSpriteAnimationClip), typeof(float) }),
            prefix: new HarmonyMethod(typeof(AnimationController), nameof(PlayFromWithClipPatch))
        );
        harmony.Patch(
            AccessTools.Method(typeof(tk2dSpriteAnimator), nameof(tk2dSpriteAnimator.Play), new[] { typeof(tk2dSpriteAnimationClip), typeof(float), typeof(float) }),
            prefix: new HarmonyMethod(typeof(AnimationController), nameof(PlayOverrideFpsPatch))
        );

    }

    private static bool PlayPatchInternal(tk2dSpriteAnimator __instance)
    {
        if (__instance != null)
        {
            RegisterAnimator(__instance);
            if (SelectedAnimator == __instance.gameObject.name && Paused && !FrameChangeRequested)
            {
                return false;
            }
        }
        return true;
    }

    public static bool PlayPatch(tk2dSpriteAnimator __instance) => PlayPatchInternal(__instance);
    public static bool PlayWithNamePatch(tk2dSpriteAnimator __instance, string name) => PlayPatchInternal(__instance);
    public static bool PlayWithClipPatch(tk2dSpriteAnimator __instance, tk2dSpriteAnimationClip clip) => PlayPatchInternal(__instance);
    public static bool PlayFromFramePatch(tk2dSpriteAnimator __instance, int frame) => PlayPatchInternal(__instance);
    public static bool PlayFromFrameWithNamePatch(tk2dSpriteAnimator __instance, string name, int frame) => PlayPatchInternal(__instance);
    public static bool PlayFromFrameWithClipPatch(tk2dSpriteAnimator __instance, tk2dSpriteAnimationClip clip, int frame) => PlayPatchInternal(__instance);
    public static bool PlayFromPatch(tk2dSpriteAnimator __instance, float clipStartTime) => PlayPatchInternal(__instance);
    public static bool PlayFromWithNamePatch(tk2dSpriteAnimator __instance, string name, float clipStartTime) => PlayPatchInternal(__instance);
    public static bool PlayFromWithClipPatch(tk2dSpriteAnimator __instance, tk2dSpriteAnimationClip clip, float clipStartTime) => PlayPatchInternal(__instance);
    public static bool PlayOverrideFpsPatch(tk2dSpriteAnimator __instance, tk2dSpriteAnimationClip clip, float clipStartTime, float overrideFps) => PlayPatchInternal(__instance);
    #endregion

    #region Functionality
    public static void Update()
    {
        if (Input.GetKeyDown(Plugin.Config.AnimationControllerPauseKey) && SelectedAnimator != null)
        {
            Paused = !Paused;
            if (animators.TryGetValue(SelectedAnimator, out var animator))
            {
                if (Paused)
                    animator.Pause();
                else
                    animator.Resume();
            }
        }

        if (Input.GetKeyDown(Plugin.Config.AnimationControllerFreezeKey) && SelectedAnimator != null)
        {
            Frozen = !Frozen;
            if (animators.TryGetValue(SelectedAnimator, out var animator))
                FrozenPosition = animator.gameObject.transform.position;
        }

        if (Paused && SelectedAnimator != null && animators.TryGetValue(SelectedAnimator, out var selectedAnimator))
        {
            if (Input.GetKeyDown(Plugin.Config.AnimationControllerNextFrameKey))
            {
                int nextFrame = selectedAnimator.CurrentFrame + 1;
                if (nextFrame >= selectedAnimator.CurrentClip.frames.Length)
                    nextFrame = 0;
                FrameChangeRequested = true;
                selectedAnimator.PlayFromFrame(nextFrame);
                selectedAnimator.UpdateAnimation(Time.deltaTime);
                FrameChangeRequested = false;
            }
            if (Input.GetKeyDown(Plugin.Config.AnimationControllerPrevFrameKey))
            {
                int prevFrame = selectedAnimator.CurrentFrame - 1;
                if (prevFrame < 0)
                    prevFrame = selectedAnimator.CurrentClip.frames.Length - 1;
                FrameChangeRequested = true;
                selectedAnimator.PlayFromFrame(prevFrame);
                selectedAnimator.UpdateAnimation(Time.deltaTime);
                FrameChangeRequested = false;
            }
        }

        if (Frozen && SelectedAnimator != null && animators.TryGetValue(SelectedAnimator, out var frozenAnimator))
            frozenAnimator.gameObject.transform.position = FrozenPosition;
    }

    private static void SelectAnimator(tk2dSpriteAnimator animator)
    {
        if (Paused && SelectedAnimator != null && animators.TryGetValue(SelectedAnimator, out var currentAnimator))
        {
            currentAnimator.Paused = false;
            Paused = false;
        }
        SelectedAnimator = animator.gameObject.name;
    }
    #endregion

    #region GUI
    public static void DrawAnimationController()
    {
        windowRect = GUILayout.Window(6971, windowRect, AnimationControllerWindow, "Patchwork Animation Controller");
    }

    private static void AnimationControllerWindow(int windowID)
    {
        scrollPosition = GUILayout.BeginScrollView(scrollPosition);
        GUILayout.BeginVertical();
        foreach (var kvp in animators)
        {
            string name = kvp.Key;
            tk2dSpriteAnimator animator = kvp.Value;

            if (animator == null || animator.gameObject == null || !animator.gameObject.activeSelf || animator.CurrentClip == null)
                continue;

            if (animator.CurrentFrame < 0 || animator.CurrentFrame >= animator.CurrentClip.frames.Length)
                continue;
            int currentSpriteId = animator.CurrentClip.frames[animator.CurrentFrame].spriteId;
            tk2dSpriteCollectionData spriteCollection = animator.CurrentClip.frames[animator.CurrentFrame].spriteCollection;
            if (spriteCollection == null || currentSpriteId < 0 || currentSpriteId >= spriteCollection.spriteDefinitions.Length)
                continue;
            tk2dSpriteDefinition currentFrameDef = spriteCollection.spriteDefinitions[currentSpriteId];

            if (SelectedAnimator == name)
                GUI.contentColor = Color.green;
            else
                GUI.contentColor = Color.white;

            GUILayout.BeginHorizontal(GUILayout.ExpandWidth(true));
            if (GUILayout.Button(name, GUILayout.ExpandWidth(false)))
                SelectAnimator(animator);
            GUILayout.Label($"{spriteCollection.name}/{currentFrameDef.material.name.Split(' ')[0]}/{currentFrameDef.name}");

            if (Paused && SelectedAnimator == name)
            {
                GUI.contentColor = Color.red;
                GUILayout.Label(" [PAUSED]");
                GUI.contentColor = Color.white;
            }

            if (Frozen && SelectedAnimator == name)
            {
                GUI.contentColor = Color.cyan;
                GUILayout.Label(" [FROZEN]");
                GUI.contentColor = Color.white;
            }

            GUILayout.EndHorizontal();
        }
        GUILayout.EndVertical();
        GUILayout.EndScrollView();
        GUI.DragWindow(new Rect(0, 0, 10000, 20));
    }
    #endregion
}