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

    private const float MinWidth = 300f;
    private const float MinHeight = 200f;

    public static string SelectedAnimator { get; private set; } = null;

    private static bool Paused = false;
    private static bool FrameChangeRequested = false;
    private static bool Frozen = false;
    private static Vector3 FrozenPosition;

    private static readonly Dictionary<string, bool> ShowAnimationDropdown = new Dictionary<string, bool>();

    private static readonly Dictionary<string, tk2dSpriteAnimator> Animators = new Dictionary<string, tk2dSpriteAnimator>();
    private static readonly List<string> _removeKeys = new(); // reused each frame to avoid allocations

    private static string _animationSearchText = "";
    private static Vector2 _animationDropdownScroll = Vector2.zero;
    private const int MaxVisibleAnimations = 10;
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
        if (!GUIHelper.IsTextFieldFocused && Input.GetKeyDown(Plugin.Config.AnimationControllerPauseKey) && SelectedAnimator != null)
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

        if (!GUIHelper.IsTextFieldFocused && Input.GetKeyDown(Plugin.Config.AnimationControllerFreezeKey) && SelectedAnimator != null)
        {
            Frozen = !Frozen;
            if (Animators.TryGetValue(SelectedAnimator, out var animator))
                FrozenPosition = animator.gameObject.transform.position;
        }

        if (Paused && SelectedAnimator != null && Animators.TryGetValue(SelectedAnimator, out var selectedAnimator))
        {
            if (!GUIHelper.IsTextFieldFocused && Input.GetKeyDown(Plugin.Config.AnimationControllerNextFrameKey))
            {
                int nextFrame = selectedAnimator.CurrentFrame + 1;
                if (nextFrame >= selectedAnimator.CurrentClip.frames.Length)
                    nextFrame = 0;
                FrameChangeRequested = true;
                selectedAnimator.PlayFromFrame(nextFrame);
                selectedAnimator.UpdateAnimation(Time.deltaTime);
                FrameChangeRequested = false;
            }
            if (!GUIHelper.IsTextFieldFocused && Input.GetKeyDown(Plugin.Config.AnimationControllerPrevFrameKey))
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

        // Cleanup sweep: remove dead/inactive animators.
        // For the selected animator, tolerate transient invalid states (e.g. null CurrentClip
        // or out-of-range CurrentFrame during animation transitions like turning around).
        // These resolve within a few frames — removing eagerly causes the UI to "unhook".
        _removeKeys.Clear();
        foreach (var kvp in Animators)
        {
            string name = kvp.Key;
            tk2dSpriteAnimator checkAnimator = kvp.Value;

            // Truly dead: native object destroyed or GameObject gone
            if (checkAnimator == null || checkAnimator.gameObject == null)
            {
                _removeKeys.Add(name);
                if (SelectedAnimator == name)
                    SelectedAnimator = null;
                continue;
            }

            // Inactive GameObject — remove non-selected, skip selected (may reactivate)
            if (!checkAnimator.gameObject.activeSelf)
            {
                if (SelectedAnimator != name)
                    _removeKeys.Add(name);
                continue;
            }

            // Transient animation state (null clip, out-of-range frame) — only remove non-selected
            if (checkAnimator.CurrentClip == null ||
                checkAnimator.CurrentFrame < 0 ||
                checkAnimator.CurrentFrame >= checkAnimator.CurrentClip.frames.Length)
            {
                if (SelectedAnimator != name)
                    _removeKeys.Add(name);
                continue;
            }
        }
        foreach (var key in _removeKeys)
            Animators.Remove(key);
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
    /// <summary>
    /// Renders the animator list content without window chrome or scroll view.
    /// Called by GraphicsPillar — the caller (GraphicsPillar) wraps this in a scroll view.
    /// </summary>
    public static void DrawPillarContent()
    {
        DrawAnimatorEntries();
    }

    private static void DrawAnimatorEntries()
    {
        foreach (var kvp in Animators.OrderByDescending(k => k.Key == "Hero_Hornet(Clone)"))
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
                if (GUILayout.Button(animator.CurrentClip.name + (ShowAnimationDropdown.GetValueOrDefault(name, false) ? " \u25B2" : " \u25BC"), GUIHelper.ButtonStyle))
                {
                    ShowAnimationDropdown[name] = !ShowAnimationDropdown.GetValueOrDefault(name, false);
                    if (ShowAnimationDropdown[name])
                    {
                        _animationSearchText = "";
                        _animationDropdownScroll = Vector2.zero;
                    }
                }

                if (ShowAnimationDropdown.GetValueOrDefault(name, false))
                {
                    GUILayout.BeginVertical(UnityEngine.GUI.skin.box);
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("Search:", GUIHelper.LabelStyle, GUIHelper.Width(60));
                    _animationSearchText = GUIHelper.TextField(_animationSearchText, GUIHelper.Width(280), GUIHelper.Height(32));
                    GUILayout.EndHorizontal();

                    var filteredClips = animator.Library.clips
                        .Where(c => !string.IsNullOrEmpty(c.name) &&
                                    (string.IsNullOrEmpty(_animationSearchText) ||
                                     c.name.ToLower().Contains(_animationSearchText.ToLower())))
                        .ToList();

                    GUILayout.Label($"{filteredClips.Count} of {animator.Library.clips.Length}", GUIHelper.LabelStyle);

                    _animationDropdownScroll = GUILayout.BeginScrollView(
                        _animationDropdownScroll,
                        GUIHelper.Height(MaxVisibleAnimations * 22));

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
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Edit Current Sprite", GUIHelper.ButtonStyle))
                {
                    string openPath = Path.Combine(SpriteLoader.LoadPath, spriteCollection.name, currentFrameDef.material.name.Split(' ')[0], currentFrameDef.name + ".png");
                    if (File.Exists(openPath))
                        Process.Start(openPath);
                    else
                    {
                        SpriteDumper.DumpSingleSprite(currentFrameDef, spriteCollection);
                        string dumpedPath = Path.Combine(SpriteDumper.DumpPath, spriteCollection.name, currentFrameDef.material.name.Split(' ')[0], currentFrameDef.name + ".png");
                        if (!File.Exists(dumpedPath))
                            Plugin.Logger.LogError($"Failed to dump sprite for editing: {spriteCollection.name}/{currentFrameDef.material.name.Split(' ')[0]}/{currentFrameDef.name}");
                        else
                        {
                            IOUtil.EnsureDirectoryExists(Path.Combine(SpriteLoader.LoadPath, spriteCollection.name, currentFrameDef.material.name.Split(' ')[0]));
                            File.Copy(dumpedPath, openPath, true);
                            Process.Start(openPath);
                        }
                    }
                }

                if (GUILayout.Button("Edit All Animation Sprites", GUIHelper.ButtonStyle))
                {
                    var clip = animator.CurrentClip;
                    int dumped = 0;
                    for (int f = 0; f < clip.frames.Length; f++)
                    {
                        var frame = clip.frames[f];
                        var frameCollection = frame.spriteCollection;
                        if (frameCollection == null || frame.spriteId < 0 || frame.spriteId >= frameCollection.spriteDefinitions.Length)
                            continue;
                        var frameDef = frameCollection.spriteDefinitions[frame.spriteId];
                        if (string.IsNullOrEmpty(frameDef.name))
                            continue;
                        string matname = frameDef.material.name.Split(' ')[0];
                        string loadPath = Path.Combine(SpriteLoader.LoadPath, frameCollection.name, matname, frameDef.name + ".png");
                        if (File.Exists(loadPath)) { dumped++; continue; }
                        SpriteDumper.DumpSingleSprite(frameDef, frameCollection);
                        string dumpPath2 = Path.Combine(SpriteDumper.DumpPath, frameCollection.name, matname, frameDef.name + ".png");
                        if (!File.Exists(dumpPath2)) { Plugin.Logger.LogError($"Failed to dump sprite: {frameCollection.name}/{matname}/{frameDef.name}"); continue; }
                        IOUtil.EnsureDirectoryExists(Path.Combine(SpriteLoader.LoadPath, frameCollection.name, matname));
                        File.Copy(dumpPath2, loadPath, true);
                        dumped++;
                    }
                    Plugin.Logger.LogInfo($"[AnimCtrl] Copied {dumped} sprites from animation '{clip.name}' ({clip.frames.Length} frames) to {SpriteLoader.LoadPath}");

                    for (int f2 = 0; f2 < clip.frames.Length; f2++)
                    {
                        var openFrame = clip.frames[f2];
                        var openCollection = openFrame.spriteCollection;
                        if (openCollection == null || openFrame.spriteId < 0 || openFrame.spriteId >= openCollection.spriteDefinitions.Length)
                            continue;
                        var openDef = openCollection.spriteDefinitions[openFrame.spriteId];
                        if (string.IsNullOrEmpty(openDef.name)) continue;
                        string openMatname = openDef.material.name.Split(' ')[0];
                        string openPath2 = Path.Combine(SpriteLoader.LoadPath, openCollection.name, openMatname, openDef.name + ".png");
                        if (File.Exists(openPath2))
                            Process.Start(openPath2);
                    }
                }
                GUILayout.EndHorizontal();
            }
        }

        if (Animators.Count == 0)
        {
            GUI.contentColor = new Color(0.6f, 0.6f, 0.6f);
            GUILayout.Label("No animators registered.", GUIHelper.LabelStyle);
            GUI.contentColor = Color.white;
        }
    }

    #endregion
}