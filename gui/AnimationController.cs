using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using HarmonyLib;
using Patchwork;
using Patchwork.Handlers;
using Patchwork.Util;
using UnityEngine;

[HarmonyPatch]
public static class AnimationController
{
    private static Vector2 scrollPosition = Vector2.zero;
    private static Rect windowRect;

    public static string SelectedAnimator { get; private set; } = null;

    private static bool Paused = false;
    private static bool FrameChangeRequested = false;
    private static bool Frozen = false;
    private static Vector3 FrozenPosition;

    private static readonly Dictionary<string, bool> ShowAnimationDropdown = new Dictionary<string, bool>();

    private static readonly Dictionary<string, tk2dSpriteAnimator> Animators = new Dictionary<string, tk2dSpriteAnimator>();

    public static void RegisterAnimator(tk2dSpriteAnimator animator)
    {
        if (animator != null && !Animators.ContainsKey(animator.gameObject.name))
            Animators.Add(animator.gameObject.name, animator);
    }

    public static void ClearAnimators()
    {
        Animators.Clear();
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
            if (Animators.TryGetValue(SelectedAnimator, out var animator))
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
            if (Animators.TryGetValue(SelectedAnimator, out var animator))
                FrozenPosition = animator.gameObject.transform.position;
        }

        if (Paused && SelectedAnimator != null && Animators.TryGetValue(SelectedAnimator, out var selectedAnimator))
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

        if (Frozen && SelectedAnimator != null && Animators.TryGetValue(SelectedAnimator, out var frozenAnimator))
            frozenAnimator.gameObject.transform.position = FrozenPosition;
    }

    private static void SelectAnimator(tk2dSpriteAnimator animator)
    {
        Frozen = false;
        if (Paused && SelectedAnimator != null && Animators.TryGetValue(SelectedAnimator, out var currentAnimator))
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
        if (windowRect.width == 0)
            windowRect = new Rect(Screen.width - Screen.width / 3 - 10, 10, Screen.width / 3, Screen.height / 3);
        windowRect = GUILayout.Window(6971, windowRect, AnimationControllerWindow, "Patchwork Animation Controller", GUILayout.MinWidth(300), GUILayout.MinHeight(200));
    }

    private static void AnimationControllerWindow(int windowID)
    {
        scrollPosition = GUILayout.BeginScrollView(scrollPosition);
        GUILayout.BeginVertical();
        foreach (var kvp in Animators)
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

            if (GUILayout.Button(name))
                SelectAnimator(animator);

            GUILayout.BeginHorizontal();
            GUILayout.Label($"{spriteCollection.name}/{currentFrameDef.material.name.Split(' ')[0]}/{currentFrameDef.name}");

            GUILayout.FlexibleSpace();

            if (SelectedAnimator == name)
            {
                GUILayout.BeginVertical();
                if (GUILayout.Button(animator.CurrentClip.name + (ShowAnimationDropdown.GetValueOrDefault(name, false) ? " ▲" : " ▼")))
                    ShowAnimationDropdown[name] = !ShowAnimationDropdown.GetValueOrDefault(name, false);
                if (ShowAnimationDropdown.GetValueOrDefault(name, false))
                {
                    foreach (var clip in animator.Library.clips)
                    {
                        if (string.IsNullOrEmpty(clip.name))
                            continue;
                        if (GUILayout.Button(clip.name))
                        {
                            Paused = true;
                            animator.Pause();
                            ShowAnimationDropdown[name] = false;
                            FrameChangeRequested = true;
                            animator.Play(clip);
                            animator.UpdateAnimation(Time.deltaTime);
                            FrameChangeRequested = false;
                        }
                    }
                }
                GUILayout.EndVertical();
            }
            else
                GUILayout.Label(animator.CurrentClip.name);

            if (Paused && SelectedAnimator == name)
            {
                Color temp = GUI.contentColor;
                GUI.contentColor = Color.red;
                GUILayout.Label("[PAUSED]");
                GUI.contentColor = temp;
            }

            if (Frozen && SelectedAnimator == name)
            {
                Color temp = GUI.contentColor;
                GUI.contentColor = Color.cyan;
                GUILayout.Label("[FROZEN]");
                GUI.contentColor = temp;
            }
            GUILayout.Label($"[Frame {animator.CurrentFrame + 1}/{animator.CurrentClip.frames.Length}]");
            GUILayout.EndHorizontal();

            if (SelectedAnimator == name)
            {
                if (GUILayout.Button("Edit Current Sprite"))
                {
                    string openPath = Path.Combine(SpriteLoader.LoadPath, spriteCollection.name, currentFrameDef.material.name.Split(' ')[0], currentFrameDef.name + ".png");
                    if (File.Exists(openPath))
                        Process.Start(openPath);
                    else
                    {
                        SpriteDumper.DumpSingleSprite(currentFrameDef, spriteCollection);
                        if (!File.Exists(Path.Combine(SpriteDumper.DumpPath, spriteCollection.name, currentFrameDef.material.name.Split(' ')[0], currentFrameDef.name + ".png")))
                            Plugin.Logger.LogError($"Failed to dump sprite for editing: {spriteCollection.name}/{currentFrameDef.material.name.Split(' ')[0]}/{currentFrameDef.name}");
                        else
                        {
                            IOUtil.EnsureDirectoryExists(Path.Combine(SpriteLoader.LoadPath, spriteCollection.name, currentFrameDef.material.name.Split(' ')[0]));
                            File.Copy(
                                Path.Combine(SpriteDumper.DumpPath, spriteCollection.name, currentFrameDef.material.name.Split(' ')[0], currentFrameDef.name + ".png"),
                                openPath,
                                true
                            );
                            Process.Start(openPath);
                        }
                    }
                }
            }
        }
        GUILayout.EndVertical();
        GUILayout.EndScrollView();
        GUI.DragWindow(new Rect(0, 0, 10000, 20));
    }
    #endregion
}