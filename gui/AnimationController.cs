using System.Collections.Generic;
using Patchwork;
using UnityEngine;

public static class AnimationController
{
    private static Vector2 scrollPosition = Vector2.zero;
    private static Rect windowRect = new Rect(Screen.width / 2 - 300, 10, 600, 800);

    public static string SelectedAnimator { get; private set; } = null;

    private static bool Paused = false;

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

    public static void DrawAnimationController()
    {
        windowRect = GUILayout.Window(6971, windowRect, AnimationControllerWindow, "Patchwork Animation Controller");
    }

    public static void Update()
    {
        if (Input.GetKeyDown(Plugin.Config.AnimationControllerPauseKey) && SelectedAnimator != null)
        {
            Paused = !Paused;
            if (animators.TryGetValue(SelectedAnimator, out var animator))
            {
                if (Paused)
                    animator.Paused = true;
                else
                    animator.Paused = false;
            }
        }
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
            {
                Plugin.Logger.LogWarning($"Animator {name} has invalid current frame {animator.CurrentFrame} for clip {animator.CurrentClip.name}");
                continue;
            }
            int currentSpriteId = animator.CurrentClip.frames[animator.CurrentFrame].spriteId;
            tk2dSpriteCollectionData spriteCollection = animator.CurrentClip.frames[animator.CurrentFrame].spriteCollection;
            if (spriteCollection == null || currentSpriteId < 0 || currentSpriteId >= spriteCollection.spriteDefinitions.Length)
            {
                Plugin.Logger.LogWarning($"Animator {name} has invalid sprite ID {currentSpriteId} in collection for clip {animator.CurrentClip.name}");
                continue;
            }
            tk2dSpriteDefinition currentFrameDef = spriteCollection.spriteDefinitions[currentSpriteId];

            if (SelectedAnimator == name)
                GUI.contentColor = Color.green;
            else
                GUI.contentColor = Color.white;

            if (GUILayout.Button(name, GUILayout.ExpandWidth(false)))
                SelectAnimator(animator);

            GUILayout.BeginHorizontal();
            GUILayout.Label($"{name}: {spriteCollection.name}/{currentFrameDef.material.name.Split(' ')[0]}/{currentFrameDef.name}");
            if (Paused && SelectedAnimator == name)
            {
                GUI.contentColor = Color.red;
                GUILayout.Label(" [PAUSED]");
                GUI.contentColor = Color.white;
            }
            GUILayout.EndHorizontal();
        }
        GUILayout.EndVertical();
        GUILayout.EndScrollView();
        GUI.DragWindow(new Rect(0, 0, 10000, 20));
    }
}