using System;
using System.Collections.Generic;
using System.IO;
using Patchwork.Handlers;
using UnityEngine;

namespace Patchwork.GUI;

public static class AudioLog
{
    private static readonly List<AudioPlayEntry> AudioPlayEntries = new();

    // Queued by log-row click — applied on the next Draw pass.
    private static string _pendingFocusClip = null;

    /// <summary>
    /// Clip name the log wants the browser to focus on.
    /// Consumed and cleared by AudioPillar each frame.
    /// </summary>
    public static string ConsumePendingFocus()
    {
        var v = _pendingFocusClip;
        _pendingFocusClip = null;
        return v;
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

            GUILayout.BeginHorizontal();

            // Clip name — click focuses browser
            UnityEngine.GUI.contentColor = new Color(1f, 1f, 1f, opacity);
            if (GUILayout.Button(entry.ClipName, GUIHelper.LabelStyle, GUILayout.ExpandWidth(true)))
                _pendingFocusClip = entry.ClipName;

            // Source path — dimmed, right-aligned
            if (!string.IsNullOrEmpty(entry.SourcePath))
            {
                UnityEngine.GUI.contentColor = new Color(0.55f, 0.55f, 0.55f, opacity);
                GUILayout.Label("← " + entry.SourcePath, GUIHelper.LabelStyle,
                    GUILayout.ExpandWidth(false));
            }

            UnityEngine.GUI.contentColor = Color.white;
            GUILayout.EndHorizontal();

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

    public static void LogAudio(AudioClip clip, AudioSource source)
    {
        string cleanName = clip.name.Replace("PATCHWORK_", "");
        string srcPath   = source != null ? GetGameObjectPath(source) : "";

        // Move to top; update source path if it's new/different
        var existing = AudioPlayEntries.Find(e => e.ClipName == cleanName);
        if (existing != null)
        {
            AudioPlayEntries.Remove(existing);
            // Keep the most-recent source path
            if (!string.IsNullOrEmpty(srcPath))
                existing.SourcePath = srcPath;
            existing.StartTime  = DateTime.Now;
            existing.BumpedTime = null;
            AudioPlayEntries.Insert(0, existing);
        }
        else
        {
            AudioPlayEntries.Insert(0, new AudioPlayEntry
            {
                ClipName   = cleanName,
                SourcePath = srcPath,
                StartTime  = DateTime.Now,
                BumpedTime = null
            });
        }
    }

    public static void ClearLog()
    {
        AudioPlayEntries.Clear();
    }

    private static string GetGameObjectPath(AudioSource src)
    {
        if (src == null) return "";
        var t    = src.transform;
        string n = t.name;
        if (t.parent != null) n = t.parent.name + "/" + n;
        return n;
    }

    internal class AudioPlayEntry
    {
        public string    ClipName;
        public string    SourcePath;
        public DateTime  StartTime;
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
