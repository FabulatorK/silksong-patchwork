using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using HarmonyLib;
using Patchwork;
using Patchwork.Handlers;
using Patchwork.Util;
using Patchwork.GUI;
using UnityEngine;

[HarmonyPatch]
public static class AnimationController
{
    private static Vector2 scrollPosition = Vector2.zero;
    private static Rect windowRect;
    private static bool initialized = false;
    // Constants at top of class (base sizes at 1080p)
    private const float WindowWidthRatio = 0.33f;   // 1/3 of screen width
    private const float WindowHeightRatio = 0.33f;  // 1/3 of screen height
    private const float RightMargin = 10f;
    private const float TopMargin = 10f;

    private const float MinWidth = 300f;
    private const float MinHeight = 200f;

    public static string SelectedAnimator { get; private set; } = null;

    private static bool Paused = false;
    private static bool FrameChangeRequested = false;
    private static bool Frozen = false;
    private static Vector3 FrozenPosition;

    private static readonly Dictionary<string, bool> ShowAnimationDropdown = new Dictionary<string, bool>();

    private static readonly Dictionary<string, tk2dSpriteAnimator> Animators = new Dictionary<string, tk2dSpriteAnimator>();

    private static string _animationSearchText = "";
    private static Vector2 _animationDropdownScroll = Vector2.zero;
    private const int MaxVisibleAnimations = 10;
    private const float MinWindowWidth = 350f;
    private const float MaxWindowWidth = 600f;
    private const int MaxPathLength = 55;

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

        for (int i = 0; i < Animators.Count; i++)
        {
            var kvp = new List<KeyValuePair<string, tk2dSpriteAnimator>>(Animators)[i];
            string name = kvp.Key;
            tk2dSpriteAnimator checkAnimator = kvp.Value;

            if (checkAnimator == null || checkAnimator.gameObject == null || !checkAnimator.gameObject.activeSelf || checkAnimator.CurrentClip == null)
            {
                Animators.Remove(name);
                if (SelectedAnimator == name)
                    SelectedAnimator = null;
                continue;
            }
            if (checkAnimator.CurrentFrame < 0 || checkAnimator.CurrentFrame >= checkAnimator.CurrentClip.frames.Length)
            {
                Animators.Remove(name);
                if (SelectedAnimator == name)
                    SelectedAnimator = null;
                continue;
            }
        }
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
        if (!initialized || windowRect.width < 1)
        {
            float width = Mathf.Max(Screen.width * WindowWidthRatio, GUIHelper.Scaled(MinWidth));
            float height = Mathf.Max(Screen.height * WindowHeightRatio, GUIHelper.Scaled(MinHeight));
            windowRect = new Rect(
                Screen.width - width - GUIHelper.Scaled(RightMargin),
                GUIHelper.Scaled(TopMargin),
                width,
                height
            );
            initialized = true;
        }

        GUIHelper.ApplyScaledSkin();
        windowRect = GUILayout.Window(
            6972,
            windowRect,
            AnimationControllerWindow,
            "Patchwork Animation Controller",
            GUIHelper.WindowStyle,
            GUILayout.MinWidth(GUIHelper.Scaled(MinWindowWidth)),
            GUILayout.MaxWidth(GUIHelper.Scaled(MaxWindowWidth))
        );
    }
    private static void AnimationControllerWindow(int windowID)
    {
        GUIHelper.Space(16); 
        
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

            if (GUILayout.Button(name, GUIHelper.ButtonStyle))
                SelectAnimator(animator);
            
            string fullPath = $"{spriteCollection.name}/{currentFrameDef.material.name.Split(' ')[0]}/{currentFrameDef.name}";
            string displayPath = fullPath.Length > MaxPathLength 
                ? "..." + fullPath.Substring(fullPath.Length - MaxPathLength + 3) 
                : fullPath;

            GUILayout.Label(displayPath, GUIHelper.LabelStyle);

            GUILayout.BeginHorizontal();

            GUIHelper.Space(48);

            if (SelectedAnimator == name)
            {
                GUILayout.BeginVertical();
                if (GUILayout.Button(animator.CurrentClip.name + (ShowAnimationDropdown.GetValueOrDefault(name, false) ? " ▲" : " ▼"), GUIHelper.ButtonStyle))
                {
                    ShowAnimationDropdown[name] = !ShowAnimationDropdown.GetValueOrDefault(name, false);
                    if (ShowAnimationDropdown[name])
                    {
                        _animationSearchText = "";  // Reset search when opening
                        _animationDropdownScroll = Vector2.zero;
                    }
                }

                if (ShowAnimationDropdown.GetValueOrDefault(name, false))
                {
                    GUILayout.BeginVertical(UnityEngine.GUI.skin.box);
                    
                    // Search field
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("Search:", GUIHelper.LabelStyle, GUILayout.Width(GUIHelper.Scaled(60)));
                    _animationSearchText = GUILayout.TextField(
                        _animationSearchText, 
                        GUIHelper.TextFieldStyle, 
                        GUILayout.Width(GUIHelper.Scaled(280)),
                        GUILayout.Height(GUIHelper.Scaled(32))
                    );
                    GUILayout.EndHorizontal();
                    
                    // Filter clips
                    var filteredClips = animator.Library.clips
                        .Where(c => !string.IsNullOrEmpty(c.name) && 
                                    (string.IsNullOrEmpty(_animationSearchText) || 
                                    c.name.ToLower().Contains(_animationSearchText.ToLower())))
                        .ToList();
                    
                    // Show count
                    GUILayout.Label($"{filteredClips.Count} of {animator.Library.clips.Length}", GUIHelper.LabelStyle);
                    
                    // Scrollable list
                    _animationDropdownScroll = GUILayout.BeginScrollView(
                        _animationDropdownScroll, 
                        GUILayout.Height(GUIHelper.Scaled(MaxVisibleAnimations * 22)));
                    
                    foreach (var clip in filteredClips)
                    {
                        if (GUILayout.Button(clip.name, GUIHelper.ButtonStyle))
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
                    
                    GUILayout.EndScrollView();
                    GUILayout.EndVertical();
                }
                GUILayout.EndVertical();
            }
            else
                GUILayout.Label(animator.CurrentClip.name, GUIHelper.LabelStyle);

            if (Paused && SelectedAnimator == name)
            {
                Color temp = GUI.contentColor;
                GUI.contentColor = Color.red;
                GUILayout.Label("[PAUSED]", GUIHelper.LabelStyle);
                GUI.contentColor = temp;
            }

            if (Frozen && SelectedAnimator == name)
            {
                Color temp = GUI.contentColor;
                GUI.contentColor = Color.cyan;
                GUILayout.Label("[FROZEN]", GUIHelper.LabelStyle);
                GUI.contentColor = temp;
            }
            GUILayout.Label($"[Frame {animator.CurrentFrame + 1}/{animator.CurrentClip.frames.Length}]", GUIHelper.LabelStyle);
            GUILayout.EndHorizontal();

            if (SelectedAnimator == name)
            {
                if (GUILayout.Button("Edit Current Sprite", GUIHelper.ButtonStyle))
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
        GUI.DragWindow(GUIHelper.DragRect);
    }
    #endregion
}