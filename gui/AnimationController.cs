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
    private static Vector2 _listScroll = Vector2.zero;

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
    /// Two-section layout: scrollable animator list (top) + standalone frame preview (bottom).
    /// GraphicsPillar must NOT wrap this in an additional scroll view.
    /// </summary>
    public static void DrawPillarContent()
    {
        // ── Scrollable object list ────────────────────────────────────────────
        _listScroll = GUILayout.BeginScrollView(_listScroll, GUILayout.ExpandHeight(true));
        DrawAnimatorEntries();
        GUILayout.EndScrollView();

        // ── Standalone frame preview ──────────────────────────────────────────
        // Rendered outside the scroll so it stays anchored at the bottom regardless
        // of how far the list is scrolled. Updates live with the current frame.
        GUIHelper.Space(4);
        DrawStandalonePreview();
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

            // ── Object name button ────────────────────────────────────────────
            GUI.contentColor = SelectedAnimator == name ? Color.green : Color.white;
            if (GUILayout.Button(name, GUIHelper.ButtonStyle))
                SelectAnimator(animator);
            GUI.contentColor = Color.white;

            // ── Current sprite path: collection / spriteName (no material tier) ──
            // The material/atlas changes per-frame when sprites span multiple atlases;
            // displaying it here would imply the whole animation lives on one atlas.
            // The standalone preview below shows the per-frame atlas name accurately.
            string spritePath = $"{spriteCollection.name}/{currentFrameDef.name}";
            if (spritePath.Length > MaxPathLength)
                spritePath = "..." + spritePath.Substring(spritePath.Length - MaxPathLength + 3);
            GUILayout.Label(spritePath, GUIHelper.LabelStyle);

            // ── Clip row ──────────────────────────────────────────────────────
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
        }

        if (Animators.Count == 0)
        {
            GUI.contentColor = new Color(0.6f, 0.6f, 0.6f);
            GUILayout.Label("No animators registered.", GUIHelper.LabelStyle);
            GUI.contentColor = Color.white;
        }
    }

    // ── Standalone preview ────────────────────────────────────────────────────

    private static Texture2D _previewWhite;

    /// <summary>
    /// Atlas preview + edit buttons for the currently selected animator's current frame.
    /// Rendered below the scrollable list — always visible, independent of scroll position.
    /// Shows the per-frame material/atlas name so cross-atlas animations are represented
    /// accurately frame by frame.
    /// </summary>
    private static void DrawStandalonePreview()
    {
        if (SelectedAnimator == null || !Animators.TryGetValue(SelectedAnimator, out var animator))
        {
            GUI.contentColor = new Color(0.5f, 0.5f, 0.5f);
            GUILayout.Label("Select an object above to preview.", GUIHelper.LabelStyle);
            GUI.contentColor = Color.white;
            return;
        }

        if (animator == null || animator.CurrentClip == null) return;
        if (animator.CurrentFrame < 0 || animator.CurrentFrame >= animator.CurrentClip.frames.Length) return;

        var frame  = animator.CurrentClip.frames[animator.CurrentFrame];
        var coll   = frame.spriteCollection;
        if (coll == null || frame.spriteId < 0 || frame.spriteId >= coll.spriteDefinitions.Length) return;
        var frameDef = coll.spriteDefinitions[frame.spriteId];
        var mat      = frameDef.materialInst ?? frameDef.material;
        if (mat?.mainTexture == null) return;

        // ── Info row: collection · atlas · sprite · frame ────────────────────
        string atlasName = mat.name.Split(' ')[0];
        GUI.contentColor = new Color(0.6f, 0.85f, 1f);
        GUILayout.Label(
            $"{coll.name}  ·  {atlasName}  ·  {frameDef.name}" +
            $"  [{animator.CurrentFrame + 1}/{animator.CurrentClip.frames.Length}]",
            GUIHelper.LabelStyle);
        GUI.contentColor = Color.white;

        // ── Atlas texture with UV highlight ───────────────────────────────────
        float previewH = GUIHelper.Scaled(150f);
        Rect atlasRect = GUILayoutUtility.GetRect(0f, float.MaxValue, previewH, previewH);
        GUI.DrawTexture(atlasRect, mat.mainTexture, ScaleMode.ScaleToFit, true);

        var uvs = frameDef.uvs;
        if (uvs != null && uvs.Length >= 4)
        {
            float minU = uvs.Min(v => v.x), maxU = uvs.Max(v => v.x);
            float minV = uvs.Min(v => v.y), maxV = uvs.Max(v => v.y);

            Texture tex  = mat.mainTexture;
            float scaleW = atlasRect.width  / tex.width;
            float scaleH = atlasRect.height / tex.height;
            float scale  = Mathf.Min(scaleW, scaleH);
            float fw = tex.width  * scale;
            float fh = tex.height * scale;
            Rect fitted = new Rect(
                atlasRect.x + (atlasRect.width  - fw) * 0.5f,
                atlasRect.y + (atlasRect.height - fh) * 0.5f,
                fw, fh);

            float hx = fitted.x + minU * fitted.width;
            float hy = fitted.y + (1f - maxV) * fitted.height;
            float hw = (maxU - minU) * fitted.width;
            float hh = (maxV - minV) * fitted.height;

            if (_previewWhite == null)
            {
                _previewWhite = new Texture2D(1, 1);
                _previewWhite.SetPixel(0, 0, Color.white);
                _previewWhite.Apply();
            }

            GUI.color = new Color(1f, 1f, 0f, 0.4f);
            GUI.DrawTexture(new Rect(hx, hy, hw, hh), _previewWhite);
            GUI.color = Color.white;
        }

        // ── Edit buttons ──────────────────────────────────────────────────────
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Edit Current Sprite", GUIHelper.ButtonStyle))
        {
            string matName  = frameDef.material.name.Split(' ')[0];
            string openPath = Path.Combine(SpriteLoader.LoadPath, coll.name, matName, frameDef.name + ".png");
            if (File.Exists(openPath))
                Process.Start(openPath);
            else
            {
                SpriteDumper.DumpSingleSprite(frameDef, coll);
                string dumpedPath = Path.Combine(SpriteDumper.DumpPath, coll.name, matName, frameDef.name + ".png");
                if (!File.Exists(dumpedPath))
                    Plugin.Logger.LogError($"Failed to dump sprite for editing: {coll.name}/{matName}/{frameDef.name}");
                else
                {
                    IOUtil.EnsureDirectoryExists(Path.Combine(SpriteLoader.LoadPath, coll.name, matName));
                    File.Copy(dumpedPath, openPath, true);
                    Process.Start(openPath);
                }
            }
        }

        if (GUILayout.Button("Edit All Animation Sprites", GUIHelper.ButtonStyle))
        {
            var clip  = animator.CurrentClip;
            int dumped = 0;
            for (int f = 0; f < clip.frames.Length; f++)
            {
                var fr   = clip.frames[f];
                var fc   = fr.spriteCollection;
                if (fc == null || fr.spriteId < 0 || fr.spriteId >= fc.spriteDefinitions.Length) continue;
                var fd   = fc.spriteDefinitions[fr.spriteId];
                if (string.IsNullOrEmpty(fd.name)) continue;
                string mn    = fd.material.name.Split(' ')[0];
                string lpath = Path.Combine(SpriteLoader.LoadPath, fc.name, mn, fd.name + ".png");
                if (File.Exists(lpath)) { dumped++; continue; }
                SpriteDumper.DumpSingleSprite(fd, fc);
                string dpath = Path.Combine(SpriteDumper.DumpPath, fc.name, mn, fd.name + ".png");
                if (!File.Exists(dpath)) { Plugin.Logger.LogError($"Failed to dump: {fc.name}/{mn}/{fd.name}"); continue; }
                IOUtil.EnsureDirectoryExists(Path.Combine(SpriteLoader.LoadPath, fc.name, mn));
                File.Copy(dpath, lpath, true);
                dumped++;
            }
            Plugin.Logger.LogInfo($"[AnimCtrl] Copied {dumped} sprites from '{clip.name}' ({clip.frames.Length} frames) to {SpriteLoader.LoadPath}");
            for (int f2 = 0; f2 < clip.frames.Length; f2++)
            {
                var fr2 = clip.frames[f2];
                var fc2 = fr2.spriteCollection;
                if (fc2 == null || fr2.spriteId < 0 || fr2.spriteId >= fc2.spriteDefinitions.Length) continue;
                var fd2 = fc2.spriteDefinitions[fr2.spriteId];
                if (string.IsNullOrEmpty(fd2.name)) continue;
                string mn2    = fd2.material.name.Split(' ')[0];
                string opath2 = Path.Combine(SpriteLoader.LoadPath, fc2.name, mn2, fd2.name + ".png");
                if (File.Exists(opath2)) Process.Start(opath2);
            }
        }
        GUILayout.EndHorizontal();
    }

    #endregion
}
