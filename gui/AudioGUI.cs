using System;
using System.Collections.Generic;
using UnityEngine;

namespace Patchwork.GUI;

public static class AudioGUI
{
    private static readonly Dictionary<string, AudioPlayEntry> AudioPlayLog = new();

    public static void DrawAudioLog()
    {
        GUILayout.Window(6969, new Rect(
            Screen.width - (Screen.width / 8),
            10,
            Screen.width / 8,
            Screen.height / 16),
            AudioLogWindow,
            "Patchwork Audio Log"
        );
    }

    private static void AudioLogWindow(int windowID)
    {
        GUILayout.BeginVertical();

        if (AudioPlayLog.Count == 0)
        {
            GUILayout.Label("No audio played recently.");
            GUILayout.EndVertical();
            UnityEngine.GUI.DragWindow(new Rect(0, 0, 10000, 20));
            return;
        }

        List<AudioPlayEntry> sortedEntries = new(AudioPlayLog.Values);
        sortedEntries.Sort((a, b) => a.ClipName.CompareTo(b.ClipName));

        foreach (var entry in sortedEntries)
        {
            if (entry.IsExpired())
            {
                AudioPlayLog.Remove(entry.ClipName);
                continue;
            }

            var opacity = entry.GetOpacity();
            var color = entry.Loaded ? new Color(0f, 1f, 0f, opacity) : new Color(1.0f, 1.0f, 1.0f, opacity);
            UnityEngine.GUI.contentColor = color;
            GUILayout.Label($"{entry.ClipName}");
        }
        UnityEngine.GUI.contentColor = Color.white;
        GUILayout.EndVertical();

        UnityEngine.GUI.DragWindow(new Rect(0, 0, 10000, 20));
    }

    public static void LogAudio(AudioClip clip, bool loaded = false)
    {
        AudioPlayLog[clip.name] = new AudioPlayEntry
        {
            ClipName = clip.name,
            StartTime = DateTime.Now,
            Loaded = loaded
        };
    }
    
    public static void ClearLog()
    {
        AudioPlayLog.Clear();
    }

    internal class AudioPlayEntry
    {
        public string ClipName;
        public DateTime StartTime;
        public bool Loaded;

        public float GetOpacity() => 1.0f - (float)((DateTime.Now - StartTime).TotalSeconds / Plugin.Config.LogAudioDuration);
        public bool IsExpired() => DateTime.Now >= StartTime.AddSeconds(Plugin.Config.LogAudioDuration);
    }
}