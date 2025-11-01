using System;
using System.Collections.Generic;
using UnityEngine;

namespace Patchwork.GUI;

public static class AudioGUI
{
    private static Dictionary<string, AudioPlayEntry> audioPlayLog = new();

    public static void DrawAudioLog()
    {
        if (audioPlayLog.Count == 0)
            return;

        GUILayout.BeginVertical();

        UnityEngine.GUI.contentColor = Color.yellow;
        GUILayout.Label("Audio Play Log:");

        List<AudioPlayEntry> sortedEntries = new(audioPlayLog.Values);
        sortedEntries.Sort((a, b) => a.ClipName.CompareTo(b.ClipName));

        foreach (var entry in sortedEntries)
        {
            if (entry.IsExpired())
            {
                audioPlayLog.Remove(entry.ClipName);
                continue;
            }

            var opacity = entry.GetOpacity();
            var color = entry.Loaded ? new Color(0f, 1f, 0f, opacity) : new Color(1.0f, 1.0f, 1.0f, opacity);
            UnityEngine.GUI.contentColor = color;
            GUILayout.Label($"{entry.ClipName}");
        }
        UnityEngine.GUI.contentColor = Color.white;
        GUILayout.EndVertical();
    }

    public static void LogAudio(AudioClip clip, bool loaded = false)
    {
        audioPlayLog[clip.name] = new AudioPlayEntry
        {
            ClipName = clip.name,
            StartTime = DateTime.Now,
            Loaded = loaded,
            EndTime = DateTime.Now.AddSeconds(clip.length)
        };
    }
    
    public static void ClearLog()
    {
        audioPlayLog.Clear();
    }

    internal class AudioPlayEntry
    {
        public string ClipName;
        public DateTime StartTime;
        public DateTime EndTime;
        public bool Loaded;

        public float GetOpacity()
        {
            var totalDuration = (EndTime - StartTime).TotalSeconds;
            if (totalDuration < 2.0)
                totalDuration = 2.0;
            var elapsed = (DateTime.Now - StartTime).TotalSeconds;
            return 1.0f - (float)(elapsed / totalDuration);
        }

        public bool IsExpired()
        {
            var endTime = (EndTime - StartTime).TotalSeconds < 2.0 ? StartTime.AddSeconds(2.0) : EndTime;
            return DateTime.Now >= endTime;
        }
    }
}