using System;
using System.Collections.Generic;
using System.IO;
using Patchwork.Handlers;
using UnityEngine;

namespace Patchwork.GUI;

public static class AudioLog
{
    private static readonly List<AudioPlayEntry> AudioPlayEntries = new();

    // Base dimensions at 1080p - will be scaled automatically
    private const float WindowWidth = 300f;
    private const float WindowHeight = 400f;
    private const float RightMargin = 10f;
    private const float TopMargin = 10f;

    private static Rect windowRect;
    private static bool initialized = false;

    public static void DrawAudioLog()
    {
        // Initialize or recalculate on resolution change
        if (!initialized || windowRect.width < 1)
        {
            windowRect = GUIHelper.ScaledRectFromRight(RightMargin, TopMargin, WindowWidth, WindowHeight);
            initialized = true;
        }

        GUIHelper.ApplyScaledSkin();
        windowRect = GUILayout.Window(
            6969,
            windowRect,
            AudioLogWindow,
            "Patchwork Audio Log",
            GUIHelper.WindowStyle,
            GUIHelper.WindowLayout(WindowWidth, WindowHeight)
        );
    }

    private static void AudioLogWindow(int windowID)
    {
        GUIHelper.Space(16);
        int maxVisible    = Mathf.Clamp(Plugin.Config.AudioLogMaxVisible, 5, 50);
        double fadeDuration = Plugin.Config.LogAudioDuration;
        DrawEntries(maxVisible, fadeDuration, Plugin.Config.HideModdedAudioInLog);
        UnityEngine.GUI.DragWindow(GUIHelper.DragRect);
    }

    /// <summary>
    /// Renders log entries into the current GUILayout context (no window chrome).
    /// Called by AudioPillar inside the Dev Hub window.
    /// </summary>
    public static void DrawEntries(int maxVisible, double fadeDuration, bool hideModded)
    {
        UpdateEntryLifecycle(maxVisible, fadeDuration);

        int shown = 0;
        GUILayout.BeginVertical();
        for (int i = 0; i < AudioPlayEntries.Count; i++)
        {
            var entry = AudioPlayEntries[i];
            if (hideModded && File.Exists(Path.Combine(AudioHandler.SoundFolder, entry.ClipName + ".wav")))
                continue;

            float opacity = i < maxVisible ? 1f : entry.GetFadeOpacity(fadeDuration);
            UnityEngine.GUI.contentColor = new Color(1f, 1f, 1f, opacity);
            GUILayout.Label(entry.ClipName, GUIHelper.LabelStyle);
            shown++;
        }
        if (shown == 0)
        {
            UnityEngine.GUI.contentColor = Color.yellow;
            GUILayout.Label("No audio played recently.", GUIHelper.LabelStyle);
        }
        UnityEngine.GUI.contentColor = Color.white;
        GUILayout.EndVertical();
    }

    private static void UpdateEntryLifecycle(int maxVisible, double fadeDuration)
    {
        for (int i = AudioPlayEntries.Count - 1; i >= maxVisible; i--)
            if (AudioPlayEntries[i].IsFadedOut(fadeDuration))
                AudioPlayEntries.RemoveAt(i);
        for (int i = maxVisible; i < AudioPlayEntries.Count; i++)
            if (AudioPlayEntries[i].BumpedTime == null)
                AudioPlayEntries[i].BumpedTime = DateTime.Now;
        for (int i = 0; i < Math.Min(maxVisible, AudioPlayEntries.Count); i++)
            AudioPlayEntries[i].BumpedTime = null;
    }

    public static void LogAudio(AudioClip clip)
    {
        string cleanName = clip.name.Replace("PATCHWORK_", "");

        // Remove existing entry for this clip so it moves to the top
        var existing = AudioPlayEntries.Find(e => e.ClipName == cleanName);
        if (existing != null)
            AudioPlayEntries.Remove(existing);

        // Insert at front so newest is always on top
        AudioPlayEntries.Insert(0, new AudioPlayEntry
        {
            ClipName = cleanName,
            StartTime = DateTime.Now,
            BumpedTime = null
        });
    }

    public static void ClearLog()
    {
        AudioPlayEntries.Clear();
    }

    internal class AudioPlayEntry
    {
        public string ClipName;
        public DateTime StartTime;
        public DateTime? BumpedTime;

        public float GetFadeOpacity(double fadeDuration)
        {
            if (BumpedTime == null) return 1.0f;
            var elapsed = (DateTime.Now - BumpedTime.Value).TotalSeconds;
            if (elapsed >= fadeDuration) return 0.0f;
            return 1.0f - (float)(elapsed / fadeDuration);
        }

        public bool IsFadedOut(double fadeDuration)
        {
            if (BumpedTime == null) return false;
            return (DateTime.Now - BumpedTime.Value).TotalSeconds >= fadeDuration;
        }
    }
}